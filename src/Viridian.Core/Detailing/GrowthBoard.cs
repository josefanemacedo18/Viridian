using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Exemplar em planta, para a simulação de crescimento.</summary>
public sealed record GrowthPlant(Species Species, Point2 Location);

/// <summary>Maciço em planta, para a simulação de crescimento.</summary>
public sealed record GrowthMassing(Species Species, Polygon2 Boundary);

/// <summary>
/// Simulação de crescimento em 2D: o mesmo trecho de planta desenhado lado a lado no plantio, aos 3 e 6 anos
/// e na maturidade, com as copas estimadas em cada momento.
/// </summary>
public static class GrowthBoard
{
    public const int MaxPlants = 600;

    public static Drawing Build(Polygon2? boundary, IReadOnlyList<GrowthPlant> plants, IReadOnlyList<GrowthMassing> massings, ColorSettings colors)
    {
        var drawing = new Drawing { Title = "Simulação de crescimento" };
        List<Point2> all = plants.Select(p => p.Location).Concat(massings.SelectMany(m => m.Boundary.Rings.SelectMany(r => r))).ToList();
        if (boundary is { IsEmpty: false })
        {
            all.AddRange(boundary.Rings.SelectMany(r => r));
        }

        if (all.Count == 0)
        {
            return drawing;
        }

        double margin = plants.Count == 0 ? 0.5 : plants.Max(p => p.Species.CrownDiameterM) / 2;
        Bounds2 extent = Bounds2.FromPoints(all).Expand(margin);
        double gap = Math.Max(2, extent.Width * 0.12);
        double labelOffset = Math.Max(0.8, extent.Height * 0.06);
        List<GrowthPlant> drawn = plants.Take(MaxPlants).ToList();

        for (int i = 0; i < GrowthModel.Stages.Count; i++)
        {
            GrowthStage stage = GrowthModel.Stages[i];
            var offset = new Point2(i * (extent.Width + gap) - extent.MinX, -extent.MinY);
            Point2 Move(Point2 p) => p + offset;

            if (boundary is { IsEmpty: false })
            {
                foreach (IReadOnlyList<Point2> ring in boundary.Rings)
                {
                    drawing.Polyline(ring.Select(Move).ToList(), true, heavy: true);
                }
            }

            foreach (GrowthMassing m in massings)
            {
                // Maciços: cobertura cresce até fechar o canteiro (transparência indicada pelo clareamento).
                double coverage = stage switch { GrowthStage.Plantio => 0.75, GrowthStage.TresAnos => 0.4, GrowthStage.SeisAnos => 0.2, _ => 0.1 };
                Rgb color = VegetationPalette.CrownFill(m.Species, colors).Tint(coverage);
                foreach (IReadOnlyList<Point2> ring in m.Boundary.Rings.Take(1))
                {
                    drawing.Fills.Add(new DrawFill(ring.Select(Move).ToList(), color));
                    drawing.Polyline(ring.Select(Move).ToList(), true);
                }
            }

            // Copas maiores primeiro, para as menores ficarem visíveis por cima. A forma é a do símbolo da espécie.
            foreach (GrowthPlant p in drawn.OrderByDescending(p => p.Species.CrownDiameterM))
            {
                (double crown, _) = GrowthModel.Size(p.Species, stage);
                List<Point2> outline = Outline(p.Species, crown, Move(p.Location));
                drawing.Fills.Add(new DrawFill(outline, VegetationPalette.CrownFill(p.Species, colors)));
                drawing.Polyline(outline, true);
                if (crown > 0.6)
                {
                    drawing.Line(Move(p.Location) + new Point2(-crown * 0.06, 0), Move(p.Location) + new Point2(crown * 0.06, 0));
                    drawing.Line(Move(p.Location) + new Point2(0, -crown * 0.06), Move(p.Location) + new Point2(0, crown * 0.06));
                }
            }

            double cx = offset.X + extent.MinX + extent.Width / 2;
            drawing.Text(new Point2(cx, offset.Y + extent.MinY - labelOffset), GrowthModel.Name(stage).ToUpperInvariant(), 0, 1.4);
        }

        drawing.Text(new Point2((GrowthModel.Stages.Count * (extent.Width + gap) - gap) / 2, -labelOffset * 3.0),
            "Copas estimadas pela taxa de crescimento de cada espécie (valores de referência)", 0, 0.9);
        return drawing;
    }

    /// <summary>Contorno da copa na forma do símbolo da espécie, no diâmetro do momento simulado.</summary>
    private static List<Point2> Outline(Species species, double crown, Point2 at)
    {
        IReadOnlyList<Point2> fill = Symbols.SymbolGenerator.Generate(species.SymbolStyle, Math.Max(0.05, species.CrownDiameterM), species.EffectiveSymbolSeed).FillOutline;
        if (fill.Count < 3)
        {
            return Drawing.Ellipse(at, crown / 2, crown / 2, 32);
        }

        double k = crown / Math.Max(0.05, species.CrownDiameterM);
        return fill.Select(p => at + p * k).ToList();
    }
}
