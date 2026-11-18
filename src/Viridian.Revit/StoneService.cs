using Autodesk.Revit.DB;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>
/// Pedras e coberturas no Revit: cobertura de uma região (canteiro) e faixas de borda de caminho, criadas como pisos
/// do tipo do material, no nível da vista em planta. Tudo precisa de transação.
/// </summary>
public static class StoneService
{
    /// <summary>Cria um piso do tipo informado cobrindo o polígono (com furos), no nível.</summary>
    public static Floor CreateFloor(Document document, FloorType type, Polygon2 polygon, Level level)
    {
        IList<CurveLoop> loops = BoundaryReader.ToCurveLoops(polygon, level.ProjectElevation, document.Application.ShortCurveTolerance);
        if (loops.Count == 0)
        {
            throw new ViridianException("O contorno é pequeno demais para criar a cobertura.");
        }

        try
        {
            return Floor.Create(document, loops, type.Id, level.Id);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            throw new ViridianException("O Revit não aceitou o contorno da cobertura (verifique se ele não se cruza).\n" + ex.Message);
        }
    }

    /// <summary>
    /// Faixas de borda ao longo dos eixos: uma por eixo. <paramref name="region"/> (quando a borda contorna um piso ou
    /// região) define o lado "de dentro" dos contornos fechados. Devolve os pisos criados e os eixos recusados.
    /// </summary>
    public static (List<Floor> Created, int Failed) CreateBorders(
        Document document, FloorType type, IReadOnlyList<PlantingPath> paths, double widthM, StripSide side, Level level, Polygon2? region)
    {
        var created = new List<Floor>();
        int failed = 0;
        foreach (PlantingPath path in paths)
        {
            List<IReadOnlyList<Point2>> rings = BorderStrip.Build(path.Points, path.Closed, widthM, side, region is null ? null : region.Contains);
            if (rings.Count == 0)
            {
                failed++;
                continue;
            }

            var strip = new Polygon2(rings);
            if (strip.HasSelfIntersections())
            {
                ViridianLog.Warn($"Borda de caminho: a faixa de {widthM:0.00} m se cruza em curvas fechadas; eixo ignorado.");
                failed++;
                continue;
            }

            try
            {
                created.Add(CreateFloor(document, type, strip, level));
            }
            catch (ViridianException ex)
            {
                ViridianLog.Warn("Borda de caminho: " + ex.Message);
                failed++;
            }
        }

        return (created, failed);
    }

    /// <summary>Contornos (anéis) de um polígono como eixos fechados.</summary>
    public static List<PlantingPath> RingsAsPaths(Polygon2 polygon) =>
        polygon.Rings.Where(r => r.Count >= 3).Select(r => new PlantingPath(r.ToList(), true)).ToList();
}
