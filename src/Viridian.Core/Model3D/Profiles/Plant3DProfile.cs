using Viridian.Core.Colors;

namespace Viridian.Core.Model3D.Profiles;

/// <summary>Arquitetura vegetal: escolhe o gerador de geometria. O perfil ajusta as proporções dentro dela.</summary>
public enum PlantArchitecture
{
    ArvoreArredondada = 0,
    ArvoreEspalhada = 1,
    ArvoreColunar = 2,
    ArvoreConica = 3,
    ArvoreIrregular = 4,
    PalmeiraEstipeUnico = 5,
    PalmeiraTouceira = 6,
    ArbustoArredondado = 7,
    ArbustoEreto = 8,
    Trepadeira = 9,
    Graminea = 10,
    RosetaRigida = 11,
    HerbaceaFolhaLarga = 12,
    Forracao = 13,
    AquaticaEmergente = 14,
    Gramado = 15,
    AquaticaFlutuante = 16,

    /// <summary>Caules com tufos de folhas no topo (dracenas, cordilines, iúcas).</summary>
    TufosEmCaule = 17,
}

/// <summary>Envoltória da copa (limite dos grupos de folhagem, não a geometria final).</summary>
public enum CrownEnvelope
{
    Elipsoide = 0,
    Achatada = 1,
    GuardaChuva = 2,
    Colunar = 3,
    Conica = 4,
    Irregular = 5,
    Pendente = 6,
}

public enum LeafShape
{
    Eliptica = 0,
    Oval = 1,
    Lanceolada = 2,
    Linear = 3,
    Pinada = 4,
    Palmada = 5,
    Suculenta = 6,
    Agulha = 7,
    Cordiforme = 8,
}

public enum InflorescenceType
{
    Cacho = 0,
    Panicula = 1,
    Capitulo = 2,
    Isolada = 3,
    Espiga = 4,
    Umbela = 5,
    Bracteas = 6,
    Plumas = 7,
    AveDoParaiso = 8,
}

public enum FruitType
{
    Baga = 0,
    Drupa = 1,
    Grande = 2,
    Vagem = 3,
    CachoPalmeira = 4,
    Caulinar = 5,
}

/// <summary>Qualidade geométrica escolhida pelo usuário (não é distância de câmera nem escala 2D).</summary>
public enum QualityLevel
{
    Simplificada = 0,
    Intermediaria = 1,
    Detalhada = 2,
}

/// <summary>Copa: envoltória, posição e textura dos agrupamentos de folhagem.</summary>
public sealed class CrownProfile
{
    public CrownEnvelope Envelope { get; set; } = CrownEnvelope.Elipsoide;

    /// <summary>Início da copa, em fração da altura total (fuste livre).</summary>
    public double BaseRatio { get; set; } = 0.35;

    /// <summary>Largura no eixo Y em relação ao X (1 = copa circular em planta).</summary>
    public double WidthAspect { get; set; } = 0.95;

    /// <summary>Irregularidade do contorno (0 = regular, 1 = muito irregular).</summary>
    public double Irregularity { get; set; } = 0.35;

    /// <summary>Densidade da folhagem (0 = muito aberta, 1 = compacta).</summary>
    public double Density { get; set; } = 0.65;

    /// <summary>Tamanho de cada agrupamento de folhagem em relação ao raio da copa.</summary>
    public double ClusterScale { get; set; } = 0.38;
}

/// <summary>Tronco ou caules.</summary>
public sealed class TrunkProfile
{
    /// <summary>Raio na base em relação à altura da planta.</summary>
    public double RadiusRatio { get; set; } = 0.02;

    /// <summary>Raio no topo em relação ao da base.</summary>
    public double Taper { get; set; } = 0.55;

    /// <summary>Alargamento do colo junto ao chão (0 = nenhum).</summary>
    public double Flare { get; set; } = 0.25;

    /// <summary>Quantidade de caules saindo da base.</summary>
    public int Stems { get; set; } = 1;

    /// <summary>Inclinação máxima de cada caule (graus).</summary>
    public double LeanDeg { get; set; } = 4;

