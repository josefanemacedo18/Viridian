using System.Globalization;
using System.Text;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Prévia em SVG exatamente da geometria que vai para a família: as regiões achatadas de cada plano.</summary>
public static class CardSvg
{
    /// <summary>Uma face do cartão (plano A ou B, de frente ou espelhada pelo verso).</summary>
    public static string Face(CardRegions regions, double plantSize, int size = 800, bool mirrored = false) =>
        Wrap(size, string.Empty, Group(regions, plantSize, size, mirrored ? $"translate({size},0) scale(-1,1)" : string.Empty));

    /// <summary>Vista de quina dos dois planos cruzados, como a referência: metade esquerda do plano A e direita do B à frente.</summary>
    public static string Corner(CardRegions planeA, CardRegions planeB, double plantSize, int size = 800)
    {
        string squeeze = string.Create(CultureInfo.InvariantCulture, $"translate({size / 2},0) scale(0.7071,1) translate({-size / 2},0)");
        string defs = string.Create(CultureInfo.InvariantCulture,
            $"<defs><clipPath id='cl'><rect x='0' y='0' width='{size / 2}' height='{size}'/></clipPath><clipPath id='cr'><rect x='{size / 2}' y='0' width='{size / 2}' height='{size}'/></clipPath></defs>");
        var sb = new StringBuilder();
        sb.Append($"<g clip-path='url(#cr)' opacity='0.92'>{Group(planeA, plantSize, size, squeeze)}</g>");
        sb.Append($"<g clip-path='url(#cl)' opacity='0.92'>{Group(planeB, plantSize, size, squeeze)}</g>");
        sb.Append($"<g clip-path='url(#cl)'>{Group(planeA, plantSize, size, squeeze)}</g>");
        sb.Append($"<g clip-path='url(#cr)'>{Group(planeB, plantSize, size, squeeze)}</g>");
        return Wrap(size, defs, sb.ToString());
    }

    private static string Wrap(int size, string defs, string inner) =>
        string.Create(CultureInfo.InvariantCulture, $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='0 0 {size} {size}'>{defs}{inner}</svg>");

    private static string Group(CardRegions regions, double plantSize, int size, string transform)
    {
        double k = size * 0.9 / Math.Max(1e-6, plantSize);
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrEmpty(transform) ? "<g>" : $"<g transform='{transform}'>");
        foreach (ColorRegion region in regions.Regions)
        {
            var d = new StringBuilder();
            foreach (List<Point2> loop in region.Loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    double x = size / 2.0 + loop[i].X * k, y = size * 0.96 - loop[i].Y * k;
                    d.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? 'M' : 'L')}{x:0.##},{y:0.##}");
                }

                d.Append('Z');
            }

            sb.Append($"<path d='{d}' fill='{region.Color.Hex}' fill-rule='evenodd'/>");
        }

        sb.Append("</g>");
        return sb.ToString();
    }
}
