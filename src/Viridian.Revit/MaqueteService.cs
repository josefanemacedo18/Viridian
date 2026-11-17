using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Detailing;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Resultado da geração da maquete.</summary>
public sealed class MaqueteResult
{
    public View3D View { get; init; } = null!;

    public int Plants { get; set; }

    public int Beds { get; set; }

    /// <summary>Exemplares deixados de fora pelo limite configurado.</summary>
    public int Skipped { get; set; }

    /// <summary>Elementos cuja geometria o Revit recusou (detalhe no registro de erros).</summary>
    public int Failed { get; set; }

    /// <summary>Elementos assentados sobre o terreno encontrado.</summary>
    public int OnTerrain { get; set; }

    public bool UsedWorkset { get; set; }

    public bool HiddenInPlans { get; set; }
}

/// <summary>
/// Maquete 3D da vegetação: formas diretas (DirectShape) sem família, na categoria Entorno, com materiais nas cores das
/// espécies. As formas têm representação vazia em planta, de modo que as plantas baixas continuam mostrando só os
/// símbolos 2D. A maquete é opcional: pode ser refeita ou removida a qualquer momento sem alterar a vegetação.
/// </summary>
public static class MaqueteService
{
    public const string ApplicationId = "Viridian.Maquete";
    public const string ViewName = "Viridian - Maquete 3D";
    public const string WorksetName = "VIR Maquete 3D";
    public const string KindMaquete = "Maquete";
    private const string MaterialPrefix = "VIR Maquete - ";

