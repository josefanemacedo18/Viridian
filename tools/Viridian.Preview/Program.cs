using System.Globalization;
using System.Text;
using Viridian.Core.Geometry;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

// Ferramenta de desenvolvimento: gera pranchas SVG com os símbolos para conferência visual.
// Uso: Viridian.Preview <pasta de saída>
string outDir = args.Length > 0 ? args[0] : "preview";
Directory.CreateDirectory(outDir);

File.WriteAllText(Path.Combine(outDir, "estilos.svg"), SvgSheets.StyleSheet());
File.WriteAllText(Path.Combine(outDir, "perfis.svg"), SvgSheets.ProfileSheet());
File.WriteAllText(Path.Combine(outDir, "variantes.svg"), SvgSheets.VariantSheet());
File.WriteAllText(Path.Combine(outDir, "hachuras.svg"), SvgSheets.HatchSheet());
File.WriteAllText(Path.Combine(outDir, "distribuicoes.svg"), SvgSheets.DistributionSheet());
Console.WriteLine("Pranchas geradas em " + Path.GetFullPath(outDir));

internal static class SvgSheets
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string StyleSheet()
    {
        var styles = SymbolStyles.All;
        const int cols = 6;
        const double cell = 250;
        int rows = (int)Math.Ceiling(styles.Count / (double)cols);
        var sb = Begin(cols * cell, rows * (cell + 30));
        for (int i = 0; i < styles.Count; i++)
        {
            double cx = (i % cols) * cell + cell / 2;
            double cy = (i / cols) * (cell + 30) + cell / 2;
            double diameter = DefaultDiameter(styles[i]);
            PlantSymbol symbol = SymbolGenerator.Generate(styles[i], diameter, 1234 + i);
            Draw(sb, symbol, DetailProfile.Detalhado, cx, cy, (cell - 40) / diameter);
            Text(sb, cx, cy + cell / 2 + 12, SymbolStyles.DisplayName(styles[i]) + $" · Ø{diameter.ToString("0.#", Inv)} m");
        }

        return End(sb);
    }

    public static string ProfileSheet()
    {
        var styles = new[]
        {
            SymbolStyle.ArvoreArredondada, SymbolStyle.ArvoreEspalhada, SymbolStyle.ArvorePiramidal,
            SymbolStyle.Palmeira, SymbolStyle.ArbustoNuvem, SymbolStyle.Herbacea,
        };
        var profiles = Enum.GetValues<DetailProfile>();
        const double cell = 230;
        var sb = Begin(profiles.Length * cell + 20, styles.Length * (cell + 26));
        for (int r = 0; r < styles.Length; r++)
        {
            double diameter = DefaultDiameter(styles[r]);
            PlantSymbol symbol = SymbolGenerator.Generate(styles[r], diameter, 77 + r);
            for (int c = 0; c < profiles.Length; c++)
            {
                double cx = c * cell + cell / 2 + 10;
                double cy = r * (cell + 26) + cell / 2;
                Draw(sb, symbol, profiles[c], cx, cy, (cell - 40) / diameter);
                Text(sb, cx, cy + cell / 2 + 10, $"{ScaleProfileSet.DisplayName(profiles[c])} · {symbol.Count(profiles[c])} linhas");
            }
        }

        return End(sb);
    }

    public static string VariantSheet()
    {
        var styles = new[] { SymbolStyle.ArvoreArredondada, SymbolStyle.ArvoreIrregular, SymbolStyle.Palmeira, SymbolStyle.ArbustoNuvem };
        const double cell = 230;
        var sb = Begin(4 * cell + 20, styles.Length * (cell + 26));
        for (int r = 0; r < styles.Length; r++)
        {
            double diameter = DefaultDiameter(styles[r]);
            for (int v = 0; v < 4; v++)
            {
                PlantSymbol symbol = SymbolGenerator.Generate(styles[r], diameter, 500 + r, v);
                double cx = v * cell + cell / 2 + 10;
                double cy = r * (cell + 26) + cell / 2;
                Draw(sb, symbol, DetailProfile.Detalhado, cx, cy, (cell - 40) / diameter);
                Text(sb, cx, cy + cell / 2 + 10, $"Variante {v + 1}");
            }
        }

        return End(sb);
    }

    public static string DistributionSheet()
    {
        var bed = new Polygon2(new[]
        {
            new Point2(0, 0), new Point2(14, -1), new Point2(18, 6), new Point2(12, 12), new Point2(4, 11), new Point2(-1, 6),
        });
        var path = new[] { new Point2(-1, 1), new Point2(6, 6), new Point2(12, 3), new Point2(18, 10) };
        var cases = new (string Title, Viridian.Core.Distribution.DistributionRequest Request, bool UseRegion)[]
        {
            ("Triangular", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Triangular, SpacingM = 1.2, EdgeMarginM = 0.4 }, true),
            ("Natural", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Natural, SpacingM = 1.2, EdgeMarginM = 0.4, Seed = 4 }, true),
            ("Linear — fileira dupla alternada", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Linear, SpacingM = 1.6, Path = path, Rows = 2, RowArrangement = Viridian.Core.Distribution.RowArrangement.Alternadas, LinearAlignment = Viridian.Core.Distribution.LinearAlignment.Ajustado }, false),
            ("Bordadura — 2 fileiras", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Bordadura, SpacingM = 1.0, EdgeMarginM = 0.4, Rows = 2, RowArrangement = Viridian.Core.Distribution.RowArrangement.Alternadas }, true),
            ("Circular — anéis", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Circular, SpacingM = 1.2, RowSpacingM = 1.3, EdgeMarginM = 0.4 }, true),
            ("Agrupamentos 3–7", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Agrupamentos, SpacingM = 0.8, EdgeMarginM = 0.4, GroupGapM = 1.4, Seed = 9 }, true),
            ("Degradê", new() { Pattern = Viridian.Core.Distribution.DistributionPattern.Gradiente, SpacingM = 0.7, EdgeMarginM = 0.3, GradientEndPercent = 5, Seed = 2 }, true),
        };
        const double cell = 300;
        const double scale = 14;
        var sb = Begin(4 * cell, 2 * (cell + 10));
        for (int i = 0; i < cases.Length; i++)
        {
            double ox = (i % 4) * cell + 30;
            double oy = (i / 4) * (cell + 10) + 250;
            var result = Viridian.Core.Distribution.PlantDistributor.Distribute(cases[i].UseRegion ? bed : null, cases[i].Request);
            if (cases[i].UseRegion)
            {
                var ring = bed.Rings[0];
                sb.Append("<polygon points=\"" + string.Join(" ", ring.Select(p => (ox + p.X * scale).ToString("0.#", Inv) + "," + (oy - p.Y * scale).ToString("0.#", Inv))) + "\" fill=\"#EEF5F0\" stroke=\"#2F6B4F\" stroke-width=\"1.2\"/>");
            }
            else
            {
                sb.Append("<polyline points=\"" + string.Join(" ", path.Select(p => (ox + p.X * scale).ToString("0.#", Inv) + "," + (oy - p.Y * scale).ToString("0.#", Inv))) + "\" fill=\"none\" stroke=\"#C88A12\" stroke-width=\"1.2\" stroke-dasharray=\"4 3\"/>");
            }

            foreach (Point2 p in result.Points)
            {
                sb.Append(Inv, $"<circle cx=\"{ox + p.X * scale:0.#}\" cy=\"{oy - p.Y * scale:0.#}\" r=\"{0.45 * scale:0.#}\" fill=\"#9CC48A\" stroke=\"#2F6B4F\" stroke-width=\"0.8\"/>");
            }

            Text(sb, ox + 120, oy + 40, $"{cases[i].Title} · {result.Points.Count} un");
        }

        return End(sb);
    }

    public static string HatchSheet()
    {
        var patterns = Viridian.Core.Hatch.HatchLibrary.BuiltIn;
        const int cols = 4;
        const double cell = 260;
        const double pxPerMm = 4;
        int rows = (int)Math.Ceiling(patterns.Count / (double)cols);
        var sb = Begin(cols * cell, rows * (cell * 0.6 + 30));
        for (int i = 0; i < patterns.Count; i++)
        {
            double x0 = (i % cols) * cell + 10;
            double y0 = (i / cols) * (cell * 0.6 + 30) + 10;
            double w = cell - 20, h = cell * 0.6 - 10;
            var area = new Bounds2(0, 0, w / pxPerMm, h / pxPerMm);
            sb.Append(Inv, $"<rect x=\"{x0:0.#}\" y=\"{y0:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.6\"/>");
            foreach (var (a, b) in Viridian.Core.Hatch.HatchRasterizer.Segments(patterns[i], area))
            {
                sb.Append(Inv, $"<line x1=\"{x0 + a.X * pxPerMm:0.##}\" y1=\"{y0 + h - a.Y * pxPerMm:0.##}\" x2=\"{x0 + b.X * pxPerMm:0.##}\" y2=\"{y0 + h - b.Y * pxPerMm:0.##}\" stroke=\"black\" stroke-width=\"0.8\" stroke-linecap=\"round\"/>");
            }

            Text(sb, x0 + w / 2, y0 + h + 16, patterns[i].Name + " · " + patterns[i].Application);
        }

        return End(sb);
    }

    private static double DefaultDiameter(SymbolStyle style) => style switch
    {
        SymbolStyle.ArvoreColunar => 3,
        SymbolStyle.ArvorePiramidal => 5,
        SymbolStyle.Palmeira or SymbolStyle.PalmeiraLeque => 4,
        SymbolStyle.ArbustoNuvem or SymbolStyle.ArbustoPontiagudo => 1.2,
        SymbolStyle.Herbacea => 0.6,
        SymbolStyle.Forracao => 0.4,
        SymbolStyle.Gramado => 0.3,
        SymbolStyle.Trepadeira => 1.0,
        SymbolStyle.Aquatica => 0.8,
        SymbolStyle.Simples => 2,
        _ => 8,
    };

    private static StringBuilder Begin(double width, double height)
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width:0}\" height=\"{height:0}\" viewBox=\"0 0 {width:0} {height:0}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        return sb;
    }

    private static string End(StringBuilder sb) => sb.Append("</svg>").ToString();

    private static void Text(StringBuilder sb, double x, double y, string text) =>
        sb.Append(Inv, $"<text x=\"{x:0.#}\" y=\"{y:0.#}\" font-family=\"Segoe UI, Arial\" font-size=\"11\" text-anchor=\"middle\" fill=\"#333\">{System.Net.WebUtility.HtmlEncode(text)}</text>");

    private static double Weight(SymbolTier tier) => tier switch
    {
        SymbolTier.Outline or SymbolTier.MinimalOutline => 1.5,
        SymbolTier.Center => 1.2,
        SymbolTier.Essential => 1.0,
        SymbolTier.Principal => 0.8,
        _ => 0.55,
    };

    public static void Draw(StringBuilder sb, PlantSymbol symbol, DetailProfile profile, double cx, double cy, double scale)
    {
        foreach (SymbolPrimitive p in symbol.ForProfile(profile))
        {
            string stroke = $"stroke=\"black\" stroke-width=\"{Weight(p.Tier).ToString("0.##", Inv)}\" fill=\"none\" stroke-linecap=\"round\"";
            switch (p)
            {
                case SymbolLine l:
                    sb.Append(Inv, $"<line x1=\"{cx + l.A.X * scale:0.##}\" y1=\"{cy - l.A.Y * scale:0.##}\" x2=\"{cx + l.B.X * scale:0.##}\" y2=\"{cy - l.B.Y * scale:0.##}\" {stroke}/>");
                    break;
                case SymbolArc a:
                    if (ArcInfo.TryFromThreePoints(a.Start, a.Mid, a.End, out ArcInfo info))
                    {
                        double radius = info.Radius * scale;
                        int large = Math.Abs(info.Sweep) > Math.PI ? 1 : 0;
                        int sweepFlag = info.Sweep > 0 ? 0 : 1; // eixo Y invertido no SVG
                        sb.Append(Inv, $"<path d=\"M {cx + a.Start.X * scale:0.##} {cy - a.Start.Y * scale:0.##} A {radius:0.##} {radius:0.##} 0 {large} {sweepFlag} {cx + a.End.X * scale:0.##} {cy - a.End.Y * scale:0.##}\" {stroke}/>");
                    }

                    break;
            }
        }
    }
}
