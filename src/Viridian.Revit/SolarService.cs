using System.IO;
using Autodesk.Revit.DB;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Reports;
using Viridian.Core.Solar;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Planta analisada no estudo solar.</summary>
public sealed record SolarPlant(PlantRecord Record, Species Species, SolarPoint Point);

/// <summary>Resultado do estudo solar de uma região.</summary>
public sealed class SolarReport
{
    public SolarSite Site { get; init; } = new(-23.55, -46.63, -3, string.Empty, SiteSource.Digitado);

    public double LatitudeDeg => Site.LatitudeDeg;

    public IReadOnlyList<SolarDate> Dates { get; init; } = Array.Empty<SolarDate>();

    public List<(Point2 Plan, SolarPoint Point)> Grid { get; } = new();

    public double Cell { get; init; }

    public List<SolarPlant> Plants { get; } = new();

    public bool UsedBuildings { get; init; }

    /// <summary>Copas que fazem sombra (inclusive de plantas fora da região, a até 40 m).</summary>
    public List<CrownObstacle> Crowns { get; } = new();

    /// <summary>Sombras de hora em hora por data analisada.</summary>
    public List<(SolarDate Date, List<ShadeFrame> Frames)> Shades { get; } = new();

    /// <summary>Ângulo do norte verdadeiro nas coordenadas do modelo (radianos, anti-horário).</summary>
    public double TrueNorthRad { get; init; }

    public TimeSpan Duration { get; set; }

    /// <summary>Triângulos lidos das edificações e do terreno (0 = sem edificações consideradas).</summary>
    public int ObstacleTriangles { get; set; }

    /// <summary>Cota do plano do jardim onde as sombras foram projetadas (m).</summary>
    public double PlaneZ { get; set; }

    /// <summary>Horas da malha usadas no mapa (média das datas analisadas).</summary>
    public double MapHours(SolarPoint p) => p.Average;
}

/// <summary>Localização lida do projeto, com a indicação de que parece não ter sido definida.</summary>
/// <param name="TrueNorthDeg">Norte verdadeiro em relação ao topo da planta (graus, anti-horário), o usado no cálculo.</param>
/// <param name="NorthFromPick">O norte foi indicado na planta pelo comando Indicar Norte (e não vem do Revit).</param>
public sealed record ProjectSite(SolarSite Site, bool LooksUndefined, double TrueNorthDeg, bool NorthFromPick = false);

/// <summary>
/// Estudo solar no Revit: posição do sol pela latitude do projeto, sombra das copas da vegetação do Viridian e
/// obstrução por edificações e terreno (também de vínculos), com mapa de horas de sol numa cópia da planta.
/// </summary>
public static class SolarService
{
    public const string ProbeViewName = "Viridian - Estudo solar (temporária)";

    private static readonly BuiltInCategory[] Obstacles =
    {
        BuiltInCategory.OST_Walls, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Mass, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_CurtainWallPanels,
        BuiltInCategory.OST_Toposolid, BuiltInCategory.OST_Topography,
    };

    /// <summary>
    /// Localização do projeto (Gerenciar > Localização): latitude, longitude, fuso e nome. Coordenadas zeradas ou o
    /// local padrão do modelo indicam projeto sem localização definida.
    /// </summary>
    public static ProjectSite ReadSite(Document document, SolarSettings settings)
    {
        (double north, bool picked) = EffectiveNorth(document, settings);
        ProjectSite Site(SolarSite s, bool undefined) => new(s, undefined, north, picked);

        if (!settings.UseProjectLocation)
        {
            // O escritório preferiu as coordenadas das configurações: sem aviso de localização ausente.
            return Site(new SolarSite(settings.LatitudeDeg, settings.LongitudeDeg, settings.TimeZoneHours, settings.PlaceName, SiteSource.Digitado), false);
        }

        try
        {
            SiteLocation site = document.SiteLocation;
            double lat = Geo.RadToDeg(site.Latitude), lon = Geo.RadToDeg(site.Longitude);
            string name = site.PlaceName ?? string.Empty;
            bool undefined = !double.IsFinite(lat) || !double.IsFinite(lon) || (Math.Abs(lat) < 1e-4 && Math.Abs(lon) < 1e-4)
                             || name.Contains("default", StringComparison.OrdinalIgnoreCase) || name.Contains("padrão", StringComparison.OrdinalIgnoreCase)
                             || Math.Abs(lat) > 66;
            if (!undefined)
            {
                double tz = double.IsFinite(site.TimeZone) ? site.TimeZone : SolarSite.SuggestTimeZone(lon);
                return Site(new SolarSite(lat, lon, tz, name, SiteSource.Projeto), false);
            }
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Estudo solar: localização do projeto", ex);
        }

        return Site(new SolarSite(settings.LatitudeDeg, settings.LongitudeDeg, settings.TimeZoneHours, settings.PlaceName, SiteSource.Digitado), true);
    }