    /// <summary>Formas da maquete existentes no documento.</summary>
    public static List<DirectShape> Shapes(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(DirectShape)).Cast<DirectShape>()
            .Where(d => string.Equals(d.ApplicationId, ApplicationId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Vista da maquete (marcada pelo Viridian ou, na falta da marca, pelo nome).</summary>
    public static View3D? FindView(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        List<View3D> views = collector.OfClass(typeof(View3D)).Cast<View3D>().Where(v => !v.IsTemplate).ToList();
        return views.FirstOrDefault(v => ElementData.KindOf(v) == KindMaquete)
               ?? views.FirstOrDefault(v => string.Equals(v.Name, ViewName, StringComparison.OrdinalIgnoreCase));
    }

    public static MaqueteState? ReadState(View3D? view) =>
        view is not null && ElementData.TryRead(view, KindMaquete, out MaqueteState state) ? state : null;

    /// <summary>Gera (ou refaz) a maquete. Substitui a maquete anterior por inteiro. Precisa de transação aberta.</summary>
    public static MaqueteResult Build(Document document, MaqueteState state, Func<string, Species?> lookup, Func<Species, string> codeOf, ViridianSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        ModelSettings options = settings.Model3D;
        View3D view = EnsureView(document);
        PlanHiding = true;
        DeleteShapes(document);
        Try("caixa de corte anterior", () => view.IsSectionBoxActive = false); // a busca do terreno só enxerga o que a vista mostra
        document.Regenerate();

        var result = new MaqueteResult { View = view };
        Polygon2? region = state.RegionPolygon();
        if (region is { IsEmpty: true })
        {
            region = null;
        }

        string level = region is null ? string.Empty : state.LevelName;
        var ground = new GroundProbe(document, view, options);
        var materials = new MaterialCache(document);
        ElementId categoryId = ShapeCategory(document);
        WorksetId? workset = EnsureWorkset(document, view);
        result.UsedWorkset = workset is not null;
        var lineColors = new Dictionary<ElementId, Rgb>();

        // Maciços: os baixos viram volume; os de árvores e palmeiras viram exemplares (os símbolos do maciço ou, na falta deles, uma malha).
        List<MassingInfo> massings = ReportBuilder.DistinctMassings(MassingService.ReadMassings(document))
            .Where(m => m.Boundary is { IsEmpty: false } && lookup(m.Record.SpeciesId) is not null)
            .Where(m => region is null || (region.Contains(m.Boundary!.InteriorPoint()) && LevelMatches(document, m.ViewId, level)))
            .ToList();
        var bedMassings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<(string Source, Species Species, XYZ Base, double ZMeters, bool Probe)>();

        IReadOnlyList<PlantRecord> plants = PlantingService.ReadPlants(document, lookup);
        foreach (MassingInfo m in massings)
        {
            Species species = lookup(m.Record.SpeciesId)!;
            if (species.Category is PlantCategory.Arvore or PlantCategory.Palmeira)
            {
                if (!plants.Any(p => string.Equals(p.MassingId, m.Record.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    double z = LevelElevation(document, m.ViewId);
                    DistributionResult points = PlantDistributor.Distribute(m.Boundary, new DistributionRequest
                    {
                        Pattern = DistributionPattern.Triangular,
                        SpacingM = species.EffectiveSpacingM,
                        Seed = DeterministicRandom.StableHash(m.Record.Id),
                        MaxCount = options.MaxPlants,
                    });
                    int index = 0;
                    foreach (Point2 p in points.Points)
                    {
                        items.Add(($"{m.Record.Id}#{index++}", species, RevitUnits.ToXyz(p, RevitUnits.ToFeet(z)), z, true));
                    }
                }

                continue;
            }

            if (BuildBed(document, m, species, state.Stage, ground, materials, categoryId, workset, codeOf, settings, lineColors))
            {
                result.Beds++;
                bedMassings.Add(m.Record.Id);
                if (ground.LastBedOnTerrain)
                {
                    result.OnTerrain++;
                }
            }
            else
            {
                result.Failed++;
            }
        }

        foreach (PlantRecord plant in plants)
        {
            if (!plant.IsRegistered || plant.Status == PlantStatus.Removido || lookup(plant.SpeciesId) is not Species species)
            {
                continue;
            }

            if (plant.BelongsToMassing && bedMassings.Contains(plant.MassingId))
            {
                continue; // representado pelo volume do maciço
            }

            if (region is not null && (!region.Contains(plant.Location) || (level.Length > 0 && !string.Equals(plant.LevelName, level, StringComparison.Ordinal))))
            {
                continue;
            }

            if (document.GetElement(new ElementId(plant.ElementId)) is not FamilyInstance instance || instance.Location is not LocationPoint location)
            {
                continue;
            }

            items.Add((plant.UniqueId, species, location.Point, RevitUnits.ToMeters(location.Point.Z), true));
        }

        if (items.Count > options.MaxPlants)
        {
            result.Skipped = items.Count - options.MaxPlants;
            items = items.Take(options.MaxPlants).ToList();
        }

        foreach ((string source, Species species, XYZ basePoint, double zMeters, bool probe) in items)
        {
            double z = zMeters;
            var plan = RevitUnits.ToPoint2(basePoint);
            if (probe && ground.TryGround(plan, zMeters, out double terrain))
            {
                z = terrain;
                result.OnTerrain++;
            }

            if (BuildPlant(document, source, species, new XYZ(basePoint.X, basePoint.Y, RevitUnits.ToFeet(z)), state.Stage, materials, categoryId, workset, codeOf, settings, lineColors))
            {
                result.Plants++;
            }
            else
            {
                result.Failed++;
            }
        }

        result.HiddenInPlans = PlanHiding;
        ConfigureView(document, view, region, lineColors);
        ElementData.Write(view, KindMaquete, state);
        return result;
    }

    /// <summary>Remove as formas da maquete, os materiais criados para ela e a vista (quando não está em uma prancha).</summary>
    public static (int Shapes, bool ViewDeleted) Remove(Document document)
    {
        List<ElementId> ids = Shapes(document).Select(s => s.Id).ToList();
        if (ids.Count > 0)
        {
            document.Delete(ids);
        }

        bool viewDeleted = false;
        View3D? view = FindView(document);
        if (view is not null)
        {
            using var viewports = new FilteredElementCollector(document);
            bool onSheet = viewports.OfClass(typeof(Viewport)).Cast<Viewport>().Any(v => v.ViewId == view.Id);
            if (onSheet)
            {
                ElementData.Clear(view);
            }
            else if (document.ActiveView?.Id != view.Id)
            {
                document.Delete(view.Id);
                viewDeleted = true;
            }
            else
            {
                ElementData.Clear(view);
            }
        }

        using var collector = new FilteredElementCollector(document);
        List<ElementId> materials = collector.OfClass(typeof(Material)).Cast<Material>()
            .Where(m => m.Name.StartsWith(MaterialPrefix, StringComparison.Ordinal)).Select(m => m.Id).ToList();
        foreach (ElementId id in materials)
        {
            try
            {
                document.Delete(id);
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Material da maquete não pôde ser removido: " + id, ex);
            }
        }

        return (ids.Count, viewDeleted);
    }

    private static void DeleteShapes(Document document)
    {
        List<ElementId> ids = Shapes(document).Select(s => s.Id).ToList();
        if (ids.Count > 0)
        {
            document.Delete(ids);
        }
    }

    // ---- vista ------------------------------------------------------------------------------------------------

    private static View3D EnsureView(Document document)
    {
        View3D? view = FindView(document);
        if (view is not null)
        {
            return view;
        }

        using var collector = new FilteredElementCollector(document);
        ViewFamilyType type = collector.OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                                  .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional)
                              ?? throw new ViridianException("O projeto não tem um tipo de vista 3D.");
        view = View3D.CreateIsometric(document, type.Id);
        view.ViewTemplateId = ElementId.InvalidElementId;
        try
        {
            view.Name = UniqueViewName(document, ViewName);
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Nome da vista da maquete", ex);
        }

        ElementData.Write(view, KindMaquete, new MaqueteState());
        return view;
    }

    private static string UniqueViewName(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        var names = new HashSet<string>(collector.OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
        {
            return name;
        }

        for (int i = 2; i < 1000; i++)
        {
            if (!names.Contains($"{name} ({i})"))
            {
                return $"{name} ({i})";
            }
        }

        return name + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    private static void ConfigureView(Document document, View3D view, Polygon2? region, Dictionary<ElementId, Rgb> lineColors)
    {
        bool templated = view.ViewTemplateId != ElementId.InvalidElementId;
        Try("estilo visual", () =>
        {
            if (!templated && view.CanModifyDisplayStyle())
            {
                view.DisplayStyle = DisplayStyle.ShadingWithEdges;
            }
        });
        Try("nível de detalhe", () =>
        {
            if (!templated && view.CanModifyDetailLevel())
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
        });
        Try("sombras", () =>
        {
            Parameter? shadows = view.GetParameter(ParameterTypeId.GraphicDisplayOptionsShadows);
            if (!templated && shadows is { IsReadOnly: false })
            {
                shadows.Set(1);
            }
        });
        Try("categoria Entorno visível", () =>
        {
            if (!templated)
            {
                view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Entourage), false);
            }
        });

        // Arestas das malhas no tom da folhagem, para não riscar as copas.
        foreach ((ElementId id, Rgb color) in lineColors)
        {
            Try("cor das arestas", () => view.SetElementOverrides(id, new OverrideGraphicSettings().SetProjectionLineColor(HatchService.ToColor(color))));
        }

        Try("caixa de corte", () =>
        {
            if (region is null)
            {
                view.IsSectionBoxActive = false;
                return;
            }

            // Recorte em torno da região escolhida, com folga para as copas e para o entorno imediato.
            Bounds2 b = region.Bounds.Expand(5);
            List<double> z = lineColors.Keys.Select(document.GetElement).Where(e => e is not null)
                .Select(e => e!.get_BoundingBox(null)).Where(x => x is not null)
                .SelectMany(x => new[] { x!.Min.Z, x.Max.Z }).ToList();
            double minZ = z.Count > 0 ? z.Min() - RevitUnits.ToFeet(3) : -RevitUnits.ToFeet(5);
            double maxZ = z.Count > 0 ? z.Max() + RevitUnits.ToFeet(3) : RevitUnits.ToFeet(30);
            view.SetSectionBox(new BoundingBoxXYZ
            {
                Min = new XYZ(RevitUnits.ToFeet(b.MinX), RevitUnits.ToFeet(b.MinY), minZ),
                Max = new XYZ(RevitUnits.ToFeet(b.MaxX), RevitUnits.ToFeet(b.MaxY), maxZ),
            });
            view.IsSectionBoxActive = true;
        });
    }

    // ---- elementos --------------------------------------------------------------------------------------------

    /// <summary>Verdadeiro enquanto a representação vazia em planta estiver sendo aceita pelo Revit.</summary>
    private static bool PlanHiding { get; set; } = true;

    private static bool BuildPlant(Document document, string source, Species species, XYZ basePoint, GrowthStage stage, MaterialCache materials,
        ElementId categoryId, WorksetId? workset, Func<Species, string> codeOf, ViridianSettings settings, Dictionary<ElementId, Rgb> lineColors)
    {
        try
        {
            (double crown, double height) = GrowthModel.Size(species, stage);
            int seed = DeterministicRandom.StableHash(source);
            bool flower = settings.Model3D.ShowFlowers && !string.IsNullOrWhiteSpace(species.FlowerColor);
            PlantModel model = PlantModels.Build(species, crown, height, seed, flower);
            double rotation = seed % 3600 / 3600.0 * Geo.TwoPi;
            var offset = new Vec3(RevitUnits.ToMeters(basePoint.X), RevitUnits.ToMeters(basePoint.Y), RevitUnits.ToMeters(basePoint.Z));
            List<MeshPiece> pieces = model.Pieces.Select(p => p.Transform(rotation, offset)).ToList();
            Rgb foliage = VegetationPalette.Foliage(species, settings.Colors);
            return CreateShape(document, source, species, codeOf, pieces, part => materials.For(part, species, foliage, settings),
                categoryId, workset, basePoint, foliage, lineColors);
        }
        catch (Exception ex)
        {
            ViridianLog.Warn($"Maquete: exemplar {source} ({species.PopularName}) não pôde ser criado", ex);
            return false;
        }
    }

    private static bool BuildBed(Document document, MassingInfo massing, Species species, GrowthStage stage, GroundProbe ground, MaterialCache materials,
        ElementId categoryId, WorksetId? workset, Func<Species, string> codeOf, ViridianSettings settings, Dictionary<ElementId, Rgb> lineColors)
    {
        try
        {
            double levelZ = LevelElevation(document, massing.ViewId);
            (_, double height) = GrowthModel.Size(species, stage);
            if (species.Category == PlantCategory.Gramado)
            {
                height = Math.Min(height, 0.08);
            }

            int hits = 0, probes = 0;
            MeshPiece bed = BedModels.Build(massing.Boundary!, p =>
            {
                probes++;
                if (ground.TryGround(p, levelZ, out double z))
                {
                    hits++;
                    return z;
                }

                return levelZ;
            }, height);
            ground.LastBedOnTerrain = probes > 0 && hits * 2 >= probes;
            if (bed.TriangleCount == 0)
            {
                ViridianLog.Warn($"Maquete: o contorno do maciço {massing.Record.Id} não pôde ser triangulado.");
                return false;
            }

            var pieces = new List<MeshPiece> { bed };
            if (settings.Model3D.ShowFlowers && !string.IsNullOrWhiteSpace(species.FlowerColor))
            {
                double blob = Math.Clamp(species.CrownDiameterM * 0.15, 0.05, 0.25);
                int seed = DeterministicRandom.StableHash(massing.Record.Id);
                int index = 0;
                foreach (Vec3 p in BedModels.FlowerPoints(bed, massing.Boundary!, Math.Max(0.6, species.EffectiveSpacingM * 1.5), 150, seed))
                {
                    pieces.Add(Primitives.Blob(MeshPart.Flor, p + new Vec3(0, 0, blob * 0.3), blob, index++ * 0.7));
                }
            }

            Element? region = document.GetElement(new ElementId(massing.ElementId));
            string source = region?.UniqueId ?? massing.Record.Id;
            Rgb foliage = VegetationPalette.Foliage(species, settings.Colors);
            XYZ anchor = RevitUnits.ToXyz(massing.Boundary!.InteriorPoint(), RevitUnits.ToFeet(levelZ));
            return CreateShape(document, source, species, codeOf, pieces, part => materials.For(part, species, foliage, settings),
                categoryId, workset, anchor, foliage, lineColors, massing.Record.Id);
        }
        catch (Exception ex)
        {
            ViridianLog.Warn($"Maquete: maciço {massing.Record.Id} não pôde ser criado", ex);
            return false;
        }
    }

    private static bool CreateShape(Document document, string source, Species species, Func<Species, string> codeOf, IReadOnlyList<MeshPiece> pieces,
        Func<MeshPart, ElementId> material, ElementId categoryId, WorksetId? workset, XYZ anchor, Rgb foliage, Dictionary<ElementId, Rgb> lineColors, string massingId = "")
    {
        var builder = new TessellatedShapeBuilder
        {
            Target = TessellatedShapeBuilderTarget.AnyGeometry,
            Fallback = TessellatedShapeBuilderFallback.Mesh,
        };
        foreach (MeshPiece piece in pieces.Where(p => p.TriangleCount > 0))
        {
            ElementId materialId = material(piece.Part);
            builder.OpenConnectedFaceSet(piece.Closed);
            foreach (Tri t in piece.Triangles)
            {
                (Vec3 a, Vec3 b, Vec3 c) = piece.Corners(t);
                builder.AddFace(new TessellatedFace(new List<XYZ> { Feet(a), Feet(b), Feet(c) }, materialId));
            }

            builder.CloseConnectedFaceSet();
        }

        builder.Build();
        using TessellatedShapeBuilderResult built = builder.GetBuildResult();
        if (built.Outcome == TessellatedShapeBuilderOutcome.Nothing || !built.AreObjectsAvailable)
        {
            ViridianLog.Warn($"Maquete: o Revit não aceitou a geometria de {species.PopularName} ({source}).");
            return false;
        }

        IList<GeometryObject> geometry = built.GetGeometricalObjects();
        DirectShape shape = DirectShape.CreateElement(document, categoryId);
        shape.ApplicationId = ApplicationId;
        shape.ApplicationDataId = source;
        shape.SetShape(geometry);
        HideInPlans(shape, anchor);

        string code = codeOf(species);
        string name = (code.Length > 0 ? code + " " : string.Empty) + species.PopularName;
        Try("nome", () => shape.SetName(name));
        Try("comentários", () =>
            shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(
                $"Maquete Viridian: {name} ({species.ScientificName})" + (massingId.Length > 0 ? " - maciço " + massingId : string.Empty)));
        if (workset is not null)
        {
            Try("subprojeto", () => shape.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM)?.Set(workset.IntegerValue));
        }

        lineColors[shape.Id] = foliage.Shade(0.3);
        return true;
    }

    /// <summary>Representação em planta reduzida a um ponto (invisível): as plantas baixas não mostram a maquete.</summary>
    private static void HideInPlans(DirectShape shape, XYZ anchor)
    {
        if (!PlanHiding)
        {
            return;
        }

        try
        {
            var plan = new List<GeometryObject> { Point.Create(anchor) };
            if (shape.IsValidShape(plan, DirectShapeTargetViewType.Plan))
            {
                shape.SetShape(plan, DirectShapeTargetViewType.Plan);
                return;
            }

            ViridianLog.Warn("Maquete: o Revit não aceitou a representação em planta das formas.");
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: representação em planta", ex);
        }

        PlanHiding = false;
    }

    private static XYZ Feet(Vec3 v) => new(RevitUnits.ToFeet(v.X), RevitUnits.ToFeet(v.Y), RevitUnits.ToFeet(v.Z));

    private static ElementId ShapeCategory(Document document)
    {
        var entourage = new ElementId(BuiltInCategory.OST_Entourage);
        return DirectShape.IsValidCategoryId(entourage, document) ? entourage : new ElementId(BuiltInCategory.OST_GenericModel);
    }

    /// <summary>Em projetos com subprojetos, as formas ficam num subprojeto oculto por padrão nas demais vistas.</summary>
    private static WorksetId? EnsureWorkset(Document document, View3D view)
    {
        if (!document.IsWorkshared)
        {
            return null;
        }

        try
        {
            Workset? workset = new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => string.Equals(w.Name, WorksetName, StringComparison.OrdinalIgnoreCase));
            if (workset is null)
            {
                workset = Workset.Create(document, WorksetName);
                WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(document)?.SetWorksetVisibility(workset.Id, false);
            }

            view.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);
            return workset.Id;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: subprojeto não pôde ser preparado", ex);
            return null;
        }
    }

