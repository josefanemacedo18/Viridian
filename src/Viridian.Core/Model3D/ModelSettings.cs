using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D;

/// <summary>Como a maquete gera a vegetação.</summary>
public enum MaqueteEngine
{
    /// <summary>Motor botânico (1.9): perfis por arquitetura, qualidade, estação, flores e frutos.</summary>
    Botanico = 0,

    /// <summary>Gerador simples das versões anteriores (volumes por categoria e forma da copa).</summary>
    Legado = 1,
}

/// <summary>Configurações da maquete 3D.</summary>
public sealed class ModelSettings
{
    public const int HardLimit = 20000;

    /// <summary>Máximo de exemplares por maquete (acima disso, a vista 3D fica pesada).</summary>
    public int MaxPlants { get; set; } = 4000;

    /// <summary>Assenta as plantas e os maciços sobre o terreno (toposólido ou superfície topográfica).</summary>
    public bool FollowTerrain { get; set; } = true;

    /// <summary>Distância máxima, para cima ou para baixo, em que o terreno é procurado a partir do nível (m).</summary>
    public double TerrainSearchM { get; set; } = 30.0;

    public string TrunkColor { get; set; } = "#6B4F3A";

    /// <summary>Mostra as manchas de flor nas espécies de floração vistosa.</summary>
    public bool ShowFlowers { get; set; } = true;

    // ---- Escolhas lembradas da janela da maquete (motor botânico) ----------------------------------------------

    public MaqueteEngine Engine { get; set; } = MaqueteEngine.Botanico;

    public QualityLevel Quality { get; set; } = QualityLevel.Intermediaria;

    /// <summary>Folhagem, flores e frutos conforme o mês de referência.</summary>
    public bool UseSeasonality { get; set; }

    /// <summary>Mês de referência (1 a 12) quando a sazonalidade está ligada.</summary>
    public int ReferenceMonth { get; set; } = 1;

    public bool ShowFruits { get; set; }

    /// <summary>Variação natural entre exemplares da mesma espécie.</summary>
    public bool Variation { get; set; } = true;

    /// <summary>Arestas visíveis na vista da maquete (desligadas, as copas ficam mais naturais).</summary>
    public bool ShowEdges { get; set; }

    /// <summary>
    /// Vegetação ilustrada como RPC: as espécies do catálogo e os maciços aparecem nas vistas 3D, cortes e elevações como imagens
    /// sempre de frente para a câmera (a planta baixa continua com o símbolo).
    /// </summary>
    public bool IllustratedFamilies { get; set; } = true;

    /// <summary>Acima deste total estimado de triângulos, a maquete pede confirmação antes de gerar.</summary>
    public int WarnTriangles { get; set; } = 3_000_000;

    public void Normalize()
    {
        if (!Enum.IsDefined(Engine))
        {
            Engine = MaqueteEngine.Botanico;
        }

        if (!Enum.IsDefined(Quality))
        {
            Quality = QualityLevel.Intermediaria;
        }

        ReferenceMonth = ReferenceMonth is >= 1 and <= 12 ? ReferenceMonth : 1;
        WarnTriangles = WarnTriangles <= 0 ? 3_000_000 : Math.Clamp(WarnTriangles, 100_000, 50_000_000);
        MaxPlants = Math.Clamp(MaxPlants <= 0 ? 4000 : MaxPlants, 1, HardLimit);
        TerrainSearchM = double.IsFinite(TerrainSearchM) && TerrainSearchM > 0 ? Math.Clamp(TerrainSearchM, 0.5, 200) : 30.0;
        if (!Rgb.TryParse(TrunkColor, out _))
        {
            TrunkColor = "#6B4F3A";
        }
    }
}

/// <summary>O que a maquete atual mostra, gravado na vista 3D para a atualização refazer o mesmo recorte.</summary>
public sealed class MaqueteState
{
    /// <summary>2: motor botânico (qualidade, mês, flores, frutos, variação). 1: maquetes das versões até 1.8.</summary>
    public const int CurrentSchema = 2;

    public int SchemaVersion { get; set; } = CurrentSchema;

    public GrowthStage Stage { get; set; } = GrowthStage.Maturidade;

    public MaqueteEngine Engine { get; set; } = MaqueteEngine.Botanico;

    public QualityLevel Quality { get; set; } = QualityLevel.Intermediaria;

    /// <summary>Mês de referência (1 a 12); zero = sem sazonalidade.</summary>
    public int Month { get; set; }

    public bool Flowers { get; set; } = true;

    public bool Fruits { get; set; }

    public bool Variation { get; set; } = true;

    /// <summary>
    /// Ajusta um estado lido de uma vista: maquetes gravadas antes do motor botânico continuam no gerador antigo
    /// (atualizar não muda a aparência sem o usuário pedir); valores fora da faixa voltam ao padrão.
    /// </summary>
    public MaqueteState Upgrade(bool legacyFlowers)
    {
        if (SchemaVersion < 2)
        {
            Engine = MaqueteEngine.Legado;
            Flowers = legacyFlowers;
            Quality = QualityLevel.Intermediaria;
            Month = 0;
            Fruits = false;
            Variation = true;
            SchemaVersion = CurrentSchema;
        }

        if (!Enum.IsDefined(Engine))
        {
            Engine = MaqueteEngine.Botanico;
        }

        if (!Enum.IsDefined(Quality))
        {
            Quality = QualityLevel.Intermediaria;
        }

        if (!Enum.IsDefined(Stage))
        {
            Stage = GrowthStage.Maturidade;
        }

        Month = Month is >= 1 and <= 12 ? Month : 0;
        Region ??= new();
        LevelName ??= string.Empty;
        return this;
    }

    /// <summary>Descrição curta para o resumo e a vista.</summary>
    public string Describe()
    {
        if (Engine == MaqueteEngine.Legado)
        {
            return "gerador simples (versões anteriores)";
        }

        string season = Month is >= 1 and <= 12 ? "mês de " + Model.MonthSet.Abbreviations[Month - 1] : "sem sazonalidade";
        return $"motor botânico · {ProfileLabels.Of(Quality).ToLowerInvariant()} · {season}" + (Fruits ? " · com frutos" : string.Empty) + (Flowers ? string.Empty : " · sem flores");
    }

    /// <summary>Contorno da região (anéis de pares X, Y em metros); vazio quando a maquete é do projeto inteiro.</summary>
    public List<List<double[]>> Region { get; set; } = new();

    /// <summary>Nível da planta usada para escolher a região (vazio no projeto inteiro).</summary>
    public string LevelName { get; set; } = string.Empty;

    public bool IsWholeProject => Region.Count == 0;

    public Geometry.Polygon2? RegionPolygon() =>
        Region.Count == 0 ? null : new Geometry.Polygon2(Region.Select(r => r.Where(p => p is { Length: >= 2 }).Select(p => new Geometry.Point2(p[0], p[1]))));

    public static List<List<double[]>> FromPolygon(Geometry.Polygon2? polygon) =>
        polygon is null ? new() : polygon.Rings.Select(r => r.Select(p => new[] { p.X, p.Y }).ToList()).ToList();
}
