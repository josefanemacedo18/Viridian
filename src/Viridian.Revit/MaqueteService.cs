using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Detailing;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;
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

    public MaqueteEngine Engine { get; set; }

    /// <summary>Triângulos criados nos exemplares (motor botânico).</summary>
    public long Triangles { get; set; }

    /// <summary>Exemplares com detalhe reduzido para caber no orçamento.</summary>
    public int Reduced { get; set; }

    public int Leafless { get; set; }

    public int Sparse { get; set; }

    public int Flowering { get; set; }

    /// <summary>Exemplares com flores cuja época de floração foi estimada pelo gênero.</summary>
    public int FloweringEstimated { get; set; }

    public int Fruiting { get; set; }

    /// <summary>Espécies com perfil 3D escolhido na ficha (as demais usam o automático).</summary>
    public int ChosenProfiles { get; set; }

    /// <summary>Exemplares mostrados pela própria família (vegetação ilustrada em 3D dentro dela).</summary>
    public int FamilyPlants { get; set; }

    /// <summary>Exemplares com família ilustrada fora da cota do nível (terreno): 3D da família desligado e forma direta no terreno.</summary>
    public int TerrainPlants { get; set; }

    /// <summary>Exemplares com família ilustrada fora do terreno cuja família não permite desligar o 3D (ficam na cota do nível).</summary>
    public int OffTerrainKept { get; set; }
}

