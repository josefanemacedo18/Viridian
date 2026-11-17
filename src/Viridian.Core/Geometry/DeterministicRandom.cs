namespace Viridian.Core.Geometry;

/// <summary>
/// Gerador pseudoaleatório determinístico (SplitMix64). A mesma semente produz sempre a mesma
/// sequência, em qualquer máquina e versão do .NET, o que mantém símbolos e distribuições estáveis.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed)
    {
        _state = unchecked((ulong)seed);
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Valor uniforme em [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Valor uniforme em [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Inteiro uniforme em [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        return minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
    }

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Hash estável de texto (FNV-1a de 32 bits), usado para derivar sementes de nomes.</summary>
    public static int StableHash(string? text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>Combina vários inteiros em uma única semente estável.</summary>
    public static long Combine(params long[] values)
    {
        unchecked
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (long v in values)
            {
                h ^= (ulong)v;
                h *= 0x100000001B3UL;
                h ^= h >> 29;
            }

            return (long)h;
        }
    }
}