    /// <summary>Grava as coordenadas na localização do projeto (Gerenciar > Localização). Precisa de transação.</summary>
    public static void SaveSite(Document document, SolarSite site)
    {
        SiteLocation location = document.SiteLocation;
        location.Latitude = Geo.DegToRad(site.LatitudeDeg);
        location.Longitude = Geo.DegToRad(site.LongitudeDeg);
        try
        {
            location.TimeZone = site.TimeZoneHours;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException)
        {
            ViridianLog.Warn("Estudo solar: fuso não aceito pelo Revit", ex);
        }

        if (!string.IsNullOrWhiteSpace(site.Name))
        {
            try
            {
                location.PlaceName = site.Name.Trim();
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                ViridianLog.Warn("Estudo solar: nome do local não aceito pelo Revit", ex);
            }
        }
    }

    /// <summary>Latitude usada (compatibilidade): a da localização do projeto ou a digitada nas configurações.</summary>
    public static double Latitude(Document document, SolarSettings settings, out string place)
    {
        ProjectSite site = ReadSite(document, settings);
        place = site.Site.Describe();
        return site.Site.LatitudeDeg;
    }

    /// <summary>
    /// Norte verdadeiro usado no cálculo, em graus a partir do topo da planta (anti-horário): o indicado na planta pelo
    /// comando Indicar Norte, gravado no projeto; sem ele, o do Revit (Gerenciar > Posição > Girar norte verdadeiro) com
    /// a correção geral das configurações.
    /// </summary>
    public static (double Degrees, bool FromPick) EffectiveNorth(Document document, SolarSettings settings)
    {
        if (ElementData.ReadProject(document).TrueNorthDeg is double picked && double.IsFinite(picked))
        {
            return (TrueNorth.Normalize(picked), true);
        }

        return (TrueNorth.Normalize(Geo.RadToDeg(-RevitNorthAngle(document)) + settings.NorthAdjustDeg), false);
    }

