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

    /// <param name="scale">Escala de apresentação (1:scale) para textos e faixas; zero = a que põe o quadro em 75 cm.</param>
    public static Drawing Build(Polygon2? boundary, IReadOnlyList<GrowthPlant> plants, IReadOnlyList<GrowthMassing> massings, ColorSettings colors, int scale = 0)
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
        double gap = Math.Max(2, extent.Width * 0.14);
        double total = GrowthModel.Stages.Count * (extent.Width + gap) - gap;
        double mm = (scale > 0 ? scale : Math.Max(20, total * 1000 / 750)) / 1000.0;
        double header = 9 * mm, footer = 14 * mm, pad = 4 * mm;
        List<GrowthPlant> drawn = plants.Take(MaxPlants).ToList();
        Polygon2 area = boundary is { IsEmpty: false } ? boundary : new Polygon2(new[]
        {
            new Point2(extent.MinX, extent.MinY), new Point2(extent.MaxX, extent.MinY), new Point2(extent.MaxX, extent.MaxY), new Point2(extent.MinX, extent.MaxY),
        });

        for (int i = 0; i < GrowthModel.Stages.Count; i++)
        {
            GrowthStage stage = GrowthModel.Stages[i];
            var offset = new Point2(i * (extent.Width + gap) - extent.MinX, -extent.MinY);
            Point2 Move(Point2 p) => p + offset;
            double x0 = offset.X + extent.MinX - pad, x1 = offset.X + extent.MaxX + pad;
            double y0 = offset.Y + extent.MinY - pad - footer, y1 = offset.Y + extent.MaxY + pad + header;

            // Painel: fundo claro, faixa de título com a fase e rodapé com os números da fase.
            drawing.Box(x0, y0, x1, y1, DrawingStyle.Paper, edge: LinePen.Media);
            drawing.Box(x0, y1 - header, x1, y1, i == GrowthModel.Stages.Count - 1 ? DrawingStyle.Primary : DrawingStyle.Secondary);
            drawing.Text(new Point2((x0 + x1) / 2, y1 - header / 2), GrowthModel.Name(stage).ToUpperInvariant(), TextStyle.Claro, 0, 1.3);

            if (boundary is { IsEmpty: false })
            {
                foreach (IReadOnlyList<Point2> ring in boundary.Rings)
                {
                    drawing.Fill(ring.Select(Move).ToList(), new Rgb(0xF0, 0xEA, 0xDC));
                    drawing.Strokes(ring.Select(Move).ToList(), true, LinePen.Grossa);
                }
            }

            foreach (GrowthMassing m in massings)
            {
                // Maciços: cobertura cresce até fechar o canteiro (indicada pelo clareamento).
                double coverage = stage switch { GrowthStage.Plantio => 0.7, GrowthStage.TresAnos => 0.4, GrowthStage.SeisAnos => 0.18, _ => 0.05 };
                Rgb color = VegetationPalette.Foliage(m.Species, colors).Tint(coverage);
                foreach (IReadOnlyList<Point2> ring in m.Boundary.Rings.Take(1))
                {
                    drawing.Fill(ring.Select(Move).ToList(), color);
                    drawing.Strokes(ring.Select(Move).ToList(), true, LinePen.Fina);
                }
            }

            // Sombras das copas no chão (deslocadas para sudeste), depois as copas: maiores primeiro.
            var crowns = new List<(Point2 Center, double Radius)>();
            var sizes = drawn.Select(p => (Plant: p, Size: GrowthModel.Size(p.Species, stage))).ToList();
            foreach ((GrowthPlant p, (double crown, double _)) in sizes.Where(x => x.Size.CrownM > 0.3))
            {
                drawing.Fill(Outline(p.Species, crown, Move(p.Location) + new Point2(crown * 0.12, -crown * 0.12)), new Rgb(0xD5, 0xD8, 0xCC));
            }

            foreach ((GrowthPlant p, (double crown, double _)) in sizes.OrderByDescending(x => x.Plant.Species.CrownDiameterM))
            {
                List<Point2> outline = Outline(p.Species, crown, Move(p.Location));
                Rgb foliage = VegetationPalette.Foliage(p.Species, colors);
                drawing.Fill(outline, foliage);
                if (crown > 0.6)
                {
                    drawing.Fill(Outline(p.Species, crown * 0.55, Move(p.Location) + new Point2(-crown * 0.1, crown * 0.1)), foliage.Tint(0.12));
                }

                drawing.Strokes(outline, true, LinePen.Media);
                crowns.Add((p.Location, crown / 2));
                if (crown > 0.6)
                {
                    drawing.Stroke(Move(p.Location) + new Point2(-crown * 0.06, 0), Move(p.Location) + new Point2(crown * 0.06, 0), LinePen.Fina);
                    drawing.Stroke(Move(p.Location) + new Point2(0, -crown * 0.06), Move(p.Location) + new Point2(0, crown * 0.06), LinePen.Fina);
                }
            }

            // Rodapé: altura média e cobertura do canteiro pelas copas.
            double height = sizes.Count == 0 ? 0 : sizes.Average(x => x.Size.HeightM);
            double cover = CanopyCover(area, crowns);
            string stats = sizes.Count == 0
                ? "maciços: " + (stage == GrowthStage.Maturidade ? "fechados" : "em formação")
                : $"altura média {height.ToString("0.0", Pt)} m · copas cobrem {cover * 100:0}% do canteiro";
            drawing.Text(new Point2((x0 + x1) / 2, y0 + footer / 2), stats, TextStyle.Subtitulo, 0, 0.95);

            if (i < GrowthModel.Stages.Count - 1)
            {
                double ym = offset.Y + extent.MinY + extent.Height / 2;
                drawing.Arrow(new Point2(x1 + gap * 0.18, ym), new Point2(x1 + gap - pad * 2 - gap * 0.18, ym), LinePen.Media, 3 * mm, DrawingStyle.Accent);
            }
        }

        drawing.Text(new Point2(extent.MinX - pad, -extent.Height * 0 - pad - footer - 6 * mm),
            "Copas estimadas pela taxa de crescimento de cada espécie (valores de referência). Sombra das copas para leitura do volume.", TextStyle.Nota, -1, 0.9);
        return drawing;
    }

    private static readonly System.Globalization.CultureInfo Pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Fração (0 a 1) da área coberta pelas copas (círculos), por amostragem em grade.</summary>
    public static double CanopyCover(Polygon2 area, IReadOnlyList<(Point2 Center, double Radius)> crowns)
    {
        if (area.IsEmpty || crowns.Count == 0)
        {
            return 0;
        }

        Bounds2 b = area.Bounds;
        const int n = 80;
        int inside = 0, covered = 0;
        for (int iy = 0; iy < n; iy++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                var p = new Point2(b.MinX + (ix + 0.5) * b.Width / n, b.MinY + (iy + 0.5) * b.Height / n);
                if (!area.Contains(p))
                {
                    continue;
                }

                inside++;
                if (crowns.Any(c => (c.Center - p).LengthSquared <= c.Radius * c.Radius))
                {
                    covered++;
                }
            }
        }

        return inside == 0 ? 0 : (double)covered / inside;
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