    /// <summary>Cor da casca (#RRGGBB); vazio = cor padrão das configurações.</summary>
    public string Color { get; set; } = string.Empty;
}

/// <summary>Galhos primários e secundários.</summary>
public sealed class BranchProfile
{
    public int PrimaryMin { get; set; } = 4;

    public int PrimaryMax { get; set; } = 6;

    /// <summary>Ângulo dos galhos primários em relação à vertical (graus).</summary>
    public double AngleDeg { get; set; } = 45;

    /// <summary>Quanto as pontas descem (0 = retas, 1 = muito pendentes).</summary>
    public double Droop { get; set; } = 0.15;

    /// <summary>Galhos secundários por primário (no nível Detalhado; metade no Intermediário).</summary>
    public int SecondaryPerPrimary { get; set; } = 2;
}

/// <summary>Folhagem: forma e tamanho das folhas e variação de tom.</summary>
public sealed class FoliageProfile
{
    public LeafShape Shape { get; set; } = LeafShape.Eliptica;

    /// <summary>Comprimento típico da folha (m), usado no nível Detalhado.</summary>
    public double LeafLengthM { get; set; } = 0.1;

    /// <summary>Largura da folha em relação ao comprimento.</summary>
    public double WidthRatio { get; set; } = 0.45;

    /// <summary>Variação de tom entre agrupamentos (0 a 0,3).</summary>
    public double ToneVariation { get; set; } = 0.12;
}

/// <summary>Palmeiras e cicas.</summary>
public sealed class PalmProfile
{
    public int FrondsMin { get; set; } = 10;

    public int FrondsMax { get; set; } = 14;

    /// <summary>Folha pinada (penas) ou palmada (leque).</summary>
    public LeafShape Frond { get; set; } = LeafShape.Pinada;

    /// <summary>Comprimento da folha em relação ao raio da copa.</summary>
    public double FrondLengthRatio { get; set; } = 1.05;

    /// <summary>Arqueamento das folhas (0 = retas, 1 = muito curvas).</summary>
    public double Arch { get; set; } = 0.6;

    /// <summary>Raio do estipe em relação à altura.</summary>
    public double StipeRatio { get; set; } = 0.022;

    /// <summary>Palmito verde (crownshaft) em fração da altura (0 = sem).</summary>
    public double Crownshaft { get; set; }

    /// <summary>Estipes por touceira.</summary>
    public int Stems { get; set; } = 1;

    /// <summary>Rigidez das folhas e folíolos (0 = pendentes, 1 = rígidos).</summary>
    public double Rigidity { get; set; } = 0.4;
}

/// <summary>Rosetas, touceiras de folhas e plantas de folhas largas.</summary>
public sealed class RosetteProfile
{
    /// <summary>Cópia rasa (todos os campos são valores), para ajustes por exemplar sem tocar no perfil.</summary>
    public RosetteProfile Copy() => (RosetteProfile)MemberwiseClone();

    public int LeavesMin { get; set; } = 14;

    public int LeavesMax { get; set; } = 20;

    public LeafShape Leaf { get; set; } = LeafShape.Suculenta;

    /// <summary>Largura da folha em relação ao comprimento.</summary>
    public double WidthRatio { get; set; } = 0.22;

    /// <summary>Espessura da folha em relação à largura (folhas suculentas são grossas).</summary>
    public double Thickness { get; set; } = 0.12;

    /// <summary>Inclinação em relação à vertical (graus): 15 = eretas, 75 = abertas.</summary>
    public double InclinationDeg { get; set; } = 45;

    /// <summary>Curvatura das folhas para fora e para baixo (0 a 1).</summary>
    public double Curvature { get; set; } = 0.4;

    /// <summary>Fração do comprimento que é pecíolo (folhas largas com cabo, como a estrelítzia).</summary>
    public double PetioleRatio { get; set; }

    /// <summary>Folhas em leque num plano (dísticas), como a estrelítzia.</summary>
    public bool Fan { get; set; }

