using System.Globalization;
using System.Text;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Paving;

/// <summary>Prancha de amostras da biblioteca de pisos (hachura de modelo sobre a cor da superfície).</summary>
internal static class PavingPreview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Sheet()
    {
        List<PavingType> all = PavingDefaults.Create();
        const int cols = 6;
        const double cell = 210, swatch = 180, sizeMm = 1500;
        int rows = (int)Math.Ceiling(all.Count / (double)cols);
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns='http://www.w3.org/2000/svg' width='{cols * cell + 20}' height='{rows * (cell + 40) + 20}'>");
        sb.Append("<rect width='100%' height='100%' fill='#FFFFFF'/>");
        for (int i = 0; i < all.Count; i++)
        {
            PavingType t = all[i];
            double x0 = 10 + i % cols * cell, y0 = 10 + i / cols * (cell + 40);
            sb.Append(Inv, $"<rect x='{x0}' y='{y0}' width='{swatch}' height='{swatch}' fill='{t.SurfaceRgb.Hex}' stroke='#333' stroke-width='0.6'/>");
            HatchPattern h = PavingPatterns.Build(t);
            double k = swatch / sizeMm;
            foreach ((Point2 a, Point2 b) in HatchRasterizer.Segments(h, new Bounds2(0, 0, sizeMm, sizeMm), 30000))
            {
                sb.Append(Inv, $"<line x1='{x0 + a.X * k:0.##}' y1='{y0 + swatch - a.Y * k:0.##}' x2='{x0 + b.X * k:0.##}' y2='{y0 + swatch - b.Y * k:0.##}' stroke='{t.JointRgb.Hex}' stroke-width='0.9'/>");
            }

            sb.Append(Inv, $"<text x='{x0}' y='{y0 + swatch + 14}' font-family='Arial' font-size='10.5' font-weight='bold'>{System.Net.WebUtility.HtmlEncode(t.Name)}</text>");
            sb.Append(Inv, $"<text x='{x0}' y='{y0 + swatch + 27}' font-family='Arial' font-size='9.5' fill='#555'>{System.Net.WebUtility.HtmlEncode(PavingLabels.Of(t.Pattern))} · 1,5 × 1,5 m</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
