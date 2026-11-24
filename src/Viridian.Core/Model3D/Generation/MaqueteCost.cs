using Viridian.Core.Model;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Generation;

/// <summary>Estimativa do peso da maquete antes de gerar (triângulos), para avisar quando a vista 3D vai ficar lenta.</summary>
public static class MaqueteCost
{
    /// <summary>Fração típica do orçamento usada por um exemplar (o gerador raramente usa tudo).</summary>
    private const double TypicalUse = 0.85;

    /// <summary>Triângulos estimados para os exemplares no nível de qualidade escolhido.</summary>
    public static long Triangles(IEnumerable<Species> plants, IReadOnlyList<Plant3DProfile> profiles, QualityLevel quality)
    {
        ArgumentNullException.ThrowIfNull(plants);
        var bySpecies = new Dictionary<string, int>(StringComparer.Ordinal);
        long total = 0;
        foreach (Species species in plants)
        {
            if (!bySpecies.TryGetValue(species.Id, out int budget))
            {
                budget = ProfileResolver.Resolve(species, profiles).Profile.Budget.For(quality);
                bySpecies[species.Id] = budget;
            }

            total += (long)(budget * TypicalUse);
        }

        return total;
    }

    /// <summary>Nível sugerido para caber no limite, ou nulo quando nem o Simplificado cabe.</summary>
    public static QualityLevel? Suggest(IReadOnlyCollection<Species> plants, IReadOnlyList<Plant3DProfile> profiles, long limit)
    {
        foreach (QualityLevel q in new[] { QualityLevel.Detalhada, QualityLevel.Intermediaria, QualityLevel.Simplificada })
        {
            if (Triangles(plants, profiles, q) <= limit)
            {
                return q;
            }
        }

        return null;
    }
}
