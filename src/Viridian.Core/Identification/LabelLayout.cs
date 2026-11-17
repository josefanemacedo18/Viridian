using Viridian.Core.Geometry;

namespace Viridian.Core.Identification;

/// <summary>Posição escolhida para uma etiqueta.</summary>
/// <param name="TopLeft">Canto superior esquerdo do texto (ponto de inserção com alinhamento à esquerda).</param>
/// <param name="Box">Retângulo ocupado pelo texto.</param>
/// <param name="NeedsLeader">Verdadeiro quando a etiqueta precisou se afastar e deve ganhar linha indicadora.</param>
public readonly record struct LabelPlacement(Point2 TopLeft, Bounds2 Box, bool NeedsLeader);

/// <summary>
/// Escolhe a posição de cada etiqueta ao redor do elemento, evitando sobrepor textos já existentes
/// na vista e as etiquetas criadas na mesma operação.
/// </summary>
public sealed class LabelLayout
{
    // Ordem de preferência: nordeste (padrão), sudeste, noroeste, sudoeste, leste, oeste, norte, sul.
    private static readonly (double X, double Y)[] Directions =
    {
        (1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1),
    };

    private readonly List<Bounds2> _occupied = new();

    /// <summary>Área já ocupada por textos existentes.</summary>
    public void Occupy(Bounds2 box)
    {
        if (box.IsValid && box.Width > 0 && box.Height > 0)
        {
            _occupied.Add(box);
        }
    }

    /// <summary>Tamanho aproximado do texto no modelo, em metros.</summary>
    /// <param name="text">Texto (pode ter várias linhas).</param>
    /// <param name="textHeightMm">Altura do texto no papel.</param>
    /// <param name="scale">Denominador da escala da vista.</param>
    public static (double Width, double Height) EstimateSize(string text, double textHeightMm, double scale)
    {
        string[] lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        int longest = lines.Max(l => l.Length);
        double factor = Math.Max(1, scale) / 1000.0;
        return (Math.Max(1, longest) * textHeightMm * 0.62 * factor, lines.Length * textHeightMm * 1.45 * factor);
    }

    /// <summary>
    /// Posição da etiqueta. Tenta oito direções à distância informada e depois a distâncias maiores;
    /// se nada servir, fica na posição padrão (nordeste).
    /// </summary>
    public LabelPlacement Place(Point2 target, double distance, double width, double height, double gap)
    {
        distance = Math.Max(0, distance);
        gap = Math.Max(0, gap);
        LabelPlacement? fallback = null;
        for (int ring = 0; ring < 4; ring++)
        {
            double d = distance + ring * (height + gap) * 1.5;
            foreach ((double dx, double dy) in Directions)
            {
                LabelPlacement candidate = Candidate(target, d, dx, dy, width, height, ring > 0);
                fallback ??= candidate;
                if (!_occupied.Any(o => Overlaps(o, candidate.Box.Expand(gap / 2))))
                {
                    _occupied.Add(candidate.Box);
                    return candidate;
                }
            }
        }

        _occupied.Add(fallback!.Value.Box);
        return fallback.Value;
    }

    private static LabelPlacement Candidate(Point2 target, double d, double dx, double dy, double width, double height, bool far)
    {
        // Âncora do texto no lado voltado para o elemento.
        double x = dx > 0 ? target.X + d : dx < 0 ? target.X - d - width : target.X - width / 2;
        double top = dy > 0 ? target.Y + d + height : dy < 0 ? target.Y - d : target.Y + height / 2;
        if (dx == 0)
        {
            top = dy > 0 ? target.Y + d + height : target.Y - d;
        }

        var box = new Bounds2(x, top - height, x + width, top);
        return new LabelPlacement(new Point2(x, top), box, far);
    }

    private static bool Overlaps(Bounds2 a, Bounds2 b) =>
        a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY;
}
