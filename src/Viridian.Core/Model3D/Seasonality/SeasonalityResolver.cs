using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Seasonality;

/// <summary>De onde veio o dado sazonal.</summary>
public enum SeasonSource
{
    NaoInformado = 0,

    /// <summary>Meses escritos na ficha da espécie.</summary>
    Ficha = 1,

    /// <summary>Estimado pelo gênero: referência geral, a conferir para a região do projeto.</summary>
    Estimado = 2,
}

/// <summary>Meses de um evento sazonal e a origem do dado.</summary>
public sealed record SeasonInfo(MonthSet Months, SeasonSource Source)
{
    public bool Known => Source != SeasonSource.NaoInformado && !Months.IsEmpty;

    public string Text => Source switch
    {
        SeasonSource.NaoInformado => "não informada",
        SeasonSource.Estimado => $"{Months} (estimada pelo gênero)",
        _ => Months.ToString(),
    };
}

/// <summary>Frutos de uma espécie: meses, cor, tipo e tamanho.</summary>
public sealed record FruitTraits(SeasonInfo Season, Rgb Color, FruitType Type, double SizeM);

/// <summary>Aparência da folhagem num mês.</summary>
public enum FoliageState
{
    Cheia = 0,

    /// <summary>Folhagem rala (semidecíduas no período seco).</summary>
    Rala = 1,

    /// <summary>Sem folhas (decíduas no período sem folhas).</summary>
    SemFolhas = 2,
}