/// <summary>Contagem prévia da maquete (antes de gerar).</summary>
public sealed record MaqueteEstimate(int Plants, int Beds, int Skipped, long Triangles, IReadOnlyList<Species> Species);

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

    /// <summary>Estado gravado na vista; maquetes de versões anteriores continuam no gerador simples.</summary>
    public static MaqueteState? ReadState(View3D? view, bool legacyFlowers = true) =>
        view is not null && ElementData.TryRead(view, KindMaquete, out MaqueteState state) ? state.Upgrade(legacyFlowers) : null;

    /// <summary>Exemplar a gerar: origem (identificador permanente), espécie, ponto e se o terreno deve ser procurado.</summary>
    private sealed record PlannedPlant(string Source, Species Species, XYZ Base, double ZMeters, string MassingId, long ElementId = 0);

    /// <summary>O que entra na maquete: maciços baixos (volumes) e exemplares, já filtrados pela região e pelo nível.</summary>
    private sealed record MaquetePlan(List<MassingInfo> Beds, List<PlannedPlant> Plants);

    private static MaquetePlan Plan(Document document, MaqueteState state, Func<string, Species?> lookup, ModelSettings options)
    {
        Polygon2? region = state.RegionPolygon();
        if (region is { IsEmpty: true })
        {
            region = null;
        }

        string level = region is null ? string.Empty : state.LevelName;
        List<MassingInfo> massings = ReportBuilder.DistinctMassings(MassingService.ReadMassings(document))
            .Where(m => m.Boundary is { IsEmpty: false } && lookup(m.Record.SpeciesId) is not null)
            .Where(m => region is null || (region.Contains(m.Boundary!.InteriorPoint()) && LevelMatches(document, m.ViewId, level)))
            .ToList();
        var beds = new List<MassingInfo>();
        var items = new List<PlannedPlant>();
        IReadOnlyList<PlantRecord> plants = PlantingService.ReadPlants(document, lookup);

        // Maciços: os baixos viram volume; os de árvores e palmeiras viram exemplares (os símbolos do maciço ou, na falta deles, uma malha).
        foreach (MassingInfo m in massings)
        {
            Species species = lookup(m.Record.SpeciesId)!;

            // Com a vegetação ilustrada ligada, o maciço já aparece nas vistas 3D (tapete e plantas): a maquete não o duplica.
            if (options.IllustratedFamilies && Viridian.Core.Model3D.Illustration.PlantSpritesPlanner.IsIllustrated(species))
            {
                continue;
            }

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
                        items.Add(new PlannedPlant($"{m.Record.Id}#{index++}", species, RevitUnits.ToXyz(p, RevitUnits.ToFeet(z)), z, string.Empty));
                    }
                }

                continue;
            }

            beds.Add(m);
        }

        foreach (PlantRecord plant in plants)
        {
            if (!plant.IsRegistered || plant.Status == PlantStatus.Removido || lookup(plant.SpeciesId) is not Species species)
            {
                continue;
            }

            if (region is not null && (!region.Contains(plant.Location) || (level.Length > 0 && !string.Equals(plant.LevelName, level, StringComparison.Ordinal))))
            {
                continue;
            }

            if (document.GetElement(new ElementId(plant.ElementId)) is not FamilyInstance instance || instance.Location is not LocationPoint location)
            {
                continue;
            }

            items.Add(new PlannedPlant(plant.UniqueId, species, location.Point, RevitUnits.ToMeters(location.Point.Z), plant.BelongsToMassing ? plant.MassingId : string.Empty, plant.ElementId));
        }

        return new MaquetePlan(beds, items);
    }

    /// <summary>Contagem e peso estimados da maquete, sem alterar o modelo (não precisa de transação).</summary>
    public static MaqueteEstimate Estimate(Document document, MaqueteState state, Func<string, Species?> lookup, ViridianSettings settings, IReadOnlyList<Plant3DProfile> profiles)
    {
        MaquetePlan plan = Plan(document, state, lookup, settings.Model3D);
        var bedIds = new HashSet<string>(plan.Beds.Select(b => b.Record.Id), StringComparer.OrdinalIgnoreCase);
        List<Species> species = plan.Plants.Where(p => p.MassingId.Length == 0 || !bedIds.Contains(p.MassingId)).Select(p => p.Species).ToList();
        int skipped = Math.Max(0, species.Count - settings.Model3D.MaxPlants);
        species = species.Take(settings.Model3D.MaxPlants).ToList();
        long triangles = state.Engine == MaqueteEngine.Botanico ? MaqueteCost.Triangles(species, profiles, state.Quality) : species.Count * 300L;
        return new MaqueteEstimate(species.Count, plan.Beds.Count, skipped, triangles, species);
    }

    /// <summary>
    /// Gera (ou refaz) a maquete. Substitui a maquete anterior por inteiro. Precisa de transação aberta: se o motor
    /// botânico falhar na maioria dos exemplares, a geração é interrompida e a transação desfeita (a maquete anterior fica).
    /// </summary>
    public static MaqueteResult Build(Document document, MaqueteState state, Func<string, Species?> lookup, Func<Species, string> codeOf, ViridianSettings settings,
        IReadOnlyList<Plant3DProfile>? profiles = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);
        ModelSettings options = settings.Model3D;
        profiles ??= Plant3DProfileDefaults.Create();
        bool botanical = state.Engine == MaqueteEngine.Botanico;
        View3D view = EnsureView(document);
        PlanHiding = true;
        DeleteShapes(document);
        Try("caixa de corte anterior", () => view.IsSectionBoxActive = false); // a busca do terreno só enxerga o que a vista mostra
        document.Regenerate();

        var result = new MaqueteResult { View = view, Engine = state.Engine };
        Polygon2? region = state.RegionPolygon();
        if (region is { IsEmpty: true })
        {
            region = null;
        }

        MaquetePlan plan = Plan(document, state, lookup, options);
        var ground = new TerrainProbe(document, view, options.FollowTerrain, options.TerrainSearchM);
        var materials = new MaterialCache(document);
        ElementId categoryId = ShapeCategory(document);
        WorksetId? workset = EnsureWorkset(document, view);
        result.UsedWorkset = workset is not null;
        var lineColors = new Dictionary<ElementId, Rgb>();
        var bedMassings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var botanicalContext = botanical ? new BotanicalContext(state, profiles, settings, SouthernHemisphere(document, settings)) : null;

        foreach (MassingInfo m in plan.Beds)
        {
            Species species = lookup(m.Record.SpeciesId)!;
            if (BuildBed(document, m, species, state.Stage, ground, materials, categoryId, workset, codeOf, settings, lineColors, botanicalContext))
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

        // Exemplares de maciços que viraram volume já estão representados.
        List<PlannedPlant> items = plan.Plants.Where(p => p.MassingId.Length == 0 || !bedMassings.Contains(p.MassingId)).ToList();
        if (items.Count > options.MaxPlants)
        {
            result.Skipped = items.Count - options.MaxPlants;
            items = items.Take(options.MaxPlants).ToList();
        }

        int attempted = 0, plantFailures = 0;
        foreach (PlannedPlant item in items)
        {
            double z = item.ZMeters;
            var planPoint = RevitUnits.ToPoint2(item.Base);
            if (ground.TryGround(planPoint, item.ZMeters, out double terrain))
            {
                z = terrain;
                result.OnTerrain++;
            }

            var at = new XYZ(item.Base.X, item.Base.Y, RevitUnits.ToFeet(z));
            // Espécies com vegetação ilustrada na família: a própria família mostra a árvore (a maquete não duplica).
            if (item.ElementId != 0 && FamilyShows(document, item, z, result))
            {
                result.Plants++;
                continue;
            }

            attempted++;
            bool ok = botanicalContext is not null
                ? BuildBotanicalPlant(document, item.Source, item.Species, at, state.Stage, botanicalContext, materials, categoryId, workset, codeOf, lineColors, result)
                : BuildPlant(document, item.Source, item.Species, at, state.Stage, materials, categoryId, workset, codeOf, settings, lineColors);
            if (ok)
            {
                result.Plants++;
                if (item.ElementId != 0 && HasFamilyVolume(document, item.ElementId))
                {
                    result.TerrainPlants++;
                }
            }
            else
            {
                result.Failed++;
                plantFailures++;
            }

            if (botanical && attempted >= 8 && plantFailures * 2 > attempted)
            {
                throw new ViridianException($"O Revit recusou a geometria de {plantFailures} de {attempted} exemplares. A geração foi interrompida e a maquete anterior foi mantida. "
                                            + "Tente o nível Simplificado ou o gerador simples; o detalhe está no registro de erros (Viridian > Sobre).");
            }
        }

        if (botanicalContext is not null)
        {
            result.ChosenProfiles = botanicalContext.ChosenProfiles.Count;
        }

        result.HiddenInPlans = PlanHiding;
        ConfigureView(document, view, region, lineColors, botanical && !options.ShowEdges);
        ElementData.Write(view, KindMaquete, state);
        return result;
    }

    /// <summary>Tolerância entre a cota do exemplar e o terreno para o volume da família representar a planta (m).</summary>
    public const double FamilyTerrainTolerance = 0.15;

    /// <summary>O exemplar é de uma família com a vegetação ilustrada em 3D.</summary>
    private static bool HasFamilyVolume(Document document, long elementId) =>
        document.GetElement(new ElementId(elementId)) is FamilyInstance { Symbol: FamilySymbol symbol }
        && ElementData.TryRead(symbol, ElementData.KindSpecies, out SpeciesLink link) && link.Has3D;

    /// <summary>
    /// Vegetação ilustrada: decide se a própria família representa o exemplar. Exemplares no terreno (ou na cota do
    /// nível, sem terreno) mostram o 3D da família; fora dela, o 3D da família é desligado pelo parâmetro do exemplar e
    /// a maquete cria a forma direta no terreno. Devolve verdadeiro quando nenhuma forma direta é necessária.
    /// </summary>
    private static bool FamilyShows(Document document, PlannedPlant item, double groundZ, MaqueteResult result)
    {
        if (document.GetElement(new ElementId(item.ElementId)) is not FamilyInstance instance || !HasFamilyVolume(document, item.ElementId))
        {
            return false;
        }

        Parameter? visible = instance.LookupParameter(SymbolFamilyBuilder.VisibleParameter);
        bool onGround = Math.Abs(groundZ - item.ZMeters) <= FamilyTerrainTolerance;
        if (onGround || visible is null || visible.IsReadOnly)
        {
            if (visible is { IsReadOnly: false } && visible.AsInteger() != 1)
            {
                visible.Set(1);
            }

            if (!onGround)
            {
                result.OffTerrainKept++;
            }

            result.FamilyPlants++;
            return true;
        }

        if (visible.AsInteger() != 0)
        {
            visible.Set(0);
        }

        return false;
    }

    /// <summary>Hemisfério da obra (para os meses de folhagem, flor e fruto): latitude da localização ou das configurações.</summary>
    public static bool SouthernHemisphere(Document document, ViridianSettings settings)
    {
        try
        {
            return SolarService.ReadSite(document, settings.Solar).Site.LatitudeDeg <= 0;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Maquete: hemisfério da obra", ex);
            return true;
        }
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

    private static void ConfigureView(Document document, View3D view, Polygon2? region, Dictionary<ElementId, Rgb> lineColors, bool withoutEdges)
    {
        bool templated = view.ViewTemplateId != ElementId.InvalidElementId;
        Try("estilo visual", () =>
        {
            if (!templated && view.CanModifyDisplayStyle())
            {
                // Motor botânico: sombreado sem arestas (as folhas e as copas ficam naturais); gerador simples: como antes.
                view.DisplayStyle = withoutEdges ? DisplayStyle.Shading : DisplayStyle.ShadingWithEdges;
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
        Try("cores dos símbolos 2D ocultas", () =>
        {
            // As camadas de cor dos símbolos de planta são superfícies finas no chão: na maquete, só os volumes.
            if (!templated && Category.GetCategory(document, BuiltInCategory.OST_Planting) is Category planting)
            {
                foreach (Core.Symbols.SymbolTier tier in new[] { Core.Symbols.SymbolTier.Fill, Core.Symbols.SymbolTier.Flowers })
                {
                    string name = Core.Symbols.TierVisibility.SubcategoryName(tier);
                    if (planting.SubCategories.Contains(name))
                    {
                        view.SetCategoryHidden(planting.SubCategories.get_Item(name).Id, true);
                    }
                }
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

    /// <summary>Escolhas e caches do motor botânico durante uma geração.</summary>
    private sealed class BotanicalContext
    {
        public BotanicalContext(MaqueteState state, IReadOnlyList<Plant3DProfile> profiles, ViridianSettings settings, bool south)
        {
            Profiles = profiles;
            Settings = settings;
            Options = new PlantOptions
            {
                Quality = state.Quality,
                Month = state.Month,
                SouthernHemisphere = south,
                Flowers = state.Flowers,
                Fruits = state.Fruits,
                Variation = state.Variation,
            };
        }

        public IReadOnlyList<Plant3DProfile> Profiles { get; }

        public ViridianSettings Settings { get; }

        public PlantOptions Options { get; }

        /// <summary>Geometria por hash: exemplares idênticos (sem variação) são gerados uma única vez.</summary>
        public Dictionary<string, GeneratedPlant> Geometry { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (Plant3DProfile Profile, bool FromSpecies)> BySpecies { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ChosenProfiles { get; } = new(StringComparer.Ordinal);

        public Plant3DProfile ProfileOf(Species species)
        {
            if (!BySpecies.TryGetValue(species.Id, out var resolved))
            {
                resolved = ProfileResolver.Resolve(species, Profiles);
                BySpecies[species.Id] = resolved;
                if (resolved.FromSpecies)
                {
                    ChosenProfiles.Add(species.Id);
                }
            }

            return resolved.Profile;
        }
    }

    private static bool BuildBotanicalPlant(Document document, string source, Species species, XYZ basePoint, GrowthStage stage, BotanicalContext context,
        MaterialCache materials, ElementId categoryId, WorksetId? workset, Func<Species, string> codeOf, Dictionary<ElementId, Rgb> lineColors, MaqueteResult result)
    {
        try
        {
            (double crown, double height) = GrowthModel.Size(species, stage);
            int seed = DeterministicRandom.StableHash(source);
            Plant3DProfile profile = context.ProfileOf(species);
            PlantSeason season = PlantPlanner.Season(species, profile, context.Options);
            PlantRequest request = PlantPlanner.Request(species, profile, crown, height, seed, context.Options);
            string hash = GeometryHash.Of(request);
            if (!context.Geometry.TryGetValue(hash, out GeneratedPlant? generated))
            {
                generated = PlantGeometryGenerator.Generate(request);
                context.Geometry[hash] = generated;
            }

            PlantColors colors = PlantPlanner.Colors(species, profile, context.Settings.Colors, context.Settings.Model3D.TrunkColor, request.Fruits);
            double rotation = seed % 3600 / 3600.0 * Geo.TwoPi;
            var offset = new Vec3(RevitUnits.ToMeters(basePoint.X), RevitUnits.ToMeters(basePoint.Y), RevitUnits.ToMeters(basePoint.Z));
            List<MeshPiece> pieces = generated.Model.Pieces.Select(p => p.Transform(rotation, offset)).ToList();
            bool ok = CreateShape(document, source, species, codeOf, pieces, part => materials.For(part, colors.For(part)),
                categoryId, workset, basePoint, colors.Foliage, lineColors);
            if (ok)
            {
                result.Triangles += generated.Model.TriangleCount;
                result.Reduced += generated.DetailFactor < 0.999 ? 1 : 0;
                result.Leafless += request.Foliage == FoliageState.SemFolhas ? 1 : 0;
                result.Sparse += request.Foliage == FoliageState.Rala ? 1 : 0;
                result.Flowering += request.Flowers ? 1 : 0;
                result.FloweringEstimated += request.Flowers && season.FloweringSource == SeasonSource.Estimado ? 1 : 0;
                result.Fruiting += request.Fruits is not null ? 1 : 0;
            }

            return ok;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn($"Maquete: exemplar {source} ({species.PopularName}) não pôde ser criado", ex);
            return false;
        }
    }

    private static bool BuildBed(Document document, MassingInfo massing, Species species, GrowthStage stage, TerrainProbe ground, MaterialCache materials,
        ElementId categoryId, WorksetId? workset, Func<Species, string> codeOf, ViridianSettings settings, Dictionary<ElementId, Rgb> lineColors,
        BotanicalContext? botanical = null)
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
            // Flores do maciço: no gerador simples, conforme as configurações; no botânico, conforme a escolha e o mês.
            bool flowers = botanical is null
                ? settings.Model3D.ShowFlowers && !string.IsNullOrWhiteSpace(species.FlowerColor)
                : PlantPlanner.Season(species, botanical.ProfileOf(species), botanical.Options).Flowers
                  || (botanical.Options.Flowers && botanical.ProfileOf(species).Flowers is null && !string.IsNullOrWhiteSpace(species.FlowerColor)
                      && (botanical.Options.Month == 0 || SeasonalityResolver.InMonth(SeasonalityResolver.Flowering(species), botanical.Options.Month, botanical.Options.SouthernHemisphere)));
            if (flowers)
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

        /// <summary>Material de uma parte no motor botânico: nome pela parte e pela cor (reaproveitado entre espécies).</summary>
        public ElementId For(MeshPart part, Rgb color)
        {
            string label = part switch
            {
                MeshPart.Tronco => "Tronco",
                MeshPart.Galho => "Galho",
                MeshPart.Flor => "Flor",
                MeshPart.Fruto => "Fruto",
                MeshPart.FolhagemClara => "Folhagem clara",
                MeshPart.FolhagemEscura => "Folhagem escura",
                _ => "Folhagem",
            };
            return Get($"{MaterialPrefix}{label} {color.Hex}", color, part is MeshPart.Flor or MeshPart.Fruto ? 20 : 5);
        }

        public ElementId For(MeshPart part, Species species, Rgb foliage, ViridianSettings settings)
        {
            (string label, Rgb color) = part switch
            {
                MeshPart.Tronco => ("Tronco", Rgb.Parse(settings.Model3D.TrunkColor, new Rgb(0x6B, 0x4F, 0x3A))),
                MeshPart.Flor => ("Flor", Rgb.Parse(species.FlowerColor, Rgb.White)),
                _ => ("Folhagem", foliage),
            };
            return Get($"{MaterialPrefix}{label} {color.Hex}", color, part == MeshPart.Flor ? 20 : 5);
        }

        private ElementId Get(string name, Rgb color, int shininess)
        {
            if (_byName.TryGetValue(name, out ElementId? id) && _document.GetElement(id) is Material)
            {
                return id;
            }

            ElementId created = Material.Create(_document, name);
            if (_document.GetElement(created) is Material material)
            {
                material.Color = HatchService.ToColor(color);
                material.UseRenderAppearanceForShading = false;
                material.Shininess = shininess;
                material.Smoothness = 30;
            }

            _byName[name] = created;
            return created;
        }
    }
}
