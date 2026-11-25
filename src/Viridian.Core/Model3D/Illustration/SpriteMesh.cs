using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Vértice da imagem: x para a direita, y para cima (a partir do chão), z para quem olha (m).</summary>
public readonly record struct SpriteVertex(double X, double Y, double Z, Rgb Color, bool Outline);

/// <summary>
/// Imagem ilustrada pronta para a placa de vídeo: as regiões de cor trianguladas num único plano, cada cor numa
/// camada a poucos milímetros da anterior (o contorno preto na frente), para não haver disputa de profundidade.
/// Os triângulos saem no sentido anti-horário vistos de +z.
/// </summary>
public sealed class SpriteMesh
{
    /// <summary>Distância entre as camadas de cor (m). Como a imagem fica sempre de frente, a diferença não aparece.</summary>
    public const double LayerStep = 0.0012;

    private SpriteMesh(List<SpriteVertex> vertices, List<int> triangles, List<int> outlineTriangles, CardRegions regions, CardArt art)
    {
        Vertices = vertices;
        Triangles = triangles;
        OutlineTriangles = outlineTriangles;
        Regions = regions;
        Art = art;
        if (vertices.Count > 0)
        {
            MinX = vertices.Min(v => v.X);
            MaxX = vertices.Max(v => v.X);
            MinY = vertices.Min(v => v.Y);
            MaxY = vertices.Max(v => v.Y);
        }
    }

    public IReadOnlyList<SpriteVertex> Vertices { get; }

    /// <summary>Todos os triângulos (índices de três em três).</summary>
    public IReadOnlyList<int> Triangles { get; }

    /// <summary>Só os triângulos do contorno preto (para o estilo Estrutura de arame).</summary>
    public IReadOnlyList<int> OutlineTriangles { get; }

    /// <summary>Regiões que deram origem à malha (para prévias).</summary>
    public CardRegions Regions { get; }

    public CardArt Art { get; }

    public double MinX { get; }

    public double MaxX { get; }

    public double MinY { get; }

    public double MaxY { get; }

    public int TriangleCount => Triangles.Count / 3;

    /// <summary>Área coberta pelos triângulos (m²), para conferência.</summary>
    public double CoveredArea(bool outlineOnly = false)
    {
        IReadOnlyList<int> list = outlineOnly ? OutlineTriangles : Triangles;
        double sum = 0;
        for (int i = 0; i + 2 < list.Count; i += 3)
        {
            SpriteVertex a = Vertices[list[i]], b = Vertices[list[i + 1]], c = Vertices[list[i + 2]];
            sum += ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
        }

        return sum;
    }

    /// <summary>Triangula as regiões de cor de uma imagem achatada.</summary>
    public static SpriteMesh From(CardRegions regions, CardArt art)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(art);
        var vertices = new List<SpriteVertex>();
        var triangles = new List<int>();
        var outline = new List<int>();

        // Camadas: cores na ordem do índice (o fundo primeiro) e o contorno preto por cima de todas.
        List<int> order = regions.Regions.Select(r => r.ColorIndex).Where(i => i != CardArt.Black).Distinct().OrderBy(i => i).ToList();
        foreach (ColorRegion region in regions.Regions)
        {
            bool black = region.ColorIndex == CardArt.Black;
            double z = (black ? order.Count + 1 : order.IndexOf(region.ColorIndex) + 1) * LayerStep;
            foreach ((List<Point2> outer, List<List<Point2>> holes) in Group(region.Loops))
            {
                var holeLists = holes.Cast<IReadOnlyList<Point2>>().ToList();
                List<int> local = Earcut.Triangulate(outer, holeLists);
                if (local.Count == 0)
                {
                    continue;
                }

                int start = vertices.Count;
                foreach (Point2 p in outer.Concat(holes.SelectMany(h => h)))
                {
                    vertices.Add(new SpriteVertex(p.X, p.Y, z, region.Color, black));
                }

                foreach (int index in local)
                {
                    triangles.Add(start + index);
                    if (black)
                    {
                        outline.Add(start + index);
                    }
                }
            }
        }

        return new SpriteMesh(vertices, triangles, outline, regions, art);
    }

    /// <summary>Separa os contornos externos (anti-horários) e põe cada furo no menor contorno externo que o contém.</summary>
    internal static List<(List<Point2> Outer, List<List<Point2>> Holes)> Group(List<List<Point2>> loops)
    {
        var outers = new List<(List<Point2> Loop, double Area, double MinX, double MinY, double MaxX, double MaxY, List<List<Point2>> Holes)>();
        var holes = new List<List<Point2>>();
        foreach (List<Point2> loop in loops)
        {
            if (loop.Count < 3)
            {
                continue;
            }

            double area = Geo.SignedArea(loop);
            if (area > 0)
            {
                outers.Add((loop, area, loop.Min(p => p.X), loop.Min(p => p.Y), loop.Max(p => p.X), loop.Max(p => p.Y), new List<List<Point2>>()));
            }
            else if (area < 0)
            {
                holes.Add(loop);
            }
        }

        outers.Sort((a, b) => a.Area.CompareTo(b.Area));
        foreach (List<Point2> hole in holes)
        {
            // Um ponto no meio de uma aresta do furo: nunca coincide com um vértice do contorno externo vizinho.
            Point2 probe = new((hole[0].X + hole[1].X) / 2, (hole[0].Y + hole[1].Y) / 2);
            foreach (var outer in outers)
            {
                if (probe.X >= outer.MinX && probe.X <= outer.MaxX && probe.Y >= outer.MinY && probe.Y <= outer.MaxY && Inside(outer.Loop, probe))
                {
                    outer.Holes.Add(hole);
                    break;
                }
            }
        }

        return outers.Select(o => (o.Loop, o.Holes)).ToList();
    }

    private static bool Inside(List<Point2> polygon, Point2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)
                && p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
