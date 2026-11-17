namespace Viridian.Core.Scale;

/// <summary>Perfil de representação gráfica aplicado a uma vista conforme a escala.</summary>
public enum DetailProfile
{
    Detalhado = 0,
    Intermediario = 1,
    Simplificado = 2,
    Minimo = 3,
}

/// <summary>Faixa de escala associada a um perfil. A faixa vale até o denominador máximo, inclusive.</summary>
public sealed class ScaleProfileRange
{
    public DetailProfile Profile { get; set; }

    /// <summary>Maior denominador de escala atendido pelo perfil (ex.: 50 para 1:50).</summary>
    public int MaxDenominator { get; set; }
}

/// <summary>Conjunto configurável de faixas de escala.</summary>
public sealed class ScaleProfileSet
{
    public List<ScaleProfileRange> Ranges { get; set; } = CreateDefaultRanges();

    public static List<ScaleProfileRange> CreateDefaultRanges() => new()
    {
        new ScaleProfileRange { Profile = DetailProfile.Detalhado, MaxDenominator = 50 },
        new ScaleProfileRange { Profile = DetailProfile.Intermediario, MaxDenominator = 100 },
        new ScaleProfileRange { Profile = DetailProfile.Simplificado, MaxDenominator = 250 },
    };

    /// <summary>
    /// Resolve o perfil de uma escala 1:<paramref name="denominator"/>. Acima da última faixa vale o perfil Mínimo.
    /// </summary>
    public DetailProfile Resolve(int denominator)
    {
        if (denominator <= 0)
        {
            return DetailProfile.Detalhado;
        }

        foreach (ScaleProfileRange range in Ranges.OrderBy(r => r.MaxDenominator))
        {
            if (denominator <= range.MaxDenominator)
            {
                return range.Profile;
            }
        }

        return DetailProfile.Minimo;
    }

    /// <summary>Corrige configurações inválidas (faixas ausentes, repetidas ou fora de ordem).</summary>
    public void Normalize()
    {
        Ranges ??= CreateDefaultRanges();
        Ranges = Ranges
            .Where(r => r is not null && r.MaxDenominator > 0 && r.Profile != DetailProfile.Minimo)
            .GroupBy(r => r.Profile)
            .Select(g => g.First())
            .OrderBy(r => r.Profile)
            .ToList();

        // Os limites precisam crescer do perfil mais detalhado para o mais simples.
        int previous = 0;
        foreach (ScaleProfileRange range in Ranges)
        {
            if (range.MaxDenominator <= previous)
            {
                range.MaxDenominator = previous + 1;
            }

            previous = range.MaxDenominator;
        }

        if (Ranges.Count == 0)
        {
            Ranges = CreateDefaultRanges();
        }
    }

    public static string DisplayName(DetailProfile profile) => profile switch
    {
        DetailProfile.Detalhado => "Detalhado",
        DetailProfile.Intermediario => "Intermediário",
        DetailProfile.Simplificado => "Simplificado",
        DetailProfile.Minimo => "Mínimo",
        _ => profile.ToString(),
    };

    public static string Description(DetailProfile profile) => profile switch
    {
        DetailProfile.Detalhado => "Copa, ramificações secundárias e detalhes internos",
        DetailProfile.Intermediario => "Copa, tronco e ramificações principais",
        DetailProfile.Simplificado => "Contorno de copa e estrutura essencial",
        DetailProfile.Minimo => "Contorno simplificado e centro",
        _ => string.Empty,
    };
}
