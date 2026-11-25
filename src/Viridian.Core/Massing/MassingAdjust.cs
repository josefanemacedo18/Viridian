using Viridian.Core.Geometry;

namespace Viridian.Core.Massing;

/// <summary>O que fazer com os exemplares que ficaram fora do novo contorno do maciço.</summary>
public enum OutsidePlants
{
    /// <summary>Mantém os exemplares e os seleciona para o projetista decidir (padrão).</summary>
    Avisar = 0,

    /// <summary>Apaga os exemplares que ficaram fora do contorno.</summary>
    Remover = 1,
}

/// <summary>Resultado do planejamento de um ajuste de contorno.</summary>
public sealed class MassingAdjustPlan
{
    /// <summary>Índices (na lista de existentes) dos exemplares dentro do novo contorno: ficam como estão.</summary>
    public IReadOnlyList<int> Inside { get; init; } = Array.Empty<int>();

    /// <summary>Índices dos exemplares que ficaram fora do novo contorno.</summary>
    public IReadOnlyList<int> Outside { get; init; } = Array.Empty<int>();

    /// <summary>Posições novas, só na parte acrescentada ao maciço e longe dos exemplares existentes.</summary>
    public IReadOnlyList<Point2> Added { get; init; } = Array.Empty<Point2>();

    /// <summary>Verdadeiro quando o maciço não tinha o limite anterior gravado (o preenchimento usa só a distância aos existentes).</summary>
    public bool WithoutPrevious { get; init; }
}

/// <summary>
/// Ajuste de contorno sem perder o plantio: os exemplares que continuam dentro do maciço não se movem,
/// e só a área acrescentada recebe exemplares novos, no mesmo padrão de distribuição.
/// </summary>
public static class MassingAdjust
{
    /// <summary>Fração do espaçamento abaixo da qual um ponto novo é considerado colado a um exemplar existente.</summary>
    public const double MinDistanceFactor = 0.75;

    /// <param name="boundary">Contorno atual da região.</param>
    /// <param name="previous">Contorno do último cálculo (nulo em maciços antigos).</param>
    /// <param name="existing">Posição dos exemplares atuais do maciço.</param>
    /// <param name="layout">Distribuição completa no contorno atual, com o padrão e a semente do maciço.</param>
    /// <param name="spacingM">Espaçamento entre exemplares (m).</param>
    /// <param name="outside">Destino dos exemplares fora do contorno (os mantidos também afastam os pontos novos).</param>
    public static MassingAdjustPlan Plan(
        Polygon2 boundary,
        Polygon2? previous,
        IReadOnlyList<Point2> existing,
        IReadOnlyList<Point2> layout,
        double spacingM,
        OutsidePlants outside = OutsidePlants.Avisar)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(layout);

        var inside = new List<int>();
        var outsideList = new List<int>();
        for (int i = 0; i < existing.Count; i++)
        {
            (boundary.Contains(existing[i]) ? inside : outsideList).Add(i);
        }

        // Pontos que continuam no projeto e, portanto, afastam os novos.
        var blockers = new List<Point2>(inside.Select(i => existing[i]));
        if (outside == OutsidePlants.Avisar)
        {
            blockers.AddRange(outsideList.Select(i => existing[i]));
        }

        double min = (double.IsFinite(spacingM) && spacingM > 0 ? spacingM : 0.3) * MinDistanceFactor;
        double min2 = min * min;
        var added = new List<Point2>();
        foreach (Point2 p in layout)
        {
            if (!boundary.Contains(p) || (previous is not null && previous.Contains(p)))
            {
                continue;
            }

            if (blockers.Any(b => (b - p).LengthSquared < min2) || added.Any(a => (a - p).LengthSquared < min2))
            {
                continue;
            }

            added.Add(p);
        }

        return new MassingAdjustPlan { Inside = inside, Outside = outsideList, Added = added, WithoutPrevious = previous is null };
    }

    /// <summary>Indica se dois contornos são o mesmo (vértices a menos de 1 mm), para saber se o maciço mudou.</summary>
    public static bool SameBoundary(Polygon2? a, Polygon2? b, double toleranceM = 0.001)
    {
        if (a is null || b is null || a.Rings.Count != b.Rings.Count)
        {
            return false;
        }

        double t2 = toleranceM * toleranceM;
        for (int r = 0; r < a.Rings.Count; r++)
        {
            IReadOnlyList<Point2> ra = a.Rings[r], rb = b.Rings[r];
            if (ra.Count != rb.Count)
            {
                return false;
            }

            for (int i = 0; i < ra.Count; i++)
            {
                if ((ra[i] - rb[i]).LengthSquared > t2)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
