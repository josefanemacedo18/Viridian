using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Viridian.Core.Geometry;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Limite de uma região de plantio lido do modelo, em metros.</summary>
public sealed class RegionBoundary
{
    public RegionBoundary(Polygon2 polygon, string source, FilledRegion? existingRegion = null)
    {
        Polygon = polygon;
        Source = source;
        ExistingRegion = existingRegion;
    }

    public Polygon2 Polygon { get; }

    /// <summary>Descrição do elemento de origem, para mensagens.</summary>
    public string Source { get; }

    /// <summary>Região preenchida de origem, quando o limite veio de uma.</summary>
    public FilledRegion? ExistingRegion { get; }
}

/// <summary>Lê limites de elementos existentes (regiões, pisos, toposólidos, áreas e ambientes).</summary>
public static class BoundaryReader
{
    public static bool IsSupported(Element? element) =>
        element is FilledRegion or Floor or Toposolid or Area or Room;

    public static RegionBoundary Read(Element element)
    {
        Document document = element.Document;
        switch (element)
        {
            case FilledRegion region:
                return new RegionBoundary(FromLoops(region.GetBoundaries()), "região preenchida", region);

            case Floor floor:
                return new RegionBoundary(FromSketch(document, floor.SketchId, "piso"), "piso");

            case Toposolid toposolid:
                return new RegionBoundary(FromSketch(document, toposolid.SketchId, "toposólido"), "toposólido");

            case SpatialElement spatial when spatial is Area or Room:
            {
                using var options = new SpatialElementBoundaryOptions();
                IList<IList<BoundarySegment>>? loops = spatial.GetBoundarySegments(options);
                if (loops is null || loops.Count == 0)
                {
                    throw new ViridianException("O elemento selecionado não possui limites fechados (verifique se está delimitado).");
                }

                var rings = new List<List<Point2>>();
                foreach (IList<BoundarySegment> loop in loops)
                {
                    rings.AddRange(Chain(loop.Select(s => Tessellate(s.GetCurve())).ToList()));
                }

                return new RegionBoundary(new Polygon2(rings), spatial is Area ? "área" : "ambiente");
            }

            default:
                throw new ViridianException("Selecione uma região preenchida, piso, toposólido, área ou ambiente.");
        }
    }

    public static Polygon2 FromLoops(IEnumerable<CurveLoop> loops)
    {
        var rings = new List<List<Point2>>();
        foreach (CurveLoop loop in loops)
        {
            rings.AddRange(Chain(loop.Select(Tessellate).ToList()));
        }

        return new Polygon2(rings);
    }

    private static Polygon2 FromSketch(Document document, ElementId sketchId, string kind)
    {
        if (sketchId is null || sketchId == ElementId.InvalidElementId || document.GetElement(sketchId) is not Sketch sketch || sketch.Profile is null)
        {
            throw new ViridianException($"Não foi possível ler o contorno do {kind} selecionado. Delimite o canteiro por pontos.");
        }

        var rings = new List<List<Point2>>();
        foreach (CurveArray array in sketch.Profile)
        {
            var parts = new List<List<Point2>>();
            foreach (Curve curve in array)
            {
                parts.Add(Tessellate(curve));
            }

            rings.AddRange(Chain(parts));
        }

        return new Polygon2(rings);
    }

    private static List<Point2> Tessellate(Curve curve)
    {
        IList<XYZ> points = curve.Tessellate();
        return points.Select(RevitUnits.ToPoint2).ToList();
    }

    /// <summary>
    /// Encadeia trechos pelas extremidades formando anéis. Os trechos podem vir fora de ordem ou invertidos.
    /// </summary>
    private static List<List<Point2>> Chain(List<List<Point2>> parts)
    {
        const double tolerance = 0.002; // 2 mm
        var remaining = parts.Where(p => p.Count >= 2).ToList();
        var rings = new List<List<Point2>>();

        while (remaining.Count > 0)
        {
            var ring = new List<Point2>(remaining[0]);
            remaining.RemoveAt(0);

            bool extended = true;
            while (extended && ring[0].DistanceTo(ring[^1]) > tolerance)
            {
                extended = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    List<Point2> candidate = remaining[i];
                    if (candidate[0].DistanceTo(ring[^1]) <= tolerance)
                    {
                        ring.AddRange(candidate.Skip(1));
                    }
                    else if (candidate[^1].DistanceTo(ring[^1]) <= tolerance)
                    {
                        ring.AddRange(Enumerable.Reverse(candidate).Skip(1));
                    }
                    else
                    {
                        continue;
                    }

                    remaining.RemoveAt(i);
                    extended = true;
                    break;
                }
            }

            if (ring.Count >= 3)
            {
                rings.Add(ring);
            }
        }

        return rings;
    }

    /// <summary>Converte um polígono (metros) em laços de curvas do Revit, no plano horizontal de cota z.</summary>
    public static IList<CurveLoop> ToCurveLoops(Polygon2 polygon, double zFeet, double shortCurveTolerance)
    {
        var loops = new List<CurveLoop>();
        double minimum = Math.Max(shortCurveTolerance * 1.5, RevitUnits.ToFeet(0.002));
        foreach (IReadOnlyList<Point2> ring in polygon.Rings)
        {
            var points = new List<XYZ>();
            foreach (Point2 p in ring)
            {
                XYZ xyz = RevitUnits.ToXyz(p, zFeet);
                if (points.Count == 0 || points[^1].DistanceTo(xyz) >= minimum)
                {
                    points.Add(xyz);
                }
            }

            while (points.Count > 3 && points[0].DistanceTo(points[^1]) < minimum)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                continue;
            }

            var curves = new List<Curve>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                curves.Add(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            loops.Add(CurveLoop.Create(curves));
        }

        return loops;
    }
}

