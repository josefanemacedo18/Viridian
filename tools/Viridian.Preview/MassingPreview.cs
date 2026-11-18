using System.Globalization;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Library;

/// <summary>Maciços de espécies diferentes lado a lado: traçado próprio e cores da biblioteca.</summary>
internal static class MassingPreview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Sheet()
    {
        var colors = new ColorSettings();
        List<Species> all = SeedCatalog.CreateSpecies();
        var picks = all.Where(s => s.Category == PlantCategory.Arbusto).Take(4)
            .Concat(all.Where(s => s.Category == PlantCategory.Forracao).Take(4))
            .Concat(all.Where(s => s.Category == PlantCategory.Herbacea).Take(4)).ToList();
        const int cols = 4;
        const double w = 230, h = 170, px = 2.6; // px por mm de papel
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns='http://www.w3.org/2000/svg' width='{cols * (w + 20) + 20}' height='{Math.Ceiling(picks.Count / (double)cols) * (h + 50) + 20}'><rect width='100%' height='100%' fill='#fff'/>");
        for (int i = 0; i < picks.Count; i++)
        {
            Species s = picks[i];
            double x0 = 20 + i % cols * (w + 20), y0 = 20 + i / cols * (h + 50);
            RegionPaint paint = VegetationPalette.Massing(s, colors)!;
            HatchPattern hatch = HatchLibrary.Find(HatchLibrary.KeyFor(s))!;
            string clip = "c" + i;
            string blob = $"M{x0 + 20},{y0 + 30} C{x0 + 60},{y0 - 10} {x0 + 170},{y0 + 10} {x0 + w - 10},{y0 + 50} C{x0 + w + 5},{y0 + 110} {x0 + 160},{y0 + h} {x0 + 90},{y0 + h - 10} C{x0 + 20},{y0 + h - 20} {x0 - 5},{y0 + 90} {x0 + 20},{y0 + 30} Z";
            sb.Append(Inv, $"<clipPath id='{clip}'><path d='{blob}'/></clipPath><path d='{blob}' fill='{paint.Background.Hex}'/>");
            sb.Append(Inv, $"<g clip-path='url(#{clip})'>");
            foreach ((Point2 a, Point2 b) in HatchRasterizer.Segments(hatch, new Bounds2(0, 0, (w + 20) / px, (h + 20) / px)))
            {
                sb.Append(Inv, $"<line x1='{x0 + a.X * px:0.#}' y1='{y0 + h - a.Y * px:0.#}' x2='{x0 + b.X * px:0.#}' y2='{y0 + h - b.Y * px:0.#}' stroke='{paint.Pattern.Hex}' stroke-width='1.1' stroke-linecap='round'/>");
            }

            sb.Append(Inv, $"</g><path d='{blob}' fill='none' stroke='#2E4A36' stroke-width='1.6'/>");
            sb.Append(Inv, $"<text x='{x0}' y='{y0 + h + 22}' font-family='Arial' font-size='12' font-weight='bold'>{System.Net.WebUtility.HtmlEncode(s.Code + " " + s.PopularName)}</text>");
            sb.Append(Inv, $"<text x='{x0}' y='{y0 + h + 37}' font-family='Arial' font-size='10' fill='#555'>{System.Net.WebUtility.HtmlEncode(hatch.Name)}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
