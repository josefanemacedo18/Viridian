namespace Viridian.Core.Model;

/// <summary>Categoria de vegetação. Define o prefixo de codificação e a biblioteca de símbolos.</summary>
public enum PlantCategory
{
    Arvore = 0,
    Palmeira = 1,
    Arbusto = 2,
    Herbacea = 3,
    Forracao = 4,
    Gramado = 5,
    Trepadeira = 6,
    Aquatica = 7,
}

public enum PlantOrigin
{
    NaoInformada = 0,
    Nativa = 1,
    Exotica = 2,
    ExoticaInvasora = 3,
}

/// <summary>Forma da copa vista em planta (usada principalmente por árvores).</summary>
public enum CrownForm
{
    Arredondada = 0,
    Espalhada = 1,
    Colunar = 2,
    Piramidal = 3,
    Irregular = 4,
}

/// <summary>Textura visual da folhagem (tamanho e recorte das folhas vistos a distância).</summary>
public enum Texture
{
    NaoInformada = 0,

    /// <summary>Folhas miúdas, finas ou recortadas (gramíneas, coníferas, folhas compostas miúdas).</summary>
    Fina = 1,

    Media = 2,

    /// <summary>Folhas grandes e marcantes (palmeiras, bromélias, filodendros, bananeiras).</summary>
    Grossa = 3,
}

/// <summary>Escala visual pelo porte adulto.</summary>
public enum VisualScale
{
    /// <summary>Até cerca de 1 m de altura e de copa: forrações, herbáceas baixas.</summary>
    Miuda = 0,

    Media = 1,

    /// <summary>A partir de 12 m de altura ou 10 m de copa: árvores de grande porte.</summary>
    Monumental = 2,
}

/// <summary>Tolerância ao solo encharcado por algumas horas ou dias (jardins de chuva, áreas alagáveis).</summary>
public enum WaterloggingTolerance
{
    NaoInformada = 0,

    /// <summary>Não tolera encharcamento: bordas e taludes altos do jardim de chuva.</summary>
    Baixa = 1,

    /// <summary>Tolera encharcamento curto: taludes.</summary>
    Media = 2,

    /// <summary>Tolera alagamento frequente: fundo do jardim de chuva.</summary>
    Alta = 3,
}

public enum GrowthRate
{
    NaoInformado = 0,
    Lento = 1,
    Medio = 2,
    Rapido = 3,
}

public enum WaterNeed
{
    NaoInformada = 0,
    Baixa = 1,
    Media = 2,
    Alta = 3,
}

public enum LightNeed
{
    NaoInformada = 0,
    SolPleno = 1,
    MeiaSombra = 2,
    Sombra = 3,
}

/// <summary>Comportamento da folhagem ao longo do ano (importa para a sombra no inverno).</summary>
public enum LeafHabit
{
    NaoInformada = 0,

    /// <summary>Mantém as folhas o ano todo.</summary>
    Perenifolia = 1,

    /// <summary>Perde parte das folhas na estação seca ou fria.</summary>
    Semidecidua = 2,

    /// <summary>Perde todas (ou quase todas) as folhas por alguns meses.</summary>
    Decidua = 3,
}

/// <summary>Biomas brasileiros de ocorrência natural (combináveis).</summary>
[Flags]
public enum Biome
{
    Nenhum = 0,
    Amazonia = 1,
    Caatinga = 2,
    Cerrado = 4,
    MataAtlantica = 8,
    Pampa = 16,
    Pantanal = 32,
}

/// <summary>Situação do exemplar no projeto.</summary>
public enum PlantStatus
{
    Novo = 0,
    Existente = 1,
    Removido = 2,
    Transplantado = 3,
}

/// <summary>Textos de apresentação (pt-BR) e dados auxiliares das enumerações.</summary>
public static class Labels
{
    public static readonly IReadOnlyList<PlantCategory> Categories = Enum.GetValues<PlantCategory>();

    public static string Prefix(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => "AR",
        PlantCategory.Palmeira => "PA",
        PlantCategory.Arbusto => "AB",
        PlantCategory.Herbacea => "HE",
        PlantCategory.Forracao => "FO",
        PlantCategory.Gramado => "GR",
        PlantCategory.Trepadeira => "TR",
        PlantCategory.Aquatica => "AQ",
        _ => "VG",
    };

    public static bool TryCategoryFromPrefix(string? prefix, out PlantCategory category)
    {
        foreach (PlantCategory c in Categories)
        {
            if (string.Equals(Prefix(c), prefix, StringComparison.OrdinalIgnoreCase))
            {
                category = c;
                return true;
            }
        }

        category = PlantCategory.Arvore;
        return false;
    }

    public static string Of(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => "Árvore",
        PlantCategory.Palmeira => "Palmeira",
        PlantCategory.Arbusto => "Arbusto",
        PlantCategory.Herbacea => "Herbácea",
        PlantCategory.Forracao => "Forração",
        PlantCategory.Gramado => "Gramado",
        PlantCategory.Trepadeira => "Trepadeira",
        PlantCategory.Aquatica => "Aquática",
        _ => category.ToString(),
    };