/// <summary>Seleção interativa de limites e pontos no Revit.</summary>
public static class RegionPicker
{
    /// <summary>
    /// Solicita os vértices de um polígono na vista ativa. ESC conclui; retorna null quando há menos de três pontos.
    /// Os trechos são desenhados provisoriamente para orientar o usuário e removidos ao final.
    /// </summary>
    public static Polygon2? PickPolygon(UIDocument uiDocument, string prompt)
    {
        IReadOnlyList<Point2> points = PickPoints(uiDocument, prompt, closePreview: true);
        return points.Count >= 3 ? new Polygon2(points) : null;
    }

    /// <summary>Solicita uma sequência de pontos (polilinha aberta). ESC conclui.</summary>
    public static IReadOnlyList<Point2> PickPath(UIDocument uiDocument, string prompt) =>
        PickPoints(uiDocument, prompt, closePreview: false);

    private static IReadOnlyList<Point2> PickPoints(UIDocument uiDocument, string prompt, bool closePreview)
    {
        Document document = uiDocument.Document;
        View view = uiDocument.ActiveView;
        var picked = new List<XYZ>();
        int needed = closePreview ? 3 : 2;

        // As linhas de apoio ficam em um grupo de transações que é desfeito ao final.
        var group = new TransactionGroup(document, "Viridian: delimitar");
        bool preview = false;
        try
        {
            preview = group.Start() == TransactionStatus.Started;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Seleção de pontos sem linhas de apoio.", ex);
        }

        try
        {
            if (preview)
            {
                EnsureWorkPlane(document, view);
            }

            const ObjectSnapTypes snaps = ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Nearest;
            while (true)
            {
                XYZ point;
                try
                {
                    string status = picked.Count < needed ? prompt : prompt + " — ESC para concluir";
                    point = uiDocument.Selection.PickPoint(snaps, status);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    throw new ViridianException("A vista ativa não possui plano de trabalho. Abra uma vista em planta e tente novamente.", ex);
                }

                if (picked.Count > 0 && picked[^1].DistanceTo(point) < RevitUnits.ToFeet(0.01))
                {
                    continue; // clique repetido no mesmo ponto
                }

                picked.Add(point);
                if (preview && picked.Count > 1)
                {
                    DrawPreview(document, view, picked[^2], picked[^1]);
                }
            }
        }
        finally
        {
            try
            {
                if (group.HasStarted() && !group.HasEnded())
                {
                    group.RollBack();
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Falha ao remover as linhas de apoio.", ex);
            }

            group.Dispose();
        }

        return picked.Select(RevitUnits.ToPoint2).ToList();
    }

    private static void EnsureWorkPlane(Document document, View view)
    {
        if (view.SketchPlane is not null || view is not ViewPlan plan || plan.GenLevel is null)
        {
            return;
        }

        try
        {
            using var transaction = new Transaction(document, "Viridian: plano de trabalho");
            transaction.Start();
            view.SketchPlane = SketchPlane.Create(document, plan.GenLevel.Id);
            transaction.Commit();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            ViridianLog.Warn("Não foi possível definir o plano de trabalho da vista.", ex);
        }
    }

    private static void DrawPreview(Document document, View view, XYZ from, XYZ to)
    {
        try
        {
            XYZ a = Flatten(view, from);
            XYZ b = Flatten(view, to);
            if (a.DistanceTo(b) < document.Application.ShortCurveTolerance * 2)
            {
                return;
            }

            using var transaction = new Transaction(document, "Viridian: apoio");
            transaction.Start();
            document.Create.NewDetailCurve(view, Line.CreateBound(a, b));
            transaction.Commit();
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or InvalidOperationException)
        {
            // O apoio visual é opcional; a seleção continua sem ele.
            ViridianLog.Warn("Não foi possível desenhar a linha de apoio.", ex);
        }
    }

    /// <summary>Projeta o ponto no plano de detalhe da vista em planta.</summary>
    private static XYZ Flatten(View view, XYZ point)
    {
        double z = view is ViewPlan plan && plan.GenLevel is not null ? plan.GenLevel.ProjectElevation : point.Z;
        return new XYZ(point.X, point.Y, z);
    }
}

/// <summary>Filtro de seleção: elementos que podem servir de limite de plantio.</summary>
public sealed class BoundaryElementFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => BoundaryReader.IsSupported(elem);

    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Filtro de seleção: exemplares de vegetação.</summary>
public sealed class PlantingInstanceFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) =>
        elem is FamilyInstance instance
        && instance.Category is not null
        && instance.Category.BuiltInCategory == BuiltInCategory.OST_Planting;

    public bool AllowReference(Reference reference, XYZ position) => false;
}

/// <summary>Filtro de seleção: regiões preenchidas (maciços).</summary>
public sealed class FilledRegionFilter : ISelectionFilter
{
    private readonly bool _onlyMassings;

    public FilledRegionFilter(bool onlyMassings)
    {
        _onlyMassings = onlyMassings;
    }

    public bool AllowElement(Element elem) =>
        elem is FilledRegion && (!_onlyMassings || ElementData.KindOf(elem) == ElementData.KindMassing);

    public bool AllowReference(Reference reference, XYZ position) => false;
}
