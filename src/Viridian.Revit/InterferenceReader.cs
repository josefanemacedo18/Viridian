using Autodesk.Revit.DB;
using Viridian.Core.Geometry;
using Viridian.Core.Interference;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Elementos do projeto e dos vínculos que interessam à verificação de interferências.</summary>
public sealed class InterferenceSources
{
    public List<NetworkSegment> Segments { get; } = new();

    public List<NetworkPoint> Points { get; } = new();

    public List<PavementArea> Pavements { get; } = new();

    /// <summary>Modelos lidos (o próprio projeto e os vínculos carregados).</summary>
    public List<string> Models { get; } = new();

    /// <summary>Vínculos não carregados, que ficaram fora da verificação.</summary>
    public List<string> Unloaded { get; } = new();
}

/// <summary>Lê redes, postes, paredes e pisos do projeto e dos modelos vinculados, em coordenadas do projeto.</summary>
public static class InterferenceReader
{
    private static readonly (BuiltInCategory Category, NetworkKind Kind, string Label)[] LinearCategories =
    {
        (BuiltInCategory.OST_PipeCurves, NetworkKind.Tubulacao, "Tubulação"),
        (BuiltInCategory.OST_FlexPipeCurves, NetworkKind.Tubulacao, "Tubulação flexível"),
        (BuiltInCategory.OST_Conduit, NetworkKind.Eletroduto, "Eletroduto"),
        (BuiltInCategory.OST_DuctCurves, NetworkKind.Duto, "Duto"),
        (BuiltInCategory.OST_FlexDuctCurves, NetworkKind.Duto, "Duto flexível"),
        (BuiltInCategory.OST_CableTray, NetworkKind.Eletrocalha, "Eletrocalha"),
    };

    private static readonly (BuiltInCategory Category, NetworkKind Kind, string Label)[] PointCategories =
    {
        (BuiltInCategory.OST_LightingFixtures, NetworkKind.Iluminacao, "Luminária"),
        (BuiltInCategory.OST_LightingDevices, NetworkKind.Iluminacao, "Dispositivo de iluminação"),
        (BuiltInCategory.OST_ElectricalFixtures, NetworkKind.Eletrico, "Ponto elétrico"),
        (BuiltInCategory.OST_ElectricalEquipment, NetworkKind.Eletrico, "Equipamento elétrico"),
    };

    public static InterferenceSources Read(Document document, InterferenceSettings settings)
    {
        var sources = new InterferenceSources();
        ReadDocument(document, Transform.Identity, string.Empty, sources, settings);
        sources.Models.Add(document.Title);

        if (settings.IncludeLinks)
        {
            using var collector = new FilteredElementCollector(document);
            foreach (RevitLinkInstance link in collector.OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document? linked = link.GetLinkDocument();
                if (linked is null)
                {
                    sources.Unloaded.Add(link.Name);
                    continue;
                }

                ReadDocument(linked, link.GetTotalTransform(), " — vínculo " + linked.Title, sources, settings);
                sources.Models.Add(linked.Title);
            }
        }

        return sources;
    }

    /// <summary>Exemplares a verificar: cadastrados, não removidos, com a categoria da espécie.</summary>
    public static List<InterferencePlant> Plants(Document document, IReadOnlyList<PlantRecord> plants, Func<string, Species?> lookup)
    {
        var result = new List<InterferencePlant>();
        foreach (PlantRecord plant in plants)
        {
            if (!plant.IsRegistered || plant.Status == PlantStatus.Removido || lookup(plant.SpeciesId) is not Species species)
            {
                continue;
            }

            double elevation = document.GetElement(new ElementId(plant.ElementId))?.Location is LocationPoint lp ? RevitUnits.ToMeters(lp.Point.Z) : 0;
            result.Add(new InterferencePlant(
                plant.ElementId,
                plant.ProjectCode.Length > 0 ? plant.ProjectCode : plant.SpeciesCode,
                plant.PopularName,
                species.Category,
                plant.Location,
                elevation,
                plant.CrownDiameterM > 0 ? plant.CrownDiameterM : species.CrownDiameterM));
        }

        return result;
    }

