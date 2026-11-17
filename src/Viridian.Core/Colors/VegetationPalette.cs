using System.Globalization;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Colors;

/// <summary>Cor RGB de 8 bits por canal.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Hex;

    public static bool TryParse(string? text, out Rgb color)
    {
        color = Black;
        string value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length != 6
            || !byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new Rgb(r, g, b);
        return true;
    }

    public static Rgb Parse(string? text, Rgb fallback) => TryParse(text, out Rgb color) ? color : fallback;

    /// <summary>Mistura linear: t = 0 devolve esta cor, t = 1 devolve <paramref name="other"/>.</summary>
    public Rgb Mix(Rgb other, double t)
    {
        t = Math.Clamp(double.IsFinite(t) ? t : 0, 0, 1);
        return new Rgb(Lerp(R, other.R, t), Lerp(G, other.G, t), Lerp(B, other.B, t));
    }

    /// <summary>Clareia em direção ao branco (0 a 1).</summary>
    public Rgb Tint(double amount) => Mix(White, amount);

    /// <summary>Escurece em direção ao preto (0 a 1).</summary>
    public Rgb Shade(double amount) => Mix(Black, amount);

    /// <summary>Luminância relativa (WCAG 2.1).</summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte c)
            {
                double v = c / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    /// <summary>Razão de contraste entre duas cores (1 a 21, WCAG 2.1).</summary>
    public double ContrastWith(Rgb other)
    {
        double a = Luminance + 0.05;
        double b = other.Luminance + 0.05;
        return a > b ? a / b : b / a;
    }

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-12)
        {
            return (0, 0, l);
        }

        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s < 1e-12)
        {
            byte v = (byte)Math.Round(l * 255);
            return new Rgb(v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new Rgb(Hue(p, q, h + 1 / 3.0), Hue(p, q, h), Hue(p, q, h - 1 / 3.0));
    }

    private static byte Hue(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        double v = t < 1 / 6.0 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6 : p;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
}

/// <summary>Modo de preenchimento das copas nos símbolos.</summary>
public enum CrownFillMode
{
    /// <summary>Preenchimento sólido colorido (mascara o que está por baixo da copa).</summary>
    Solido = 0,

    /// <summary>Retícula colorida fina: colore sem esconder o que está por baixo.</summary>
    Reticulado = 1,

    /// <summary>Somente linhas (representação técnica em preto e branco).</summary>
    Nenhum = 2,
}

/// <summary>Configurações de cor da vegetação.</summary>
public sealed class ColorSettings
{
    public CrownFillMode CrownFill { get; set; } = CrownFillMode.Solido;

    /// <summary>Quanto a cor da copa é clareada (0 = cor pura, 0,8 = quase branco).</summary>
    public double CrownTint { get; set; } = 0.28;

    /// <summary>Quanto o fundo dos maciços é clareado em relação à cor da espécie.</summary>
    public double MassingBackgroundTint { get; set; } = 0.55;

    /// <summary>Representa a floração das espécies que têm cor de flor cadastrada.</summary>
    public bool ShowFlowers { get; set; } = true;

    /// <summary>Cor-base de cada categoria (#RRGGBB).</summary>
    public Dictionary<PlantCategory, string> CategoryColors { get; set; } = VegetationPalette.DefaultCategoryColors();

    public void Normalize()
    {
        if (!Enum.IsDefined(CrownFill))
        {
            CrownFill = CrownFillMode.Solido;
        }

        CrownTint = double.IsFinite(CrownTint) ? Math.Clamp(CrownTint, 0, 0.85) : 0.28;
        MassingBackgroundTint = double.IsFinite(MassingBackgroundTint) ? Math.Clamp(MassingBackgroundTint, 0, 0.95) : 0.55;
        CategoryColors ??= VegetationPalette.DefaultCategoryColors();
        Dictionary<PlantCategory, string> defaults = VegetationPalette.DefaultCategoryColors();
        foreach (PlantCategory category in Labels.Categories)
        {
            if (!CategoryColors.TryGetValue(category, out string? value) || !Rgb.TryParse(value, out Rgb color))
            {
                CategoryColors[category] = defaults[category];
            }
            else
            {
                CategoryColors[category] = color.Hex;
            }
        }
    }
}

/// <summary>
/// Paleta da vegetação: cores coerentes por categoria, com variação suave e estável por espécie,
/// para que o desenho seja legível e bonito sem perder a unidade gráfica da prancha.
/// </summary>
public static class VegetationPalette
{
    public static Dictionary<PlantCategory, string> DefaultCategoryColors() => new()
    {
        [PlantCategory.Arvore] = "#4E8A3E",
        [PlantCategory.Palmeira] = "#6E9E36",
        [PlantCategory.Arbusto] = "#5F9A57",
        [PlantCategory.Herbacea] = "#7DAE4E",
        [PlantCategory.Forracao] = "#93B957",
        [PlantCategory.Gramado] = "#A3C964",
        [PlantCategory.Trepadeira] = "#6A9A47",
        [PlantCategory.Aquatica] = "#3F9A8C",
    };

    /// <summary>Cor da folhagem: a cadastrada na espécie ou a da categoria com variação própria da espécie.</summary>
    public static Rgb Foliage(Species species, ColorSettings settings)
    {
        if (Rgb.TryParse(species.FoliageColor, out Rgb own))
        {
            return own;
        }

        Rgb baseColor = CategoryBase(species.Category, settings);
        return Vary(baseColor, species.EffectiveSymbolSeed);
    }

    public static Rgb CategoryBase(PlantCategory category, ColorSettings settings)
    {
        string? hex = settings.CategoryColors.TryGetValue(category, out string? value) ? value : null;
        return Rgb.Parse(hex, Rgb.Parse(DefaultCategoryColors()[category], new Rgb(0x4E, 0x8A, 0x3E)));
    }

    /// <summary>Variação suave de matiz e luminosidade, sempre igual para a mesma semente.</summary>
    public static Rgb Vary(Rgb color, int seed)
    {
        var rng = new DeterministicRandom(DeterministicRandom.Combine(seed, 31337));
        (double h, double s, double l) = color.ToHsl();
        double hue = h + rng.Range(-9, 9);
        double saturation = Math.Clamp(s + rng.Range(-0.06, 0.06), 0.18, 0.85);
        double lightness = Math.Clamp(l + rng.Range(-0.06, 0.06), 0.22, 0.62);
        return Rgb.FromHsl(hue, saturation, lightness);
    }

    /// <summary>Cor de preenchimento da copa.</summary>
    public static Rgb CrownFill(Species species, ColorSettings settings) => Foliage(species, settings).Tint(settings.CrownTint);

    /// <summary>Cor da floração, quando cadastrada e habilitada.</summary>
    public static Rgb? Flower(Species species, ColorSettings settings) =>
        settings.ShowFlowers && Rgb.TryParse(species.FlowerColor, out Rgb flower) ? flower : null;

    /// <summary>Cor das linhas da hachura de maciço (mais escura que a folhagem).</summary>
    public static Rgb MassingPattern(Species species, ColorSettings settings) => Foliage(species, settings).Shade(0.18);

    /// <summary>Cor do fundo dos maciços (clara).</summary>
    public static Rgb MassingBackground(Species species, ColorSettings settings) =>
        Foliage(species, settings).Tint(settings.MassingBackgroundTint);
}
