using Viridian.Core.Colors;
using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>Harmonias de cor oferecidas no ateliê.</summary>
public enum Harmony
{
    /// <summary>Uma só família de cor, variando a intensidade (ex.: jardim todo em roxos).</summary>
    Monocromatica = 0,

    /// <summary>Cores vizinhas no círculo cromático (ex.: amarelo, laranja e vermelho).</summary>
    Analoga = 1,

    /// <summary>Cor e oposta (ex.: roxo e amarelo).</summary>
    Complementar = 2,

    /// <summary>Três cores equidistantes (ex.: vermelho, amarelo e azul).</summary>
    Triade = 3,
}

/// <summary>Paleta: matizes-alvo (graus no círculo cromático), tolerância e preferências.</summary>
public sealed class Palette
{
    public string Name { get; set; } = string.Empty;

    public List<double> Hues { get; set; } = new();

    /// <summary>Distância máxima de matiz, em graus, para uma cor contar como da paleta.</summary>
    public double ToleranceDeg { get; set; } = 25;

    /// <summary>Folhagens prateadas, acinzentadas ou muito escuras combinam com qualquer paleta (neutras).</summary>
    public bool AcceptNeutralFoliage { get; set; } = true;

    /// <summary>Meses em que a paleta deve estar florida (vazio = qualquer época).</summary>
    public MonthSet Months { get; set; } = MonthSet.None;

    public static Palette FromHarmony(Harmony harmony, double baseHue, string name = "")
    {
        double h = Norm(baseHue);
        List<double> hues = harmony switch
        {
            Harmony.Analoga => new() { Norm(h - 30), h, Norm(h + 30) },
            Harmony.Complementar => new() { h, Norm(h + 180) },
            Harmony.Triade => new() { h, Norm(h + 120), Norm(h + 240) },
            _ => new() { h },
        };
        return new Palette { Name = name, Hues = hues, ToleranceDeg = harmony == Harmony.Monocromatica ? 22 : 25 };
    }

    /// <summary>Paletas prontas de uso corrente no paisagismo.</summary>
    public static IReadOnlyList<Palette> Presets() => new[]
    {
        new Palette { Name = "Roxos e prateados (jardim de inverno)", Hues = new() { 275, 300 } },
        new Palette { Name = "Quente tropical: amarelo, laranja e vermelho", Hues = new() { 50, 25, 5 } },
        new Palette { Name = "Branco e verde (jardim noturno)", Hues = new() { }, AcceptNeutralFoliage = true },
        new Palette { Name = "Rosa e lilás (romântico)", Hues = new() { 330, 300 } },
        new Palette { Name = "Azul e roxo (frescor)", Hues = new() { 230, 270 } },
        new Palette { Name = "Complementar: roxo e amarelo", Hues = new() { 280, 52 } },
        new Palette { Name = "Vermelho e verde-escuro (contraste)", Hues = new() { 0, 130 } },
    };

    internal static double Norm(double deg) => ((deg % 360) + 360) % 360;
}

/// <summary>Uma espécie avaliada para a paleta.</summary>
public sealed record PaletteMatch(Species Species, double Score, bool FlowersInMonths, string Reason);

/// <summary>Ateliê de paletas: classifica as espécies pela afinidade de cor com a paleta escolhida.</summary>
public static class PaletteStudio
{
    /// <summary>Nota de 0 a 1. Floração pesa mais que folhagem; flores brancas combinam com paletas "branco e verde".</summary>
    public static PaletteMatch Evaluate(Species species, Palette palette, ColorSettings colors)
    {
        Rgb foliage = VegetationPalette.Foliage(species, colors);
        Rgb? flower = Rgb.TryParse(species.FlowerColor, out Rgb f) ? f : null;
        MonthSet flowering = MonthSet.Parse(species.Flowering);
        bool inMonths = palette.Months.IsEmpty || (!flowering.IsEmpty && (flowering.Mask & palette.Months.Mask) != 0);

        double foliageScore = ColorScore(foliage, palette, isFlower: false);
        double flowerScore = flower is Rgb fl ? ColorScore(fl, palette, isFlower: true) : 0;
        double score = flower is null ? foliageScore * 0.8 : Math.Max(flowerScore * 0.75 + foliageScore * 0.25, foliageScore * 0.8);
        if (!palette.Months.IsEmpty && !inMonths)
        {
            score *= 0.35; // não floresce na época pedida
        }

        string reason = flower is Rgb fc && flowerScore >= 0.5
            ? $"floração {Describe(fc)}" + (flowering.IsEmpty ? string.Empty : $" ({flowering})")
            : foliageScore >= 0.5 ? $"folhagem {Describe(foliage)}" : "fora da paleta";
        return new PaletteMatch(species, Math.Round(score, 3), inMonths, reason);
    }

    /// <summary>Espécies em ordem de afinidade (somente as com nota mínima).</summary>
    public static IReadOnlyList<PaletteMatch> Rank(IEnumerable<Species> species, Palette palette, ColorSettings colors, double minimumScore = 0.45) =>
        species.Select(s => Evaluate(s, palette, colors))
            .Where(m => m.Score >= minimumScore)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Species.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static double ColorScore(Rgb color, Palette palette, bool isFlower)
    {
        (double h, double s, double l) = color.ToHsl();
        bool neutral = s < 0.15 || l > 0.9 || l < 0.12;
        if (neutral)
        {
            // Brancos, cinzas e prateados: combinam com todas as paletas; brancos são o tema da paleta sem matizes.
            if (palette.Hues.Count == 0)
            {
                return l > 0.8 ? 1.0 : 0.7;
            }

            return palette.AcceptNeutralFoliage ? (isFlower ? 0.55 : 0.6) : 0.2;
        }

        if (palette.Hues.Count == 0)
        {
            return isFlower ? 0.1 : 0.5; // paleta "branco e verde": folhagem verde ok, flores coloridas não
        }

        double best = palette.Hues.Min(target => Distance(h, target));
        double tolerance = Math.Max(5, palette.ToleranceDeg);
        return best <= tolerance ? 1.0 - 0.3 * (best / tolerance) : Math.Max(0, 0.7 - (best - tolerance) / 60.0);
    }

    private static double Distance(double a, double b)
    {
        double d = Math.Abs(Palette.Norm(a) - Palette.Norm(b));
        return Math.Min(d, 360 - d);
    }

    /// <summary>Nome aproximado de uma cor, para explicar a escolha.</summary>
    public static string Describe(Rgb color)
    {
        (double h, double s, double l) = color.ToHsl();
        if (s < 0.15 || l > 0.9)
        {
            return l > 0.8 ? "branca" : l < 0.25 ? "escura" : "prateada";
        }

        return h switch
        {
            < 15 or >= 345 => "vermelha",
            < 40 => "laranja",
            < 65 => "amarela",
            < 160 => "verde",
            < 200 => "verde-azulada",
            < 255 => "azul",
            < 290 => "roxa",
            < 330 => "lilás",
            _ => "rosa",
        };
    }
}
