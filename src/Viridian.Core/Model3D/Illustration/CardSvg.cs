using System.Globalization;
using System.Text;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Prévias em SVG da vegetação ilustrada: as regiões achatadas e a malha triangulada que vai para a vista 3D.</summary>
public static class CardSvg
{
    /// <summary>As regiões de cor de uma imagem (de frente ou espelhada).</summary>
    public static string Face(CardRegions regions, double plantSize, int size = 800, bool mirrored = false) =>
        Wrap(size, string.Empty, Group(regions, plantSize, size, mirrored ? $"translate({size},0) scale(-1,1)" : string.Empty));

    /// <summary>
    /// A imagem exatamente como vai para a placa de vídeo: os triângulos de cada camada, do fundo para a frente.
    /// <paramref name="style"/>: "cor", "linha" (linha oculta: contorno preto e branco) ou "arame" (só o contorno).
    /// </summary>
    public static string Mesh(SpriteMesh mesh, double plantSize, int size = 800, string style = "cor", bool centerVertically = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        double k = size * 0.9 / Math.Max(1e-6, plantSize);
        double cy = centerVertically ? (mesh.MinY + mesh.MaxY) / 2 : 0;
        var sb = new StringBuilder("<g>");
        IReadOnlyList<int> list = style == "arame" ? mesh.OutlineTriangles : mesh.Triangles;
        var tris = new List<(double Z, int I)>();
        for (int i = 0; i + 2 < list.Count; i += 3)
        {
            tris.Add((mesh.Vertices[list[i]].Z, i));
        }

        foreach ((double _, int i) in tris.OrderBy(t => t.Z).ThenBy(t => t.I))
        {
            SpriteVertex a = mesh.Vertices[list[i]], b = mesh.Vertices[list[i + 1]], c = mesh.Vertices[list[i + 2]];
            string color = style == "linha" && !a.Outline ? "#FFFFFF" : a.Color.Hex;
            double X(SpriteVertex v) => size / 2.0 + v.X * k;
            double Y(SpriteVertex v) => centerVertically ? size / 2.0 - (v.Y - cy) * k : size * 0.96 - v.Y * k;
            sb.Append(CultureInfo.InvariantCulture,
                $"<path d='M{X(a):0.##},{Y(a):0.##}L{X(b):0.##},{Y(b):0.##}L{X(c):0.##},{Y(c):0.##}Z' fill='{color}' stroke='{color}' stroke-width='0.35' stroke-linejoin='round'/>");
        }

        sb.Append("</g>");
        return Wrap(size, string.Empty, sb.ToString());
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
