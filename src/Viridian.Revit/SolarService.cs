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
    public double LatitudeDeg { get; init; }

    public string Place { get; init; } = string.Empty;

    public IReadOnlyList<SolarDate> Dates { get; init; } = Array.Empty<SolarDate>();

    public List<(Point2 Plan, SolarPoint Point)> Grid { get; } = new();

    public double Cell { get; init; }

    public List<SolarPlant> Plants { get; } = new();

    public bool UsedBuildings { get; init; }

    public TimeSpan Duration { get; set; }

    /// <summary>Horas da malha usadas no mapa (média das datas analisadas).</summary>
    public double MapHours(SolarPoint p) => p.Average;
}

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

    /// <summary>Latitude usada: a da localização do projeto ou a digitada nas configurações.</summary>
    public static double Latitude(Document document, SolarSettings settings, out string place)
    {
        place = "latitude digitada nas configurações";
        if (settings.UseProjectLocation)
        {
            try
            {
                SiteLocation site = document.SiteLocation;
                double lat = Geo.RadToDeg(site.Latitude);
                if (double.IsFinite(lat) && Math.Abs(lat) > 1e-6)
                {
                    place = string.IsNullOrWhiteSpace(site.PlaceName) ? "localização do projeto" : site.PlaceName;
                    return Math.Clamp(lat, -66, 66);
                }

                place = "latitude digitada (o projeto não tem localização definida)";
            }
            catch (Exception ex)
            {
                ViridianLog.Warn("Estudo solar: localização do projeto", ex);
            }
        }

        return settings.LatitudeDeg;
    }

    /// <summary>
    /// Converte direções do norte verdadeiro para as coordenadas internas do modelo, pelo ângulo do norte do projeto
    /// (Gerenciar > Localização > Girar norte verdadeiro) e pela correção das configurações.
    /// </summary>
    public static Func<Vec3, Vec3> ToModel(Document document, SolarSettings settings)
    {
        double angle = 0;
        try
        {
            angle = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Angle;
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Estudo solar: norte do projeto", ex);
        }

        double rotation = -angle + Geo.DegToRad(settings.NorthAdjustDeg);
        return v => v.RotateZ(rotation);
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
        GrowthStage stage)
    {
        DateTime started = DateTime.Now;
        double latitude = Latitude(document, settings, out string place);
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
            CrownObstacle? c = CrownObstacle.For(r.UniqueId, s, new Vec3(r.Location.X, r.Location.Y, z), stage);
            if (c is not null)
            {
                crowns.Add(c);
            }
        }

        (List<Point2> grid, double cell) = SolarStudy.Grid(region, settings.GridM, settings.MaxPoints);
        var report = new SolarReport { LatitudeDeg = latitude, Place = place, Dates = dates, Cell = cell, UsedBuildings = settings.IncludeBuildings };
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

        Func<Vec3, Vec3, bool>? blocked = null;
        if (settings.IncludeBuildings)
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
        report.Duration = DateTime.Now - started;
        return report;
    }

    /// <summary>
    /// Mapa de horas de sol numa cópia da planta (a planta original não muda): quadros coloridos por faixa de horas,
    /// semitransparentes, e a legenda ao lado. Precisa de transação.
    /// </summary>
    public static ViewPlan HeatMap(Document document, ViewPlan source, Polygon2 region, SolarReport report, string label, double textMm)
    {
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
        {
            throw new ViridianException("Esta vista não pode ser duplicada para o mapa solar.");
        }

        var view = (ViewPlan)document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
        string baseName = $"Viridian - Estudo solar - {source.Name}";
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
        double half = report.Cell * 0.48;
        double z = source.GenLevel?.ProjectElevation ?? 0; // pés: plano da planta
        for (int bin = 0; bin < SolarClasses.Bins.Count; bin++)
        {
            var loops = new List<CurveLoop>();
            foreach ((Point2 p, SolarPoint sp) in report.Grid.Where(g => SolarClasses.BinOf(report.MapHours(g.Point)) == bin))
            {
                XYZ a = RevitUnits.ToXyz(new Point2(p.X - half, p.Y - half), z), b = RevitUnits.ToXyz(new Point2(p.X + half, p.Y - half), z);
                XYZ c = RevitUnits.ToXyz(new Point2(p.X + half, p.Y + half), z), d = RevitUnits.ToXyz(new Point2(p.X - half, p.Y + half), z);
                loops.Add(CurveLoop.Create(new List<Curve> { Line.CreateBound(a, b), Line.CreateBound(b, c), Line.CreateBound(c, d), Line.CreateBound(d, a) }));
            }

            if (loops.Count == 0)
            {
                continue;
            }

            FilledRegionType? type = HatchService.EnsureSolidRegionType(document, Rgb.Parse(SolarClasses.Bins[bin].Color, Rgb.White));
            if (type is null)
            {
                continue;
            }

            FilledRegion region2 = FilledRegion.Create(document, type.Id, view.Id, loops);
            view.SetElementOverrides(region2.Id, new OverrideGraphicSettings().SetSurfaceTransparency(40));
        }

        // Legenda à direita da região, dimensionada pela escala da vista.
        double k = view.Scale / 1000.0; // metros reais por milímetro de papel
        var legend = new Drawing();
        double y = 0;
        legend.Text(new Point2(0, y), "ESTUDO SOLAR — HORAS DE SOL DIRETO", -1, 1.2);
        y -= 8 * k;
        legend.Text(new Point2(0, y), label, -1, 0.9);
        y -= 6 * k;
        legend.Text(new Point2(0, y), $"Latitude {report.LatitudeDeg.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}° ({report.Place})", -1, 0.85);
        y -= 8 * k;
        foreach ((double from, double to, string color, string text) in SolarClasses.Bins)
        {
            var box = new List<Point2> { new(0, y - 4 * k), new(8 * k, y - 4 * k), new(8 * k, y + 1 * k), new(0, y + 1 * k) };
            legend.Fills.Add(new DrawFill(box, Rgb.Parse(color, Rgb.White)));
            legend.Polyline(box, true);
            legend.Text(new Point2(11 * k, y - 1.5 * k), text, -1, 0.9);
            y -= 7 * k;
        }

        legend.Text(new Point2(0, y - 2 * k), "Sombra das copas adultas" + (report.UsedBuildings ? ", edificações e terreno." : "."), -1, 0.8);
        Bounds2 bounds = region.Bounds;
        new DrawingRenderer(document, view, textMm) { ElevationFeet = z }.Render(legend, new Point2(bounds.MaxX + 10 * k, bounds.MaxY));
        return view;
    }
}