    /// <summary>Altura do caule sob a roseta, em fração da altura (tufos em caule).</summary>
    public double StemRatio { get; set; }
}

/// <summary>Flores: tipo de inflorescência e quantidade.</summary>
public sealed class FlowerProfile
{
    public InflorescenceType Type { get; set; } = InflorescenceType.Panicula;

    /// <summary>Tamanho de cada inflorescência (m).</summary>
    public double SizeM { get; set; } = 0.15;

    /// <summary>Flores por inflorescência no nível Detalhado.</summary>
    public int PerCluster { get; set; } = 6;

    /// <summary>Quanto da copa fica florido (0 a 1).</summary>
    public double Abundance { get; set; } = 0.5;

    /// <summary>Flores em hastes acima da folhagem (agapanto, gramíneas, estrelítzia).</summary>
    public bool AboveFoliage { get; set; }
}

/// <summary>Frutos: tipo e quantidade (o mês e a cor vêm da espécie).</summary>
public sealed class FruitProfile
{
    public FruitType Type { get; set; } = FruitType.Drupa;

    public double SizeM { get; set; } = 0.04;

    public int PerCluster { get; set; } = 3;

    public double Abundance { get; set; } = 0.35;
}

/// <summary>Orçamento máximo de triângulos por nível de qualidade.</summary>
public sealed class GeometryBudget
{
    public int Simplificada { get; set; } = 350;

    public int Intermediaria { get; set; } = 1500;

    public int Detalhada { get; set; } = 4000;

    public int For(QualityLevel level) => level switch
    {
        QualityLevel.Simplificada => Simplificada,
        QualityLevel.Intermediaria => Intermediaria,
        _ => Detalhada,
    };
}

/// <summary>
/// Perfil botânico 3D: a receita de geometria de uma arquitetura vegetal. Várias espécies compartilham o mesmo perfil;
/// a espécie informa porte, cores e sazonalidade. Medidas estruturais são proporções, para o perfil servir a qualquer porte.
/// </summary>
public sealed class Plant3DProfile
{
    public const int CurrentSchema = 1;