    /// <summary>
    /// Procura um símbolo de norte (anotação genérica ou componente de detalhe cujo nome tenha "norte", "north" ou
    /// "rosa dos ventos") na planta e nas pranchas onde ela está, e devolve a direção para onde ele aponta em relação ao
    /// topo da planta. Serve só como sugestão na janela do estudo. Nulo quando não há símbolo ou a vista já está
    /// orientada pelo norte verdadeiro do Revit.
    /// </summary>
    public static NorthHint? NorthSymbolHint(Document document, View view)
    {
        try
        {
            if (view.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH) is { StorageType: StorageType.Integer } orientation && orientation.AsInteger() == 1)
            {
                return null;
            }

            NorthHint? Find(ElementId owner, string where, double ownerRotationDeg)
            {
                using var collector = new FilteredElementCollector(document, owner);
                foreach (FamilyInstance instance in collector.OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    long category = instance.Category?.Id.Value ?? 0;
                    if (category != (long)BuiltInCategory.OST_GenericAnnotation && category != (long)BuiltInCategory.OST_DetailComponents)
                    {
                        continue;
                    }

                    string name = ((instance.Symbol?.FamilyName ?? string.Empty) + " " + (instance.Symbol?.Name ?? string.Empty)).ToLowerInvariant();
                    if (!name.Contains("norte", StringComparison.Ordinal) && !name.Contains("north", StringComparison.Ordinal) && !name.Contains("rosa dos ventos", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    XYZ up = instance.GetTransform().BasisY;
                    if (instance.Mirrored)
                    {
                        up = -up;
                    }

                    return new NorthHint(TrueNorth.Normalize(Geo.RadToDeg(Math.Atan2(-up.X, up.Y)) - ownerRotationDeg), where);
                }

                return null;
            }

            if (Find(view.Id, "na planta", 0) is NorthHint inView)
            {
                return inView;
            }

            using var viewports = new FilteredElementCollector(document);
            foreach (Viewport port in viewports.OfClass(typeof(Viewport)).Cast<Viewport>().Where(v => v.ViewId == view.Id))
            {
                double rotation = port.Rotation switch
                {
                    ViewportRotation.Clockwise => -90,
                    ViewportRotation.Counterclockwise => 90,
                    _ => 0,
                };
                if (Find(port.SheetId, "na prancha", rotation) is NorthHint onSheet)
                {
                    return onSheet;
                }
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Estudo solar: procura do símbolo de norte", ex);
        }

        return null;
    }

    /// <summary>Ângulo do norte verdadeiro do Revit (posição do projeto), em radianos.</summary>
    public static double RevitNorthAngle(Document document)
    {
        try
        {
            return document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Estudo solar: norte do projeto", ex);
            return 0;
        }
    }

    /// <summary>
    /// Converte direções do norte verdadeiro para as coordenadas internas do modelo, pelo norte efetivo do projeto
    /// (<see cref="EffectiveNorth"/>).
    /// </summary>
    public static Func<Vec3, Vec3> ToModel(Document document, SolarSettings settings)
    {
        double rotation = Geo.DegToRad(EffectiveNorth(document, settings).Degrees);
        return v => v.RotateZ(rotation);
    }

    /// <summary>
    /// Grava o norte verdadeiro indicado na planta. Com <paramref name="alsoRevit"/>, gira também o norte verdadeiro da
    /// posição do projeto no Revit (o mesmo de Gerenciar > Posição), e o projeto passa a seguir o Revit. Nulo apaga a
    /// indicação. Precisa de transação.
    /// </summary>
    public static void SaveNorth(Document document, double? degrees, bool alsoRevit)
    {
        ProjectData data = ElementData.ReadProject(document);
        if (degrees is double d && alsoRevit)
        {
            ProjectLocation location = document.ActiveProjectLocation;
            ProjectPosition position = location.GetProjectPosition(XYZ.Zero);
            location.SetProjectPosition(XYZ.Zero, new ProjectPosition(position.EastWest, position.NorthSouth, position.Elevation, -Geo.DegToRad(d)));
            data.TrueNorthDeg = null;
        }
        else
        {
            data.TrueNorthDeg = degrees;
        }

        ElementData.WriteProject(document, data);
    }

    /// <summary>Vista 3D temporária para as consultas de obstrução e terreno. Precisa de transação.</summary>
    public static View3D CreateProbeView(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        ViewFamilyType type = collector.OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                                  .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional)
                              ?? throw new ViridianException("O projeto não tem um tipo de vista 3D.");
        View3D view = View3D.CreateIsometric(document, type.Id);
        view.ViewTemplateId = ElementId.InvalidElementId;
        try
        {
            view.Name = ProbeViewName + " " + DateTime.Now.ToString("HHmmss");
        }
        catch (Exception)
        {
            // nome repetido: fica o padrão do Revit
        }

        return view;
    }

    /// <summary>Limite de triângulos lidos das edificações (modelos muito grandes ficam com o que couber).</summary>
    private const int MaxObstacleTriangles = 600_000;

    /// <summary>
    /// Geometria real (em triângulos, metros) das edificações, muros, coberturas, pisos, pilares, massas e do terreno
    /// a até <paramref name="reachM"/> metros da área, inclusive de modelos vinculados. Não altera o modelo.
    /// </summary>
    public static ObstacleMesh ReadObstacles(Document document, Bounds2 area, double reachM)
    {
        var triangles = new List<(Tri3, bool)>();
        var options = new Options { DetailLevel = ViewDetailLevel.Medium, ComputeReferences = false, IncludeNonVisibleObjects = false };
        Bounds2 near = area.Expand(reachM);

        Vec3 ToVec(Transform t, XYZ p)
        {
            XYZ q = t.OfPoint(p);
            return new Vec3(RevitUnits.ToMeters(q.X), RevitUnits.ToMeters(q.Y), RevitUnits.ToMeters(q.Z));
        }

        void AddMesh(Mesh mesh, Transform t, bool terrain)
        {
            for (int i = 0; i < mesh.NumTriangles && triangles.Count < MaxObstacleTriangles; i++)
            {
                MeshTriangle tri = mesh.get_Triangle(i);
                triangles.Add((new Tri3(ToVec(t, tri.get_Vertex(0)), ToVec(t, tri.get_Vertex(1)), ToVec(t, tri.get_Vertex(2))), terrain));
            }
        }

        void Add(GeometryElement geometry, Transform t, bool terrain)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (triangles.Count >= MaxObstacleTriangles)
                {
                    return;
                }

                switch (obj)
                {
                    case Solid solid when solid.Faces.Size > 0:
                        foreach (Face face in solid.Faces)
                        {
                            if (face.Triangulate() is Mesh faceMesh)
                            {
                                AddMesh(faceMesh, t, terrain);
                            }
                        }

                        break;
                    case Mesh mesh:
                        AddMesh(mesh, t, terrain);
                        break;
                    case GeometryInstance instance when instance.GetInstanceGeometry() is GeometryElement inner:
                        Add(inner, t, terrain);
                        break;
                }
            }
        }