    private static double LevelElevation(Document document, long viewId) =>
        document.GetElement(new ElementId(viewId)) is ViewPlan { GenLevel: Level level } ? RevitUnits.ToMeters(level.ProjectElevation) : 0;

    private static bool LevelMatches(Document document, long viewId, string level) =>
        level.Length == 0 || (document.GetElement(new ElementId(viewId)) is ViewPlan { GenLevel: Level l } && string.Equals(l.Name, level, StringComparison.Ordinal));

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: " + what, ex);
        }
    }

    // ---- terreno e materiais ----------------------------------------------------------------------------------

    /// <summary>
    /// Cota de apoio de cada ponto: a superfície mais alta do terreno (toposólido ou superfície topográfica, também em
    /// vínculos) dentro da faixa de busca, a menos que haja uma laje ou cobertura entre o nível e o terreno (jardim sobre
    /// laje, cobertura verde, pátio pavimentado); nesse caso a planta fica sobre a laje.
    /// </summary>
    private sealed class GroundProbe
    {
        private const double StructureClearance = 0.5;
        private readonly ReferenceIntersector? _terrain;
        private readonly ReferenceIntersector? _structure;
        private readonly double _search;

        public GroundProbe(Document document, View3D view, ModelSettings settings)
        {
            _search = settings.TerrainSearchM;
            if (!settings.FollowTerrain)
            {
                return;
            }

            try
            {
                var terrain = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Toposolid, BuiltInCategory.OST_Topography });
                _terrain = new ReferenceIntersector(terrain, FindReferenceTarget.Face, view) { FindReferencesInRevitLinks = true };
                var structure = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs });
                _structure = new ReferenceIntersector(structure, FindReferenceTarget.Face, view) { FindReferencesInRevitLinks = true };
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Maquete: busca do terreno indisponível", ex);
            }
        }

        public bool LastBedOnTerrain { get; set; }

        /// <summary>Cota (m) sobre a qual a planta se apoia; falso quando nada foi encontrado e vale a cota do nível.</summary>
        public bool TryGround(Point2 plan, double levelZ, out double z)
        {
            z = levelZ;
            if (_terrain is null)
            {
                return false;
            }

            try
            {
                double? terrain = Hit(_terrain, plan, levelZ + _search, _search * 2);
                double floor = terrain ?? levelZ - 1.0;
                double? structure = _structure is null ? null : Hit(_structure, plan, levelZ + StructureClearance, levelZ + StructureClearance - floor);
                if (structure is double slab && (terrain is null || slab >= terrain.Value - 0.05))
                {
                    z = slab;
                    return true;
                }

                if (terrain is double t)
                {
                    z = t;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Maquete: consulta ao terreno", ex);
                return false;
            }
        }

        private static double? Hit(ReferenceIntersector intersector, Point2 plan, double originZ, double maxDistance)
        {
            if (maxDistance <= 0)
            {
                return null;
            }

            ReferenceWithContext? hit = intersector.FindNearest(RevitUnits.ToXyz(plan, RevitUnits.ToFeet(originZ)), -XYZ.BasisZ);
            if (hit is null)
            {
                return null;
            }

            double distance = RevitUnits.ToMeters(hit.Proximity);
            return distance <= maxDistance ? originZ - distance : null;
        }
    }

    private sealed class MaterialCache
    {
        private readonly Document _document;
        private readonly Dictionary<string, ElementId> _byName = new(StringComparer.Ordinal);

        public MaterialCache(Document document)
        {
            _document = document;
            using var collector = new FilteredElementCollector(document);
            foreach (Material m in collector.OfClass(typeof(Material)).Cast<Material>())
            {
                if (m.Name.StartsWith(MaterialPrefix, StringComparison.Ordinal))
                {
                    _byName[m.Name] = m.Id;
                }
            }
        }

        public ElementId For(MeshPart part, Species species, Rgb foliage, ViridianSettings settings)
        {
            (string label, Rgb color) = part switch
            {
                MeshPart.Tronco => ("Tronco", Rgb.Parse(settings.Model3D.TrunkColor, new Rgb(0x6B, 0x4F, 0x3A))),
                MeshPart.Flor => ("Flor", Rgb.Parse(species.FlowerColor, Rgb.White)),
                _ => ("Folhagem", foliage),
            };
            string name = $"{MaterialPrefix}{label} {color.Hex}";
            if (_byName.TryGetValue(name, out ElementId? id) && _document.GetElement(id) is Material)
            {
                return id;
            }

            ElementId created = Material.Create(_document, name);
            if (_document.GetElement(created) is Material material)
            {
                material.Color = HatchService.ToColor(color);
                material.UseRenderAppearanceForShading = false;
                material.Shininess = part == MeshPart.Flor ? 20 : 5;
                material.Smoothness = 30;
            }

            _byName[name] = created;
            return created;
        }
    }
}