    public static string Plural(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => "Árvores",
        PlantCategory.Palmeira => "Palmeiras",
        PlantCategory.Arbusto => "Arbustos",
        PlantCategory.Herbacea => "Herbáceas",
        PlantCategory.Forracao => "Forrações",
        PlantCategory.Gramado => "Gramados",
        PlantCategory.Trepadeira => "Trepadeiras",
        PlantCategory.Aquatica => "Aquáticas",
        _ => category.ToString(),
    };

    /// <summary>Nome de pasta sem acentos, usado na estrutura da biblioteca em disco.</summary>
    public static string Folder(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => "Arvores",
        PlantCategory.Palmeira => "Palmeiras",
        PlantCategory.Arbusto => "Arbustos",
        PlantCategory.Herbacea => "Herbaceas",
        PlantCategory.Forracao => "Forracoes",
        PlantCategory.Gramado => "Gramados",
        PlantCategory.Trepadeira => "Trepadeiras",
        PlantCategory.Aquatica => "Aquaticas",
        _ => "Outros",
    };

    /// <summary>Categorias normalmente representadas como maciço (área) e não como exemplares isolados.</summary>
    public static bool IsUsuallyMassed(PlantCategory category) =>
        category is PlantCategory.Forracao or PlantCategory.Gramado;

    public static string Of(PlantOrigin origin) => origin switch
    {
        PlantOrigin.Nativa => "Nativa",
        PlantOrigin.Exotica => "Exótica",
        PlantOrigin.ExoticaInvasora => "Exótica invasora",
        _ => "Não informada",
    };

    public static string Of(Texture texture) => texture switch
    {
        Texture.Fina => "Fina",
        Texture.Media => "Média",
        Texture.Grossa => "Grossa",
        _ => "Não informada",
    };

    public static string Of(VisualScale scale) => scale switch
    {
        VisualScale.Miuda => "Miúda",
        VisualScale.Monumental => "Monumental",
        _ => "Média",
    };

    public static string Of(CrownForm form) => form switch
    {
        CrownForm.Arredondada => "Arredondada",
        CrownForm.Espalhada => "Espalhada",
        CrownForm.Colunar => "Colunar",
        CrownForm.Piramidal => "Piramidal",
        CrownForm.Irregular => "Irregular",
        _ => form.ToString(),
    };

    public static string Of(GrowthRate rate) => rate switch
    {
        GrowthRate.Lento => "Lento",
        GrowthRate.Medio => "Médio",
        GrowthRate.Rapido => "Rápido",
        _ => "Não informado",
    };

    public static string Of(LeafHabit h) => h switch
    {
        LeafHabit.Perenifolia => "Perenifólia (folhas o ano todo)",
        LeafHabit.Semidecidua => "Semidecídua (perde parte das folhas)",
        LeafHabit.Decidua => "Decídua (fica sem folhas)",
        _ => "Não informada",
    };

    public static string Of(WaterloggingTolerance t) => t switch
    {
        WaterloggingTolerance.Baixa => "Baixa",
        WaterloggingTolerance.Media => "Média",
        WaterloggingTolerance.Alta => "Alta",
        _ => "Não informada",
    };

    public static string Of(WaterNeed need) => need switch
    {
        WaterNeed.Baixa => "Baixa",
        WaterNeed.Media => "Média",
        WaterNeed.Alta => "Alta",
        _ => "Não informada",
    };

    public static string Of(LightNeed need) => need switch
    {
        LightNeed.SolPleno => "Sol pleno",
        LightNeed.MeiaSombra => "Meia-sombra",
        LightNeed.Sombra => "Sombra",
        _ => "Não informada",
    };

    public static readonly IReadOnlyList<Biome> Biomes = new[]
    {
        Biome.Amazonia, Biome.Caatinga, Biome.Cerrado, Biome.MataAtlantica, Biome.Pampa, Biome.Pantanal,
    };

    public static string Of(Biome biome) => biome switch
    {
        Biome.Amazonia => "Amazônia",
        Biome.Caatinga => "Caatinga",
        Biome.Cerrado => "Cerrado",
        Biome.MataAtlantica => "Mata Atlântica",
        Biome.Pampa => "Pampa",
        Biome.Pantanal => "Pantanal",
        Biome.Nenhum => "—",
        _ => string.Join(", ", Biomes.Where(b => biome.HasFlag(b)).Select(Of)),
    };

    public static string Of(PlantStatus status) => status switch
    {
        PlantStatus.Novo => "Novo",
        PlantStatus.Existente => "Existente",
        PlantStatus.Removido => "Removido",
        PlantStatus.Transplantado => "Transplantado",
        _ => status.ToString(),
    };

    /// <summary>Interpreta o texto gravado no parâmetro de status; valores desconhecidos viram "Novo".</summary>
    public static PlantStatus ParseStatus(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        foreach (PlantStatus status in Enum.GetValues<PlantStatus>())
        {
            if (string.Equals(Of(status), value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return PlantStatus.Novo;
    }
}