        void FromDocument(Document doc, Transform toHost)
        {
            // Área de busca nas coordenadas do documento (vínculos podem estar deslocados e girados).
            Transform toDoc = toHost.Inverse;
            var corners = new[]
            {
                toDoc.OfPoint(RevitUnits.ToXyz(new Point2(near.MinX, near.MinY))), toDoc.OfPoint(RevitUnits.ToXyz(new Point2(near.MaxX, near.MinY))),
                toDoc.OfPoint(RevitUnits.ToXyz(new Point2(near.MaxX, near.MaxY))), toDoc.OfPoint(RevitUnits.ToXyz(new Point2(near.MinX, near.MaxY))),
            };
            var outline = new Outline(
                new XYZ(corners.Min(c => c.X), corners.Min(c => c.Y), -30000),
                new XYZ(corners.Max(c => c.X), corners.Max(c => c.Y), 30000));
            using var collector = new FilteredElementCollector(doc);
            foreach (Element element in collector.WherePasses(new ElementMulticategoryFilter(Obstacles.ToList()))
                         .WherePasses(new BoundingBoxIntersectsFilter(outline)).WhereElementIsNotElementType())
            {
                if (triangles.Count >= MaxObstacleTriangles)
                {
                    break;
                }

                long category = element.Category?.Id.Value ?? 0;
                bool terrain = category == (long)BuiltInCategory.OST_Toposolid || category == (long)BuiltInCategory.OST_Topography;
                try
                {
                    if (element.get_Geometry(options) is GeometryElement geometry)
                    {
                        Add(geometry, toHost, terrain);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    ViridianLog.Warn($"Estudo solar: geometria de {element.Id.Value} ignorada", ex);
                }
            }
        }

        FromDocument(document, Transform.Identity);
        using (var links = new FilteredElementCollector(document))
        {
            foreach (RevitLinkInstance link in links.OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                if (link.GetLinkDocument() is Document linked)
                {
                    FromDocument(linked, link.GetTotalTransform());
                }
            }
        }

        if (triangles.Count >= MaxObstacleTriangles)
        {
            ViridianLog.Warn($"Estudo solar: geometria das edificações limitada a {MaxObstacleTriangles} triângulos.");
        }

        return new ObstacleMesh(triangles);
    }

    /// <summary>Calcula as horas de sol da malha e das plantas da região. Não altera o modelo.</summary>
    public static SolarReport Analyze(
        Document document,
        View3D probe,
        Polygon2 region,
        double levelZ,
        IReadOnlyList<(PlantRecord Record, Species Species)> plantsInRegion,
        IReadOnlyList<(PlantRecord Record, Species Species)> shadingPlants,
        IReadOnlyList<SolarDate> dates,
        SolarSettings settings,
        GrowthStage stage,
        SolarSite site,
        IReadOnlyList<SolarDate>? shadeDates = null)
    {
        DateTime started = DateTime.Now;
        double latitude = site.LatitudeDeg;
        Func<Vec3, Vec3> toModel = ToModel(document, settings);
        var terrain = new TerrainProbe(document, probe, true, 30);

        double Ground(Point2 p, double z) => terrain.TryGround(p, z, out double g) ? g : z;
        double PlantZ(PlantRecord r) =>
            document.GetElement(new ElementId(r.ElementId)) is FamilyInstance { Location: LocationPoint lp } ? RevitUnits.ToMeters(lp.Point.Z) : levelZ;

        // Copas que fazem sombra (inclusive de plantas fora da região, a até 40 m).
        Bounds2 near = region.Bounds.Expand(40);
        var crowns = new List<CrownObstacle>();
        foreach ((PlantRecord r, Species s) in shadingPlants.Where(p => near.Contains(p.Record.Location)))
        {
            double z = Ground(r.Location, PlantZ(r));
            CrownObstacle? c = CrownObstacle.For(r.UniqueId, s, new Vec3(r.Location.X, r.Location.Y, z), stage, site.South);
            if (c is not null)
            {
                crowns.Add(c);
            }
        }

        (List<Point2> grid, double cell) = SolarStudy.Grid(region, settings.GridM, settings.MaxPoints);
        var report = new SolarReport { Site = site, Dates = dates, TrueNorthRad = toModel(new Vec3(0, 1, 0)) is Vec3 n ? Math.Atan2(-n.X, n.Y) : 0, Cell = cell, UsedBuildings = settings.IncludeBuildings };
        var points = new List<SolarPoint>();
        foreach (Point2 p in grid)
        {
            var sp = new SolarPoint { Position = new Vec3(p.X, p.Y, Ground(p, levelZ) + 0.3), Hours = new double[dates.Count] };
            report.Grid.Add((p, sp));
            points.Add(sp);
        }

        foreach ((PlantRecord r, Species s) in plantsInRegion)
        {
            (_, double height) = GrowthModel.Size(s, stage);
            double z = Ground(r.Location, PlantZ(r)) + Math.Max(0.3, height * 0.9);
            var sp = new SolarPoint { Position = new Vec3(r.Location.X, r.Location.Y, z), IgnoreId = r.UniqueId, Hours = new double[dates.Count] };
            report.Plants.Add(new SolarPlant(r, s, sp));
            points.Add(sp);
        }

        // Geometria real das edificações e do terreno: a mesma para as horas de sol e para o desenho das sombras.
        ObstacleMesh? mesh = null;
        Func<Vec3, Vec3, bool>? blocked = null;
        if (settings.IncludeBuildings)
        {
            mesh = ReadObstacles(document, region.Bounds, 80);
            report.ObstacleTriangles = mesh.Count;
        }

        if (mesh is { IsEmpty: false })
        {
            blocked = mesh.Blocks;
        }
        else if (settings.IncludeBuildings)
        {
            var filter = new ElementMulticategoryFilter(Obstacles.ToList());
            var intersector = new ReferenceIntersector(filter, FindReferenceTarget.Element, probe) { FindReferencesInRevitLinks = true };
            blocked = (origin, dir) =>
            {
                try
                {
                    XYZ o = RevitUnits.ToXyz(new Point2(origin.X, origin.Y), RevitUnits.ToFeet(origin.Z + 0.02));
                    return intersector.FindNearest(o, new XYZ(dir.X, dir.Y, dir.Z)) is not null;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    return false;
                }
            };
        }

        SolarStudy.Compute(points, crowns, latitude, dates, settings, toModel, blocked);
        report.Crowns.AddRange(crowns);

        // Sombras de hora em hora (hora legal) nas datas pedidas.
        if (shadeDates is { Count: > 0 })
        {
            List<Vec3> gridPositions = report.Grid.Select(g => g.Point.Position).ToList();
            // Plano do jardim: a cota mediana do chão da região (os pontos ficam 0,30 m acima dele).
            List<double> grounds = gridPositions.Select(p => p.Z - 0.3).OrderBy(z => z).ToList();
            double plane = grounds.Count == 0 ? levelZ : grounds[grounds.Count / 2];
            var shadeGeometry = new ShadeGeometry(region, plane, mesh, ShadowField.ResolutionFor(region.Bounds));
            report.PlaneZ = plane;
            List<(Vec3, string)> plantPositions = report.Plants.Select(p => (p.Point.Position, p.Point.IgnoreId)).ToList();
            foreach (SolarDate date in shadeDates)
            {
                report.Shades.Add((date, HourlyShade.Compute(gridPositions, plantPositions, crowns, site, date, settings.FromHour, settings.ToHour, toModel, blocked,
                    default, shadeGeometry)));
            }
        }
        report.Duration = DateTime.Now - started;
        return report;
    }