    public string Id { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = CurrentSchema;

    /// <summary>Versão da receita: muda quando os parâmetros mudam (entra no hash da geometria).</summary>
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public PlantArchitecture Architecture { get; set; }

    public List<string> Tags { get; set; } = new();

    public CrownProfile Crown { get; set; } = new();

    public TrunkProfile? Trunk { get; set; }

    public BranchProfile? Branches { get; set; }

    public FoliageProfile Foliage { get; set; } = new();

    public PalmProfile? Palm { get; set; }

    public RosetteProfile? Rosette { get; set; }

    public FlowerProfile? Flowers { get; set; }

    public FruitProfile? Fruits { get; set; }

    public GeometryBudget Budget { get; set; } = new();

    /// <summary>Criado ou editado pelo escritório: não é sobrescrito por versões novas do plugin.</summary>
    public bool IsUserDefined { get; set; }

    public Plant3DProfile Clone() => Config.ViridianJson.Deserialize<Plant3DProfile>(Config.ViridianJson.Serialize(this))!;

    /// <summary>Texto canônico de todos os parâmetros que afetam a geometria (entra no hash).</summary>
    public string Signature() =>
        Config.ViridianJson.Serialize(new { Id, Version, Architecture, Crown, Trunk, Branches, Foliage, Palm, Rosette, Flowers, Fruits, Budget });

    /// <summary>Corrige valores fora de faixa (NaN, negativos, enums desconhecidos) para valores seguros.</summary>
    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        Name = (Name ?? string.Empty).Trim();
        Description ??= string.Empty;
        Tags ??= new List<string>();
        Crown ??= new CrownProfile();
        Foliage ??= new FoliageProfile();
        Budget ??= new GeometryBudget();
        if (!Enum.IsDefined(Architecture))
        {
            Architecture = PlantArchitecture.ArvoreArredondada;
        }

        Version = Math.Max(1, Version);
        static double C(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

        if (!Enum.IsDefined(Crown.Envelope))
        {
            Crown.Envelope = CrownEnvelope.Elipsoide;
        }

        Crown.BaseRatio = C(Crown.BaseRatio, 0, 0.9, 0.35);
        Crown.WidthAspect = C(Crown.WidthAspect, 0.4, 1.0, 0.95);
        Crown.Irregularity = C(Crown.Irregularity, 0, 1, 0.35);
        Crown.Density = C(Crown.Density, 0.05, 1, 0.65);
        Crown.ClusterScale = C(Crown.ClusterScale, 0.12, 0.7, 0.38);

        if (Trunk is not null)
        {
            Trunk.RadiusRatio = C(Trunk.RadiusRatio, 0.003, 0.08, 0.02);
            Trunk.Taper = C(Trunk.Taper, 0.1, 1, 0.55);
            Trunk.Flare = C(Trunk.Flare, 0, 1, 0.25);
            Trunk.Stems = Math.Clamp(Trunk.Stems, 1, 16);
            Trunk.LeanDeg = C(Trunk.LeanDeg, 0, 25, 4);
            Trunk.Color = Rgb.TryParse(Trunk.Color, out _) ? Trunk.Color : string.Empty;
        }

        if (Branches is not null)
        {
            Branches.PrimaryMin = Math.Clamp(Branches.PrimaryMin, 0, 16);
            Branches.PrimaryMax = Math.Clamp(Branches.PrimaryMax, Branches.PrimaryMin, 16);
            Branches.AngleDeg = C(Branches.AngleDeg, 5, 95, 45);
            Branches.Droop = C(Branches.Droop, 0, 1, 0.15);
            Branches.SecondaryPerPrimary = Math.Clamp(Branches.SecondaryPerPrimary, 0, 5);
        }

        if (!Enum.IsDefined(Foliage.Shape))
        {
            Foliage.Shape = LeafShape.Eliptica;
        }

        Foliage.LeafLengthM = C(Foliage.LeafLengthM, 0.01, 3, 0.1);
        Foliage.WidthRatio = C(Foliage.WidthRatio, 0.03, 1.2, 0.45);
        Foliage.ToneVariation = C(Foliage.ToneVariation, 0, 0.3, 0.12);

        if (Palm is not null)
        {
            Palm.FrondsMin = Math.Clamp(Palm.FrondsMin, 3, 40);
            Palm.FrondsMax = Math.Clamp(Palm.FrondsMax, Palm.FrondsMin, 40);
            Palm.Frond = Palm.Frond is LeafShape.Pinada or LeafShape.Palmada ? Palm.Frond : LeafShape.Pinada;
            Palm.FrondLengthRatio = C(Palm.FrondLengthRatio, 0.4, 1.6, 1.05);
            Palm.Arch = C(Palm.Arch, 0, 1, 0.6);
            Palm.StipeRatio = C(Palm.StipeRatio, 0.004, 0.08, 0.022);
            Palm.Crownshaft = C(Palm.Crownshaft, 0, 0.3, 0);
            Palm.Stems = Math.Clamp(Palm.Stems, 1, 16);
            Palm.Rigidity = C(Palm.Rigidity, 0, 1, 0.4);
        }

        if (Rosette is not null)
        {
            Rosette.LeavesMin = Math.Clamp(Rosette.LeavesMin, 2, 120);
            Rosette.LeavesMax = Math.Clamp(Rosette.LeavesMax, Rosette.LeavesMin, 120);
            if (!Enum.IsDefined(Rosette.Leaf))
            {
                Rosette.Leaf = LeafShape.Suculenta;
            }

            Rosette.WidthRatio = C(Rosette.WidthRatio, 0.02, 1.2, 0.22);
            Rosette.Thickness = C(Rosette.Thickness, 0.01, 0.5, 0.12);
            Rosette.InclinationDeg = C(Rosette.InclinationDeg, 0, 88, 45);
            Rosette.Curvature = C(Rosette.Curvature, 0, 1, 0.4);
            Rosette.PetioleRatio = C(Rosette.PetioleRatio, 0, 0.8, 0);
            Rosette.StemRatio = C(Rosette.StemRatio, 0, 0.9, 0);
        }

        if (Flowers is not null)
        {
            if (!Enum.IsDefined(Flowers.Type))
            {
                Flowers.Type = InflorescenceType.Panicula;
            }

            Flowers.SizeM = C(Flowers.SizeM, 0.01, 1.5, 0.15);
            Flowers.PerCluster = Math.Clamp(Flowers.PerCluster, 1, 40);
            Flowers.Abundance = C(Flowers.Abundance, 0, 1, 0.5);
        }

        if (Fruits is not null)
        {
            if (!Enum.IsDefined(Fruits.Type))
            {
                Fruits.Type = FruitType.Drupa;
            }

            Fruits.SizeM = C(Fruits.SizeM, 0.005, 0.6, 0.04);
            Fruits.PerCluster = Math.Clamp(Fruits.PerCluster, 1, 60);
            Fruits.Abundance = C(Fruits.Abundance, 0, 1, 0.35);
        }

        Budget.Simplificada = Math.Clamp(Budget.Simplificada, 20, 20000);
        Budget.Intermediaria = Math.Clamp(Budget.Intermediaria, Budget.Simplificada, 40000);
        Budget.Detalhada = Math.Clamp(Budget.Detalhada, Budget.Intermediaria, 80000);
    }

    /// <summary>Problemas que impedem usar o perfil (vazio quando válido).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
        {
            problems.Add("O perfil precisa de identificador.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("O perfil precisa de nome.");
        }

        if (SchemaVersion > CurrentSchema)
        {
            problems.Add($"Perfil gravado por uma versão mais nova do Viridian (esquema {SchemaVersion}).");
        }

        bool palm = Architecture is PlantArchitecture.PalmeiraEstipeUnico or PlantArchitecture.PalmeiraTouceira;
        if (palm && Palm is null)
        {
            problems.Add("Perfis de palmeira precisam dos dados de palmeira.");
        }

        bool rosette = Architecture is PlantArchitecture.RosetaRigida or PlantArchitecture.HerbaceaFolhaLarga or PlantArchitecture.TufosEmCaule;
        if (rosette && Rosette is null)
        {
            problems.Add("Perfis de roseta, folha larga ou tufo precisam dos dados de roseta.");
        }

        return problems;
    }
}

/// <summary>Nomes em português.</summary>
public static class ProfileLabels
{
    public static string Of(PlantArchitecture a) => a switch
    {
        PlantArchitecture.ArvoreArredondada => "Árvore de copa arredondada",
        PlantArchitecture.ArvoreEspalhada => "Árvore de copa espalhada",
        PlantArchitecture.ArvoreColunar => "Árvore colunar",
        PlantArchitecture.ArvoreConica => "Árvore cônica",
        PlantArchitecture.ArvoreIrregular => "Árvore irregular",
        PlantArchitecture.PalmeiraEstipeUnico => "Palmeira de estipe único",
        PlantArchitecture.PalmeiraTouceira => "Palmeira de touceira",
        PlantArchitecture.ArbustoArredondado => "Arbusto arredondado",
        PlantArchitecture.ArbustoEreto => "Arbusto ereto",
        PlantArchitecture.Trepadeira => "Trepadeira",
        PlantArchitecture.Graminea => "Gramínea ou touceira",
        PlantArchitecture.RosetaRigida => "Roseta",
        PlantArchitecture.HerbaceaFolhaLarga => "Herbácea de folhas largas",
        PlantArchitecture.Forracao => "Forração",
        PlantArchitecture.AquaticaEmergente => "Aquática emergente",
        PlantArchitecture.Gramado => "Gramado",
        PlantArchitecture.AquaticaFlutuante => "Aquática flutuante",
        PlantArchitecture.TufosEmCaule => "Tufos em caule",
        _ => a.ToString(),
    };

    public static string Of(QualityLevel q) => q switch
    {
        QualityLevel.Simplificada => "Simplificada",
        QualityLevel.Intermediaria => "Intermediária",
        _ => "Detalhada",
    };
}
