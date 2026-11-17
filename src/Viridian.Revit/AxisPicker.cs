using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Como o eixo do plantio linear é definido.</summary>
public enum AxisSource
{
    /// <summary>Linha reta: dois cliques.</summary>
    LinhaReta,

    /// <summary>Polilinha: um clique por vértice, ESC conclui.</summary>
    Polilinha,

    /// <summary>Círculo: centro e um ponto do contorno.</summary>
    Circulo,

    /// <summary>Linhas, arcos, paredes e redes já existentes no projeto.</summary>
    Existentes,
}

/// <summary>Eixos de plantio e a descrição de onde vieram.</summary>
public sealed record AxisSelection(IReadOnlyList<PlantingPath> Paths, string Description);

/// <summary>Obtém, na vista ativa, os eixos do plantio linear.</summary>
public static class AxisPicker
{
    /// <summary>Elementos que podem servir de eixo: linhas de detalhe e de modelo e elementos com eixo (paredes, tubos, eletrodutos...).</summary>
    public static bool IsAxisElement(Element? element) => element switch
    {
        null => false,
        CurveElement curve => curve.GeometryCurve is not null,
        _ => element.Location is LocationCurve { Curve: not null },
    };

    /// <summary>Eixos a partir de elementos já selecionados (vazio quando a seleção não tem elementos de eixo).</summary>
    public static AxisSelection FromElements(IEnumerable<Element> elements)
    {
        var pieces = new List<IReadOnlyList<Point2>>();
        int count = 0;
        foreach (Element element in elements)
        {
            Curve? curve = element switch
            {
                CurveElement c => c.GeometryCurve,
                _ => (element.Location as LocationCurve)?.Curve,
            };
            if (curve is null)
            {
                continue;
            }

            IList<XYZ> points;
            try
            {
                points = curve.Tessellate();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                ViridianLog.Warn("Curva ignorada no eixo de plantio.", ex);
                continue;
            }

            if (points.Count >= 2)
            {
                pieces.Add(points.Select(RevitUnits.ToPoint2).ToList());
                count++;
            }
        }

        IReadOnlyList<PlantingPath> paths = PathTools.Chain(pieces);
        string description = count == 1 ? "1 elemento selecionado" : $"{count} elementos selecionados";
        if (paths.Count > 1)
        {
            description += $" ({paths.Count} eixos separados)";
        }

        return new AxisSelection(paths, description);
    }

    /// <summary>Solicita o eixo na vista ativa. Retorna eixo vazio quando o usuário cancela.</summary>
    public static AxisSelection Pick(UIDocument uiDocument, AxisSource source)
    {
        switch (source)
        {
            case AxisSource.LinhaReta:
            {
                IReadOnlyList<Point2> points = RegionPicker.PickPoints(uiDocument, "Plantio linear: clique no início e no fim da linha", 2, 2);
                return points.Count == 2
                    ? new AxisSelection(new[] { new PlantingPath(points, false) }, "linha reta desenhada")
                    : Empty;
            }

            case AxisSource.Polilinha:
            {
                IReadOnlyList<Point2> points = RegionPicker.PickPoints(uiDocument, "Plantio linear: clique nos vértices do eixo", 2, int.MaxValue);
                if (points.Count < 2)
                {
                    return Empty;
                }

                // Voltar ao primeiro ponto fecha o eixo (por exemplo, o contorno de um canteiro).
                bool closed = points.Count >= 4 && points[0].DistanceTo(points[^1]) <= 0.05;
                List<Point2> list = closed ? points.Take(points.Count - 1).ToList() : points.ToList();
                return new AxisSelection(new[] { new PlantingPath(list, closed) }, closed ? "polilinha fechada desenhada" : "polilinha desenhada");
            }

            case AxisSource.Circulo:
            {
                IReadOnlyList<Point2> points = RegionPicker.PickPoints(uiDocument, "Plantio em círculo: clique no centro e depois em um ponto do contorno", 2, 2);
                if (points.Count < 2 || points[0].DistanceTo(points[1]) < 0.05)
                {
                    return Empty;
                }

                double radius = points[0].DistanceTo(points[1]);
                return new AxisSelection(new[] { PathTools.Circle(points[0], radius) }, $"círculo de raio {radius:0.00} m");
            }

            default:
            {
                IList<Reference> references;
                try
                {
                    references = uiDocument.Selection.PickObjects(
                        ObjectType.Element,
                        new AxisElementFilter(),
                        "Selecione as linhas, arcos, paredes ou redes que formam o eixo e clique em Concluir");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Empty;
                }

                Document document = uiDocument.Document;
                return FromElements(references.Select(r => document.GetElement(r)).Where(e => e is not null));
            }
        }
    }

    private static AxisSelection Empty => new(Array.Empty<PlantingPath>(), string.Empty);
}

/// <summary>Filtro de seleção: elementos que podem servir de eixo de plantio linear.</summary>
public sealed class AxisElementFilter : ISelectionFilter
{
    public bool AllowElement(Element elem) => AxisPicker.IsAxisElement(elem);

    public bool AllowReference(Reference reference, XYZ position) => false;
}