/// <summary>
/// Sazonalidade estruturada da espécie: floração (ficha ou gênero), frutificação (ficha ou gênero) e folhagem (hábito
/// foliar e meses sem folhas). Nunca inventa meses: sem dado e sem estimativa conhecida, a origem é "não informado".
/// Os meses valem para o hemisfério sul; no norte são deslocados seis meses.
/// </summary>
public static class SeasonalityResolver
{
    /// <summary>Floração de espécies sem meses na ficha (gênero → meses), referência geral.</summary>
    private static readonly Dictionary<string, string> FloweringByGenus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Neoregelia"] = "out–mar",
        ["Aechmea"] = "nov–mar",
        ["Vriesea"] = "jun–out",
        ["Alcantarea"] = "set–dez",
        ["Billbergia"] = "dez–mar",
    };

    private sealed record FruitSeed(string Months, string Color, FruitType Type, double Size);

    /// <summary>Frutificação por gênero ou espécie (referência geral; cores aproximadas do fruto maduro).</summary>
    private static readonly Dictionary<string, FruitSeed> FruitByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Eugenia uniflora"] = new("out–dez", "#C4261E", FruitType.Baga, 0.025),
        ["Eugenia involucrata"] = new("nov–dez", "#6E1020", FruitType.Baga, 0.025),
        ["Eugenia pyriformis"] = new("set–jan", "#F2A900", FruitType.Baga, 0.03),
        ["Eugenia dysenterica"] = new("set–out", "#E2CC4A", FruitType.Baga, 0.03),
        ["Plinia"] = new("ago–nov", "#2B0F2E", FruitType.Caulinar, 0.025),
        ["Psidium guajava"] = new("dez–mar", "#C9C64A", FruitType.Drupa, 0.07),
        ["Psidium cattleyanum"] = new("jan–mar", "#B3203A", FruitType.Baga, 0.03),
        ["Acca"] = new("mar–mai", "#6E8F3A", FruitType.Drupa, 0.05),
        ["Campomanesia"] = new("nov–dez", "#E8C230", FruitType.Baga, 0.025),
        ["Mangifera"] = new("nov–jan", "#E48A2A", FruitType.Grande, 0.11),
        ["Persea"] = new("mar–jul", "#4E6B2E", FruitType.Grande, 0.11),
        ["Anacardium"] = new("set–dez", "#E04A1E", FruitType.Drupa, 0.07),
        ["Artocarpus"] = new("dez–abr", "#9FAE3C", FruitType.Grande, 0.35),
        ["Citrus"] = new("mai–set", "#F2D034", FruitType.Drupa, 0.07),
        ["Malpighia"] = new("nov–abr", "#C81E1E", FruitType.Baga, 0.02),
        ["Genipa"] = new("nov–jan", "#7A5A3A", FruitType.Drupa, 0.08),
        ["Spondias mombin"] = new("jan–mar", "#E8B820", FruitType.Drupa, 0.035),
        ["Spondias tuberosa"] = new("jan–mar", "#C2C24A", FruitType.Drupa, 0.035),
        ["Tamarindus"] = new("jul–set", "#7A5230", FruitType.Vagem, 0.12),
        ["Hymenaea"] = new("ago–out", "#5A3A22", FruitType.Vagem, 0.14),
        ["Caryocar"] = new("nov–jan", "#7E8E3C", FruitType.Drupa, 0.08),
        ["Hancornia"] = new("out–dez", "#D8A23A", FruitType.Drupa, 0.04),
        ["Syzygium cumini"] = new("jan–mar", "#3A1440", FruitType.Baga, 0.02),
        ["Syzygium malaccense"] = new("dez–mar", "#B5172F", FruitType.Drupa, 0.06),
        ["Moquilea"] = new("dez–mar", "#E2B640", FruitType.Drupa, 0.05),
        ["Schinus"] = new("mai–jul", "#D4344C", FruitType.Baga, 0.006),
        ["Melia"] = new("jun–ago", "#D9B85A", FruitType.Baga, 0.015),
        ["Ligustrum"] = new("jun–ago", "#2C2A48", FruitType.Baga, 0.008),
        ["Ilex"] = new("jan–mar", "#A51E1E", FruitType.Baga, 0.006),
        ["Bixa"] = new("ago–out", "#B32A1E", FruitType.Drupa, 0.04),
        ["Inga"] = new("dez–mar", "#6E8A3A", FruitType.Vagem, 0.3),
        ["Delonix"] = new("jun–out", "#4A2E1E", FruitType.Vagem, 0.45),
        ["Cassia fistula"] = new("mai–set", "#2A1E16", FruitType.Vagem, 0.45),
        ["Couroupita"] = new("jan–dez", "#7A5A3A", FruitType.Caulinar, 0.18),
        ["Lecythis"] = new("ago–out", "#6A4A2A", FruitType.Grande, 0.15),
        ["Bertholletia"] = new("out–dez", "#5A3A22", FruitType.Grande, 0.14),
        ["Nandina"] = new("mai–ago", "#C21E2E", FruitType.Baga, 0.008),
        ["Murraya"] = new("mar–mai", "#C81E1E", FruitType.Baga, 0.012),
        ["Passiflora"] = new("dez–mar", "#E8C230", FruitType.Drupa, 0.08),
        ["Ananas"] = new("nov–mar", "#C2381E", FruitType.Grande, 0.1),
        ["Syagrus"] = new("fev–ago", "#F28C28", FruitType.CachoPalmeira, 0.025),
        ["Butia"] = new("dez–mar", "#F2A51E", FruitType.CachoPalmeira, 0.025),
        ["Euterpe"] = new("ago–dez", "#2A1530", FruitType.CachoPalmeira, 0.015),
        ["Bactris"] = new("dez–mar", "#D8481E", FruitType.CachoPalmeira, 0.04),
        ["Acrocomia"] = new("set–jan", "#C9B84A", FruitType.CachoPalmeira, 0.04),
        ["Mauritia"] = new("dez–jun", "#8A3A1E", FruitType.CachoPalmeira, 0.05),
        ["Cocos"] = new("jan–dez", "#7A8A3A", FruitType.CachoPalmeira, 0.22),
        ["Archontophoenix"] = new("jan–abr", "#C21E1E", FruitType.CachoPalmeira, 0.012),
        ["Adonidia"] = new("dez–fev", "#D21E1E", FruitType.CachoPalmeira, 0.025),
    };

    public static SeasonInfo Flowering(Species species)
    {
        ArgumentNullException.ThrowIfNull(species);
        MonthSet typed = MonthSet.Parse(species.Flowering);
        if (!typed.IsEmpty)
        {
            return new SeasonInfo(typed, SeasonSource.Ficha);
        }

        if (string.IsNullOrWhiteSpace(species.FlowerColor))
        {
            return new SeasonInfo(MonthSet.None, SeasonSource.NaoInformado); // floração pouco vistosa: não representada
        }

        return FloweringByGenus.TryGetValue(Genus(species), out string? months)
            ? new SeasonInfo(MonthSet.Parse(months), SeasonSource.Estimado)
            : new SeasonInfo(MonthSet.None, SeasonSource.NaoInformado);
    }

    /// <summary>Frutos da espécie; nulo quando não há dado nem estimativa (frutos não são representados).</summary>
    public static FruitTraits? Fruit(Species species, Plant3DProfile profile)
    {
        ArgumentNullException.ThrowIfNull(species);
        FruitSeed? seed = Lookup(species);
        MonthSet typed = MonthSet.Parse(species.Fruiting);
        SeasonInfo season = !typed.IsEmpty ? new SeasonInfo(typed, SeasonSource.Ficha)
            : seed is not null ? new SeasonInfo(MonthSet.Parse(seed.Months), SeasonSource.Estimado)
            : new SeasonInfo(MonthSet.None, SeasonSource.NaoInformado);
        if (!season.Known)
        {
            return null;
        }

        Rgb color = Rgb.TryParse(species.FruitColor, out Rgb c) ? c : seed is not null ? Rgb.Parse(seed.Color, new Rgb(0xB0, 0x30, 0x20)) : new Rgb(0xB0, 0x30, 0x20);
        FruitType type = seed?.Type ?? profile.Fruits?.Type ?? FruitType.Drupa;
        double size = seed?.Size ?? profile.Fruits?.SizeM ?? 0.04;
        return new FruitTraits(season, color, type, size);
    }

    /// <summary>Estado da folhagem no mês (1 a 12; zero = sem sazonalidade).</summary>
    public static FoliageState Foliage(Species species, int month, bool southernHemisphere = true)
    {
        ArgumentNullException.ThrowIfNull(species);
        if (month is < 1 or > 12)
        {
            return FoliageState.Cheia;
        }

        MonthSet leafless = species.EffectiveLeaflessMonths;
        if (!leafless.Contains(SouthMonth(month, southernHemisphere)))
        {
            return FoliageState.Cheia;
        }

        return species.EffectiveLeafHabit == LeafHabit.Decidua ? FoliageState.SemFolhas : FoliageState.Rala;
    }

    /// <summary>Verdadeiro quando o evento ocorre no mês (meses do hemisfério sul deslocados no norte).</summary>
    public static bool InMonth(SeasonInfo season, int month, bool southernHemisphere = true) =>
        season.Known && month is >= 1 and <= 12 && season.Months.Contains(SouthMonth(month, southernHemisphere));

    private static int SouthMonth(int month, bool south) => south ? month : (month + 5) % 12 + 1;

    private static FruitSeed? Lookup(Species species)
    {
        string name = string.Join(' ', (species.ScientificName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        return FruitByName.TryGetValue(name, out FruitSeed? s) ? s : FruitByName.TryGetValue(Genus(species), out s) ? s : null;
    }

    private static string Genus(Species species) => (species.ScientificName ?? string.Empty).Split(' ').FirstOrDefault() ?? string.Empty;
}