    /// <summary>Contorno fechado em planta como laço do Revit, sem lados menores que a tolerância.</summary>
    private static CurveLoop? RingLoop(IReadOnlyList<Point2> ring, double z, double shortCurve)
    {
        var points = new List<XYZ>();
        foreach (Point2 p in ring)
        {
            XYZ xyz = RevitUnits.ToXyz(p, z);
            if (points.Count == 0 || points[^1].DistanceTo(xyz) >= shortCurve * 1.5)
            {
                points.Add(xyz);
            }
        }

        while (points.Count > 3 && points[^1].DistanceTo(points[0]) < shortCurve * 1.5)
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count < 3)
        {
            return null;
        }

        try
        {
            var curves = new List<Curve>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                curves.Add(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            return CurveLoop.Create(curves);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Estudo solar: contorno de sombra ignorado", ex);
            return null;
        }
    }

    /// <summary>Frase do norte usado ("Norte verdadeiro 90° à direita do topo da planta").</summary>
    public static string NorthSentence(SolarReport report)
    {
        string text = TrueNorth.Describe(Geo.RadToDeg(report.TrueNorthRad));
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>Fresta entre faixas vizinhas da malha (metros): 1% da célula, no mínimo 3 mm.</summary>
    private static double StripGap(double cell) => Math.Max(cell * 0.01, 0.003);

    private static List<CurveLoop> StripLoops(IEnumerable<Bounds2> boxes, double z, double shortCurve)
    {
        var loops = new List<CurveLoop>();
        foreach (Bounds2 box in boxes)
        {
            CurveLoop? loop = SafeRegions.Rectangle(RevitUnits.ToXyz(new Point2(box.MinX, box.MinY), z), RevitUnits.ToXyz(new Point2(box.MaxX, box.MaxY), z), shortCurve);
            if (loop is not null)
            {
                loops.Add(loop);
            }
        }

        return loops;
    }

    /// <summary>Quadro de sombras de hora em hora de uma data, em vista de desenho (refeito a cada estudo). Precisa de transação.</summary>
    public static ViewDrafting ShadeSheetView(Document document, ViewPlan source, Polygon2 region, SolarReport report, SolarDate date,
        IReadOnlyList<ShadeFrame> frames, IReadOnlyList<ShadeRow> rows, string stageText, double textMm)
    {
        string title = $"Sombras por horário — {date.Name}";
        Drawing drawing = ShadeSheet.Build(title, report.Site, date, frames, report.Grid.Select(g => g.Plan).ToList(), report.Cell, region,
            report.Crowns, rows, report.TrueNorthRad, report.UsedBuildings, stageText, NorthSentence(report),
            report.ObstacleTriangles > 0 ? report.ObstacleTriangles.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " triângulos lidos do modelo"
                : "nenhuma edificação encontrada perto da região");
        return new DesignService(textMm).DrawingView(document, PavingService.SafeName($"Viridian - Sombras por horário - {date.Name} - {source.Name}"), drawing, 1);
    }

    /// <summary>
    /// Cópia da planta com as sombras de um horário: malha à sombra e à meia-luz (semitransparentes), contorno da sombra
    /// de cada copa e a legenda com a data, a hora e a posição do sol. A planta original não muda. Precisa de transação.
    /// </summary>
    public static ViewPlan ShadowPlan(Document document, ViewPlan source, Polygon2 region, SolarReport report, ShadeFrame frame, double textMm)
    {
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            throw new ViridianException("Esta vista não pode ser duplicada para as sombras.");
        }

        var view = (ViewPlan)document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
        string baseName = PavingService.SafeName($"Viridian - Sombras {frame.Date.Name} {frame.ClockHour:00}h - {source.Name}");
        string name = baseName;
        using (var collector = new FilteredElementCollector(document))
        {
            var names = new HashSet<string>(collector.OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            for (int i = 2; names.Contains(name); i++)
            {
                name = $"{baseName} ({i})";
            }
        }

        view.Name = name;
        double z = source.GenLevel?.ProjectElevation ?? 0;
        List<Point2> grid = report.Grid.Select(g => g.Plan).ToList();
        double shortCurve = document.Application.ShortCurveTolerance;
        if (frame.Rings.Count > 0)
        {
            // Sombras realistas: cada classe é uma região com os contornos dos seus dois níveis (o de dentro vira furo),
            // então a meia-luz não fica por baixo da sombra e as edificações não são pintadas.
            foreach ((ShadeClass cls, double outer, double inner, Rgb color, int transparency) in new[]
                     {
                         (ShadeClass.MeiaLuz, LightField.Levels[2], LightField.Levels[1], HourlyShade.PartialColor, 55),
                         (ShadeClass.Sombra, LightField.Levels[1], LightField.Levels[0], HourlyShade.ShadeColor, 35),
                     })
            {
                var loops = new List<CurveLoop>();
                foreach (ShadeRing ring in frame.Rings.Where(r => Math.Abs(r.Level - outer) < 1e-9 || Math.Abs(r.Level - inner) < 1e-9))
                {
                    if (RingLoop(ring.Raw, z, shortCurve) is CurveLoop loop)
                    {
                        loops.Add(loop);
                    }
                }

                if (loops.Count == 0 || HatchService.EnsureSolidRegionType(document, color) is not FilledRegionType type)
                {
                    continue;
                }

                foreach (FilledRegion shade in SafeRegions.Create(document, type.Id, view.Id, loops, "Sombras na planta (" + cls + ")"))
                {
                    view.SetElementOverrides(shade.Id, new OverrideGraphicSettings().SetSurfaceTransparency(transparency));
                }
            }
        }
        else
        {
            // Sem o campo fino: faixas da malha (não podem se tocar dentro de uma região preenchida).
            List<(int Class, Bounds2 Box)> strips = HourlyShade.RegionStrips(
                HourlyShade.Runs(grid, report.Cell, i => HourlyShade.ShadeClass(frame.Grid[i])), StripGap(report.Cell));
            foreach (IGrouping<int, (int Class, Bounds2 Box)> group in strips.GroupBy(r => r.Class))
            {
                List<CurveLoop> loops = StripLoops(group.Select(g => g.Box), z, shortCurve);
                if (loops.Count == 0 || HatchService.EnsureSolidRegionType(document, group.Key == 0 ? HourlyShade.ShadeColor : HourlyShade.PartialColor) is not FilledRegionType type)
                {
                    continue;
                }

                foreach (FilledRegion shade in SafeRegions.Create(document, type.Id, view.Id, loops, "Sombras na planta"))
                {
                    view.SetElementOverrides(shade.Id, new OverrideGraphicSettings().SetSurfaceTransparency(group.Key == 0 ? 35 : 55));
                }
            }
        }

        // Contorno da sombra de cada copa (tracejado), direção do sol e legenda.
        double k = view.Scale / 1000.0;
        var drawing = new Drawing();
        if (frame.Rings.Count == 0)
        {
            foreach ((string _, IReadOnlyList<Point2> outline, double _) in frame.Shadows)
            {
                drawing.Polyline(outline, true); // contínuo: tracejado viraria milhares de linhas na planta
            }
        }

        Bounds2 bounds = region.Bounds;
        var legend = new Drawing();
        double y = 0;
        legend.Text(new Point2(0, y), $"SOMBRAS — {frame.Date.Name.ToUpperInvariant()}, DIA {frame.Date.Day}, {frame.HourText}", -1, 1.2);
        y -= 7 * k;
        legend.Text(new Point2(0, y), HourlyShade.SunText(frame.Sun) + $" · {frame.ShadedFraction:0%} da região à sombra", -1, 0.85);
        y -= 5 * k;
        foreach (string part in TextLayout.Wrap(report.Site.Describe() + ". Hora legal, sem horário de verão.", 60))
        {
            legend.Text(new Point2(0, y), part, -1, 0.8);
            y -= 5 * k;
        }

        foreach ((Rgb color, string text) in new[] { (HourlyShade.ShadeColor, "sombra"), (HourlyShade.PartialColor, "meia-luz (copa rala ou borda)") })
        {
            var box = new List<Point2> { new(0, y - 4 * k), new(8 * k, y - 4 * k), new(8 * k, y + 1 * k), new(0, y + 1 * k) };
            legend.Fills.Add(new DrawFill(box, color));
            legend.Polyline(box, true);
            legend.Text(new Point2(11 * k, y - 1.5 * k), text, -1, 0.85);
            y -= 7 * k;
        }

        legend.Text(new Point2(0, y - 2 * k), frame.Rings.Count > 0
            ? "Sombras pela geometria do modelo e pela folhagem de cada copa."
            : "Contorno fino: sombra de cada copa no chão.", -1, 0.8);
        legend.Text(new Point2(0, y - 7 * k), TrueNorth.Describe(Geo.RadToDeg(report.TrueNorthRad)) + ".", -1, 0.8);

        // Seta do sol: da borda da região para o centro, do lado de onde vem a luz.
        var dir = new Point2(frame.ToSun.X, frame.ToSun.Y);
        if (dir.Length > 1e-6)
        {
            dir = dir.Normalized();
            Point2 center = bounds.Center;
            double reach = Math.Max(bounds.Width, bounds.Height) * 0.5 + 12 * k;
            Point2 from = center + dir * reach, to = center + dir * (reach - 10 * k);
            List<Point2> sun = Drawing.Ellipse(from, 2.2 * k, 2.2 * k, 16);
            drawing.Fills.Add(new DrawFill(sun, HourlyShade.SunColor));
            drawing.Polyline(sun, true);
            drawing.Line(from, to, heavy: true);
            Point2 back = (from - to).Normalized() * (1.6 * k), side = back.Perp() * 0.7;
            drawing.Line(to, to + back + side, heavy: true);
            drawing.Line(to, to + back - side, heavy: true);
        }

        var renderer = new DrawingRenderer(document, view, textMm) { ElevationFeet = z };
        renderer.Render(drawing, Point2.Zero);
        renderer.Render(legend, new Point2(bounds.MaxX + 16 * k, bounds.MaxY));
        return view;
    }

    /// <summary>
    /// Exporta as vistas como imagens PNG para a pasta indicada (criada se preciso). Devolve a pasta; falhas ficam no
    /// registro e não interrompem o estudo.
    /// </summary>
    public static int ExportImages(Document document, IReadOnlyList<View> views, string folder)
    {
        Directory.CreateDirectory(folder);
        int exported = 0;
        foreach (View view in views)
        {
            try
            {
                using var options = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(folder, PavingService.SafeName(view.Name)),
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_300,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 6000, // nitidez de impressão em A1
                    FitDirection = FitDirectionType.Horizontal,
                };
                options.SetViewsAndSheets(new List<ElementId> { view.Id });
                document.ExportImage(options);
                exported++;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Estudo solar: imagem não exportada: " + view.Name, ex);
            }
        }

        return exported;
    }

