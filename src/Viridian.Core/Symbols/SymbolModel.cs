using Viridian.Core.Geometry;
using Viridian.Core.Scale;

namespace Viridian.Core.Symbols;

/// <summary>Estilo gráfico do símbolo 2D em planta.</summary>
public enum SymbolStyle
{
    Simples = 0,
    ArvoreArredondada = 10,
    ArvoreEspalhada = 11,
    ArvoreColunar = 12,
    ArvorePiramidal = 13,
    ArvoreIrregular = 14,
    ArvoreRadial = 15,
    ArvoreFolhagem = 16,
    Palmeira = 20,
    PalmeiraLeque = 21,
    ArbustoNuvem = 30,
    ArbustoPontiagudo = 31,
    Herbacea = 40,
    Forracao = 50,
    Gramado = 60,
    Trepadeira = 70,
    Aquatica = 80,
}

/// <summary>
/// Camada gráfica de cada linha do símbolo. O gerenciador de escalas liga e desliga camadas
/// conforme o perfil de representação da vista, sem alterar a dimensão real do símbolo.
/// </summary>
public enum SymbolTier
{
    /// <summary>Círculo simples, exibido somente no perfil Mínimo.</summary>
    MinimalOutline = 0,

    /// <summary>Marca do centro (tronco, estipe ou eixo). Sempre visível.</summary>
    Center = 1,

    /// <summary>Contorno da copa: representa a dimensão da vegetação na implantação.</summary>
    Outline = 2,

    /// <summary>Estrutura essencial (visível a partir do perfil Simplificado).</summary>
    Essential = 3,

    /// <summary>Ramificações principais (a partir do perfil Intermediário).</summary>
    Principal = 4,

    /// <summary>Ramificações secundárias e detalhes internos (somente no perfil Detalhado).</summary>
    Detail = 5,
}

/// <summary>Elemento gráfico de um símbolo: segmento de reta ou arco por três pontos.</summary>
public abstract record SymbolPrimitive(SymbolTier Tier);

public sealed record SymbolLine(SymbolTier Tier, Point2 A, Point2 B) : SymbolPrimitive(Tier);

public sealed record SymbolArc(SymbolTier Tier, Point2 Start, Point2 Mid, Point2 End) : SymbolPrimitive(Tier);

/// <summary>Símbolo 2D completo, em metros, centrado na origem (ponto de inserção).</summary>
public sealed class PlantSymbol
{
    public PlantSymbol(SymbolStyle style, double diameter, IReadOnlyList<SymbolPrimitive> primitives)
    {
        Style = style;
        Diameter = diameter;
        Primitives = primitives;
    }

    public SymbolStyle Style { get; }

    /// <summary>Diâmetro real da copa, em metros.</summary>
    public double Diameter { get; }

    public IReadOnlyList<SymbolPrimitive> Primitives { get; }

    /// <summary>Primitivas visíveis em determinado perfil de representação.</summary>
    public IEnumerable<SymbolPrimitive> ForProfile(DetailProfile profile) =>
        Primitives.Where(p => TierVisibility.IsVisible(p.Tier, profile));

    public int Count(DetailProfile profile) => ForProfile(profile).Count();
}

/// <summary>Regras de visibilidade das camadas em cada perfil de representação.</summary>
public static class TierVisibility
{
    public static readonly IReadOnlyList<SymbolTier> AllTiers = Enum.GetValues<SymbolTier>();

    public static bool IsVisible(SymbolTier tier, DetailProfile profile) => tier switch
    {
        SymbolTier.MinimalOutline => profile == DetailProfile.Minimo,
        SymbolTier.Center => true,
        SymbolTier.Outline => profile != DetailProfile.Minimo,
        SymbolTier.Essential => profile != DetailProfile.Minimo,
        SymbolTier.Principal => profile is DetailProfile.Intermediario or DetailProfile.Detalhado,
        SymbolTier.Detail => profile == DetailProfile.Detalhado,
        _ => true,
    };

    /// <summary>Nome da subcategoria do Revit que guarda as linhas da camada.</summary>
    public static string SubcategoryName(SymbolTier tier) => tier switch
    {
        SymbolTier.MinimalOutline => "VIR Contorno mínimo",
        SymbolTier.Center => "VIR Centro",
        SymbolTier.Outline => "VIR Contorno",
        SymbolTier.Essential => "VIR Estrutura essencial",
        SymbolTier.Principal => "VIR Ramificação principal",
        SymbolTier.Detail => "VIR Detalhe",
        _ => "VIR Detalhe",
    };

    public static string DisplayName(SymbolTier tier) => tier switch
    {
        SymbolTier.MinimalOutline => "Contorno mínimo",
        SymbolTier.Center => "Centro",
        SymbolTier.Outline => "Contorno da copa",
        SymbolTier.Essential => "Estrutura essencial",
        SymbolTier.Principal => "Ramificação principal",
        SymbolTier.Detail => "Detalhe interno",
        _ => tier.ToString(),
    };
}