    private static void ReadDocument(Document document, Transform transform, string suffix, InterferenceSources sources, InterferenceSettings settings)
    {
        bool Wanted(NetworkKind kind) => settings.RuleOf(kind) is { Enabled: true } rule && (rule.TreeM > 0 || rule.PalmM > 0 || rule.ShrubM > 0);

        foreach ((BuiltInCategory category, NetworkKind kind, string label) in LinearCategories)
        {
            if (!Wanted(kind))
            {
                continue;
            }

            using var collector = new FilteredElementCollector(document);
            foreach (Element element in collector.OfCategory(category).WhereElementIsNotElementType())
            {
                if (element.Location is not LocationCurve { Curve: Curve curve })
                {
                    continue;
                }

                double half = HalfWidth(element);
                string description = $"{label} {SizeText(half)}{suffix} (id {element.Id.Value})";
                AddCurve(sources, kind, curve, transform, half, description);
            }
        }

        if (Wanted(NetworkKind.Edificacao))
        {
            using var collector = new FilteredElementCollector(document);
            foreach (Wall wall in collector.OfClass(typeof(Wall)).Cast<Wall>())
            {
                if (wall.Location is LocationCurve { Curve: Curve curve })
                {
                    double half = RevitUnits.ToMeters(Safe(() => wall.Width)) / 2;
                    AddCurve(sources, NetworkKind.Edificacao, curve, transform, half, $"parede \"{wall.Name}\"{suffix} (id {wall.Id.Value})");
                }
            }
        }

        foreach ((BuiltInCategory category, NetworkKind kind, string label) in PointCategories)
        {
            if (!Wanted(kind))
            {
                continue;
            }

            using var collector = new FilteredElementCollector(document);
            foreach (Element element in collector.OfCategory(category).WhereElementIsNotElementType())
            {
                if (element.Location is LocationPoint lp)
                {
                    XYZ p = transform.OfPoint(lp.Point);
                    sources.Points.Add(new NetworkPoint(kind, RevitUnits.ToPoint2(p), 0.1, $"{label} \"{element.Name}\"{suffix} (id {element.Id.Value})"));
                }
            }
        }

        if (settings.CheckPavements)
        {
            using var collector = new FilteredElementCollector(document);
            foreach (Floor floor in collector.OfClass(typeof(Floor)).Cast<Floor>())
            {
                try
                {
                    Polygon2 local = BoundaryReader.Read(floor).Polygon;
                    if (local.IsEmpty)
                    {
                        continue;
                    }

                    var rings = local.Rings.Select(r => r.Select(pt => RevitUnits.ToPoint2(transform.OfPoint(RevitUnits.ToXyz(pt))))).ToList();
                    BoundingBoxXYZ? box = floor.get_BoundingBox(null);
                    double top = box is null ? 0 : RevitUnits.ToMeters(transform.OfPoint(box.Max).Z);
                    sources.Pavements.Add(new PavementArea(new Polygon2(rings), top, $"o piso \"{floor.Name}\"{suffix} (id {floor.Id.Value})"));
                }
                catch (Exception ex) when (ex is ViridianException or Autodesk.Revit.Exceptions.ApplicationException)
                {
                    ViridianLog.Warn("Piso ignorado na verificação de interferências: " + floor.Id.Value, ex);
                }
            }
        }
    }

    private static void AddCurve(InterferenceSources sources, NetworkKind kind, Curve curve, Transform transform, double halfWidthM, string description)
    {
        IList<XYZ> points;
        try
        {
            points = curve.Tessellate();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return;
        }

        for (int i = 1; i < points.Count; i++)
        {
            Point2 a = RevitUnits.ToPoint2(transform.OfPoint(points[i - 1]));
            Point2 b = RevitUnits.ToPoint2(transform.OfPoint(points[i]));
            sources.Segments.Add(new NetworkSegment(kind, a, b, halfWidthM, description));
        }
    }

    /// <summary>Metade da largura (ou do diâmetro externo) do elemento linear, em metros.</summary>
    private static double HalfWidth(Element element)
    {
        foreach (BuiltInParameter id in new[] { BuiltInParameter.RBS_PIPE_OUTER_DIAMETER, BuiltInParameter.RBS_CONDUIT_OUTER_DIAM_PARAM })
        {
            if (element.get_Parameter(id) is { StorageType: StorageType.Double } p && p.AsDouble() > 0)
            {
                return RevitUnits.ToMeters(p.AsDouble()) / 2;
            }
        }

        if (element is MEPCurve mep)
        {
            double diameter = Safe(() => mep.Diameter);
            if (diameter > 0)
            {
                return RevitUnits.ToMeters(diameter) / 2;
            }

            double width = Safe(() => mep.Width);
            if (width > 0)
            {
                return RevitUnits.ToMeters(width) / 2;
            }
        }

        return 0;
    }

    private static string SizeText(double halfWidthM) =>
        halfWidthM > 0 ? $"Ø {Math.Round(halfWidthM * 2000):0} mm" : string.Empty;

    private static double Safe(Func<double> read)
    {
        try
        {
            double value = read();
            return double.IsFinite(value) ? value : 0;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            return 0;
        }
    }
}
