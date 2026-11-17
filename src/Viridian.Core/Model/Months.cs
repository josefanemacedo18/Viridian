using System.Text;

namespace Viridian.Core.Model;

/// <summary>Conjunto de meses do ano (bit 0 = janeiro ... bit 11 = dezembro).</summary>
public readonly record struct MonthSet(int Mask)
{
    public const int AllMask = 0xFFF;

    public static readonly MonthSet None = new(0);
    public static readonly MonthSet All = new(AllMask);

    public static readonly IReadOnlyList<string> Abbreviations =
        new[] { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };

    public bool IsEmpty => (Mask & AllMask) == 0;

    public int Count
    {
        get
        {
            int n = 0;
            for (int i = 0; i < 12; i++)
            {
                if (Contains(i + 1))
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Indica se o mês (1 a 12) pertence ao conjunto.</summary>
    public bool Contains(int month) => month is >= 1 and <= 12 && (Mask & (1 << (month - 1))) != 0;

    public MonthSet With(int month) => month is >= 1 and <= 12 ? new MonthSet(Mask | (1 << (month - 1))) : this;

    public MonthSet Union(MonthSet other) => new((Mask | other.Mask) & AllMask);

    /// <summary>Intervalo circular de meses (por exemplo, nov a fev atravessa a virada do ano).</summary>
    public static MonthSet Range(int from, int to)
    {
        var set = None;
        if (from is < 1 or > 12 || to is < 1 or > 12)
        {
            return set;
        }

        int m = from;
        for (int guard = 0; guard < 12; guard++)
        {
            set = set.With(m);
            if (m == to)
            {
                break;
            }

            m = m % 12 + 1;
        }

        return set;
    }

    /// <summary>Desloca todos os meses (por exemplo, +1 = mês seguinte), circularmente.</summary>
    public MonthSet Shift(int months)
    {
        var set = None;
        for (int m = 1; m <= 12; m++)
        {
            if (Contains(m))
            {
                set = set.With(((m - 1 + months) % 12 + 12) % 12 + 1);
            }
        }

        return set;
    }

    /// <summary>
    /// Interpreta textos de época como "ago–set", "jan–abr e jun–ago", "ano todo", "out-dez", "maio".
    /// Textos não reconhecidos resultam em conjunto vazio.
    /// </summary>
    public static MonthSet Parse(string? text)
    {
        string t = Normalize(text);
        if (t.Length == 0)
        {
            return None;
        }

        if (t.Contains("ano todo", StringComparison.Ordinal) || t.Contains("o ano inteiro", StringComparison.Ordinal))
        {
            return All;
        }

        var set = None;
        foreach (string part in t.Split(new[] { " e ", ",", ";", "/" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] ends = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ends.Length == 1 && TryMonth(ends[0], out int single))
            {
                set = set.With(single);
            }
            else if (ends.Length == 2 && TryMonth(ends[0], out int from) && TryMonth(ends[1], out int to))
            {
                set = set.Union(Range(from, to));
            }
        }

        return set;
    }

    /// <summary>Texto compacto, por exemplo "ago–out".</summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        if ((Mask & AllMask) == AllMask)
        {
            return "ano todo";
        }

        // Começa em janeiro, exceto quando um intervalo atravessa a virada do ano (dezembro e janeiro presentes):
        // nesse caso começa logo depois do último mês ausente.
        int start = 1;
        if (Contains(1) && Contains(12))
        {
            for (int m = 12; m >= 1; m--)
            {
                if (!Contains(m))
                {
                    start = m % 12 + 1;
                    break;
                }
            }
        }

        var parts = new List<string>();
        int i = 0;
        while (i < 12)
        {
            int m = (start - 1 + i) % 12 + 1;
            if (!Contains(m))
            {
                i++;
                continue;
            }

            int first = m;
            int last = m;
            while (i + 1 < 12 && Contains((start - 1 + i + 1) % 12 + 1))
            {
                i++;
                last = (start - 1 + i) % 12 + 1;
            }

            parts.Add(first == last ? Abbreviations[first - 1] : Abbreviations[first - 1] + "–" + Abbreviations[last - 1]);
            i++;
        }

        return string.Join(" e ", parts);
    }

    private static string Normalize(string? text)
    {
        var sb = new StringBuilder();
        foreach (char c in (text ?? string.Empty).Trim().ToLowerInvariant())
        {
            sb.Append(c switch
            {
                '–' or '—' or '−' => '-',
                'ç' => 'c',
                'á' or 'â' or 'ã' => 'a',
                'é' or 'ê' => 'e',
                'í' => 'i',
                'ó' or 'ô' or 'õ' => 'o',
                'ú' => 'u',
                _ => c,
            });
        }

        string result = sb.ToString().Replace(" a ", "-", StringComparison.Ordinal).Replace(" ate ", "-", StringComparison.Ordinal);
        return result;
    }

    private static bool TryMonth(string token, out int month)
    {
        month = 0;
        string t = token.Trim().TrimEnd('.');
        if (t.Length < 3)
        {
            return false;
        }

        string key = t[..3];
        for (int i = 0; i < 12; i++)
        {
            if (Abbreviations[i] == key)
            {
                month = i + 1;
                return true;
            }
        }

        return false;
    }
}