    /// <summary>Calendário de luz da região numa vista de desenho (refeita a cada estudo da mesma planta). Precisa de transação.</summary>
    public static ViewDrafting CalendarView(Document document, ViewPlan source, SolarReport report, string period, IReadOnlyList<LightCalendarRow> rows, SolarSettings settings, double textMm)
    {
        string title = "Calendário de luz — " + source.Name;
        Drawing drawing = LightCalendar.Build(title, period, report.Site, report.Dates, rows, settings, report.UsedBuildings);
        return new DesignService(textMm).DrawingView(document, PavingService.SafeName("Viridian - Calendário de luz - " + source.Name), drawing, 1);
    }

    /// <summary>
    /// Mapa de horas de sol numa cópia da planta (a planta original não muda): quadros coloridos por faixa de horas,
    /// semitransparentes, e a legenda ao lado. Precisa de transação.
    /// </summary>
    public static ViewPlan HeatMap(Document document, ViewPlan source, Polygon2 region, SolarReport report, string label, Func<SolarPoint, double> hours, SolarSettings settings, double textMm)
    {
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            throw new ViridianException("Esta vista não pode ser duplicada para o mapa solar.");
        }

        var view = (ViewPlan)document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
        string baseName = $"Viridian - Estudo solar - {label} - {source.Name}";
        string name = baseName;
        using (var collector = new FilteredElementCollector(document))
        {
            var names = new HashSet<string>(collector.OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            for (int i = 2; names.Contains(name); i++)
            {
                name = $"{baseName} ({i})";
            }
        }

        view.Name = name;
        double z = source.GenLevel?.ProjectElevation ?? 0; // pés: plano da planta
        IReadOnlyList<(double From, double To, string Color, string Label)> bins = SolarClasses.Bins(settings);
        // Células da mesma faixa unidas em faixas horizontais (menos contornos) e regiões sem linha de contorno: o mapa
        // fica contínuo, sem a grade aparecendo.
        ElementId? invisible = HatchService.InvisibleLineStyle(document);
        List<Point2> points = report.Grid.Select(g => g.Plan).ToList();
        // Faixas de linhas vizinhas encostam umas nas outras, e o Revit recusa uma região com contornos que se tocam:
        // as faixas empilhadas são unidas e todas recuam uma fresta mínima (invisível na impressão).
        List<(int Class, Bounds2 Box)> runs = HourlyShade.RegionStrips(
            HourlyShade.Runs(points, report.Cell, i => SolarClasses.BinOf(hours(report.Grid[i].Point), settings)), StripGap(report.Cell));
        double shortCurve = document.Application.ShortCurveTolerance;
        for (int bin = 0; bin < bins.Count; bin++)
        {
            List<CurveLoop> loops = StripLoops(runs.Where(r => r.Class == bin).Select(r => r.Box), z, shortCurve);
            if (loops.Count == 0)
            {
                continue;
            }

            FilledRegionType? type = HatchService.EnsureSolidRegionType(document, Rgb.Parse(bins[bin].Color, Rgb.White));
            if (type is null)
            {
                continue;
            }

            foreach (FilledRegion region2 in SafeRegions.Create(document, type.Id, view.Id, loops, "Mapa solar"))
            {
                if (invisible is not null)
                {
                    try
                    {
                        region2.SetLineStyleId(invisible);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        ViridianLog.Warn("Estudo solar: contorno invisível no mapa", ex);
                    }
                }

                view.SetElementOverrides(region2.Id, new OverrideGraphicSettings().SetSurfaceTransparency(35));
            }
        }

        // Legenda à direita da região, dimensionada pela escala da vista.
        double k = view.Scale / 1000.0; // metros reais por milímetro de papel
        var legend = new Drawing();
        double y = 0;
        legend.Text(new Point2(0, y), "ESTUDO SOLAR — HORAS DE SOL DIRETO", -1, 1.2);
        y -= 8 * k;
        legend.Text(new Point2(0, y), label, -1, 0.9);
        y -= 6 * k;
        foreach (string part in TextLayout.Wrap(report.Site.Describe(), 60))
        {
            legend.Text(new Point2(0, y), part, -1, 0.85);
            y -= 5 * k;
        }

        y -= 3 * k;
        foreach ((double from, double to, string color, string text) in bins)
        {
            var box = new List<Point2> { new(0, y - 4 * k), new(8 * k, y - 4 * k), new(8 * k, y + 1 * k), new(0, y + 1 * k) };
            legend.Fills.Add(new DrawFill(box, Rgb.Parse(color, Rgb.White)));
            legend.Polyline(box, true);
            legend.Text(new Point2(11 * k, y - 1.5 * k), text, -1, 0.9);
            y -= 7 * k;
        }

        legend.Text(new Point2(0, y - 2 * k), "Sombra das copas adultas" + (report.UsedBuildings ? ", edificações e terreno." : "."), -1, 0.8);
        legend.Text(new Point2(0, y - 7 * k), "Horas de sol direto num dia típico; sem horário de verão.", -1, 0.8);
        Bounds2 bounds = region.Bounds;
        new DrawingRenderer(document, view, textMm) { ElevationFeet = z }.Render(legend, new Point2(bounds.MaxX + 10 * k, bounds.MaxY));
        return view;
    }
}
