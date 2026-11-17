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
