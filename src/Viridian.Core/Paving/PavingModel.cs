using Viridian.Core.Colors;

namespace Viridian.Core.Paving;

/// <summary>Família do piso, usada para organizar a biblioteca.</summary>
public enum PavingCategory
{
    Cimenticio = 0,
    Fulget = 1,
    Intertravado = 2,
    Drenante = 3,
    PedraNatural = 4,
    Porcelanato = 5,
    Concreto = 6,
    DeckMadeira = 7,
    DeckWpc = 8,
    Pedrisco = 9,
    Emborrachado = 10,
    GramaConcreto = 11,
    PedraPortuguesa = 12,
    Ceramico = 13,
    Cobertura = 14,
}

/// <summary>Paginação (padrão de assentamento) das peças.</summary>
public enum LayingPattern
{
    Grade = 0,
    AmarracaoMeia = 1,
    AmarracaoTerco = 2,
    EspinhaDePeixe = 3,
    EspinhaDePeixe45 = 4,
    Trama = 5,
    Diagonal = 6,
    Hexagonal = 7,
    Opus = 8,
    Cacos = 9,
    Mosaico = 10,
    Reguas = 11,
    Granular = 12,
    Vazado = 13,
    Seixos = 14,
    Britas = 15,
    Lascas = 16,
}

/// <summary>Função de uma camada abaixo do revestimento.</summary>
public enum PavingLayerKind
{
    Assentamento = 0,
    Argamassa = 1,
    Contrapiso = 2,
    Base = 3,
    SubBase = 4,
    Manta = 5,
    Estrutura = 6,
    Outro = 7,
}

/// <summary>Camada sob o revestimento (de cima para baixo).</summary>
public sealed class PavingLayer
{
    public string Name { get; set; } = string.Empty;

    public PavingLayerKind Kind { get; set; } = PavingLayerKind.Base;

    /// <summary>Espessura em milímetros (zero para mantas).</summary>
    public double ThicknessMm { get; set; }

    /// <summary>Para estruturas descontínuas (barrotes): espaçamento entre eixos, em milímetros.</summary>
    public double SpacingMm { get; set; }

    public PavingLayer Clone() => (PavingLayer)MemberwiseClone();
}

/// <summary>Tipo de piso da biblioteca: revestimento, paginação e camadas.</summary>
public sealed class PavingType
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public PavingCategory Category { get; set; }

    /// <summary>Material e acabamento do revestimento (texto livre: "pedra São Tomé serrada, acabamento natural").</summary>
    public string Material { get; set; } = string.Empty;

    public LayingPattern Pattern { get; set; } = LayingPattern.Grade;

    /// <summary>Comprimento da peça (mm). Em cacos, mosaico e pedrisco é o tamanho médio do fragmento.</summary>
    public double LengthMm { get; set; } = 500;

    /// <summary>Largura da peça (mm).</summary>
    public double WidthMm { get; set; } = 500;

    /// <summary>Espessura do revestimento (mm).</summary>
    public double ThicknessMm { get; set; } = 30;

    /// <summary>Largura da junta (mm).</summary>
    public double JointMm { get; set; } = 5;

    /// <summary>Rotação da paginação (graus).</summary>
    public double AngleDeg { get; set; }

    /// <summary>Cor da superfície (#RRGGBB).</summary>
    public string Color { get; set; } = "#BDB8AE";

    /// <summary>Cor das juntas e do desenho da paginação (#RRGGBB).</summary>
    public string JointColor { get; set; } = "#6E6A62";

    /// <summary>Caimento recomendado (%).</summary>
    public double SlopePercent { get; set; } = 1.0;

    /// <summary>Perda de material (%).</summary>
    public double WastePercent { get; set; } = 10;

    /// <summary>Piso moldado no local (fulget, concreto): a compra é por área/volume, não por peças.</summary>
    public bool CastInPlace { get; set; }

    public List<PavingLayer> Layers { get; set; } = new();

    /// <summary>Uso indicado e observações de execução.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Fontes das medidas de referência.</summary>
    public string References { get; set; } = string.Empty;

    public string PriceItemCode { get; set; } = string.Empty;

    public string ServiceItemCode { get; set; } = string.Empty;

    public bool IsUserDefined { get; set; }

    /// <summary>Densidade aparente solta do revestimento granular (t/m³); zero quando não se aplica.</summary>
    public double DensityTM3 { get; set; }

    /// <summary>Saco de venda em quilos (zero quando não se aplica).</summary>
    public double BagKg { get; set; }

    /// <summary>Saco de venda em litros, para materiais vendidos por volume (zero quando não se aplica).</summary>
    public double BagLiters { get; set; }

    public double TotalThicknessMm => ThicknessMm + Layers.Sum(l => Math.Max(0, l.ThicknessMm));

    public Rgb SurfaceRgb => Rgb.Parse(Color, new Rgb(0xBD, 0xB8, 0xAE));

    public Rgb JointRgb => Rgb.Parse(JointColor, new Rgb(0x6E, 0x6A, 0x62));

    /// <summary>Assinatura do desenho e das camadas: muda quando algo que afeta o Revit muda.</summary>
    public string Signature
    {
        get
        {
            string text = string.Join("|", Name, Pattern, LengthMm, WidthMm, ThicknessMm, JointMm, AngleDeg, Color, JointColor,
                string.Join(";", Layers.Select(l => $"{l.Name}:{l.Kind}:{l.ThicknessMm}:{l.SpacingMm}")));
            return Geometry.DeterministicRandom.StableHash(text).ToString("x8");
        }
    }

    public PavingType Clone()
    {
        var copy = (PavingType)MemberwiseClone();
        copy.Layers = Layers.Select(l => l.Clone()).ToList();
        return copy;
    }

    /// <summary>Problemas que impedem usar o tipo (vazio quando válido).</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Informe o nome do piso.");
        }

        if (!(LengthMm >= 5 && LengthMm <= 20000) || !(WidthMm >= 5 && WidthMm <= 20000))
        {
            problems.Add("As dimensões da peça devem ficar entre 5 mm e 20 m.");
        }

        if (!(ThicknessMm >= 1 && ThicknessMm <= 500))
        {
            problems.Add("A espessura do revestimento deve ficar entre 1 e 500 mm.");
        }

        if (!(JointMm >= 0 && JointMm <= 100))
        {
            problems.Add("A junta deve ficar entre 0 e 100 mm.");
        }

        if (!double.IsFinite(AngleDeg))
        {
            problems.Add("Ângulo inválido.");
        }

        if (!Rgb.TryParse(Color, out _) || !Rgb.TryParse(JointColor, out _))
        {
            problems.Add("Use cores no formato #RRGGBB.");
        }

        if (!(WastePercent >= 0 && WastePercent <= 100) || !(SlopePercent >= 0 && SlopePercent <= 30))
        {
            problems.Add("Perda deve ficar entre 0 e 100% e caimento entre 0 e 30%.");
        }

        foreach (PavingLayer layer in Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Name))
            {
                problems.Add("Toda camada precisa de nome.");
            }

            if (!(layer.ThicknessMm >= 0 && layer.ThicknessMm <= 2000))
            {
                problems.Add($"Espessura inválida na camada \"{layer.Name}\".");
            }

            if (layer.ThicknessMm == 0 && layer.Kind != PavingLayerKind.Manta)
            {
                problems.Add($"Só mantas podem ter espessura zero (camada \"{layer.Name}\").");
            }
        }

        if ((Pattern is LayingPattern.Trama or LayingPattern.EspinhaDePeixe or LayingPattern.EspinhaDePeixe45) && LengthMm <= WidthMm)
        {
            problems.Add("Em espinha de peixe e trama, o comprimento deve ser maior que a largura.");
        }

        return problems.Distinct().ToList();
    }

    public void Normalize()
    {
        Name = (Name ?? string.Empty).Trim();
        Material ??= string.Empty;
        Notes ??= string.Empty;
        References ??= string.Empty;
        PriceItemCode ??= string.Empty;
        ServiceItemCode ??= string.Empty;
        Layers ??= new List<PavingLayer>();
        Layers.RemoveAll(l => l is null);
        foreach (PavingLayer l in Layers)
        {
            l.Name = (l.Name ?? string.Empty).Trim();
            l.ThicknessMm = double.IsFinite(l.ThicknessMm) ? Math.Max(0, l.ThicknessMm) : 0;
            l.SpacingMm = double.IsFinite(l.SpacingMm) ? Math.Max(0, l.SpacingMm) : 0;
        }

        if (!Rgb.TryParse(Color, out _))
        {
            Color = "#BDB8AE";
        }

        if (!Rgb.TryParse(JointColor, out _))
        {
            JointColor = "#6E6A62";
        }

        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        DensityTM3 = double.IsFinite(DensityTM3) ? Math.Clamp(DensityTM3, 0, 5) : 0;
        BagKg = double.IsFinite(BagKg) ? Math.Clamp(BagKg, 0, 2000) : 0;
        BagLiters = double.IsFinite(BagLiters) ? Math.Clamp(BagLiters, 0, 2000) : 0;
    }
}

/// <summary>Nomes de exibição em português.</summary>
public static class PavingLabels
{
    public static string Of(PavingCategory c) => c switch
    {
        PavingCategory.Cimenticio => "Cimentício",
        PavingCategory.Fulget => "Fulget (granito lavado)",
        PavingCategory.Intertravado => "Intertravado",
        PavingCategory.Drenante => "Drenante",
        PavingCategory.PedraNatural => "Pedra natural",
        PavingCategory.Porcelanato => "Porcelanato externo",
        PavingCategory.Concreto => "Concreto moldado",
        PavingCategory.DeckMadeira => "Deck de madeira",
        PavingCategory.DeckWpc => "Deck WPC",
        PavingCategory.Pedrisco => "Pedriscos e seixos",
        PavingCategory.Emborrachado => "Emborrachado",
        PavingCategory.GramaConcreto => "Grama-concreto",
        PavingCategory.PedraPortuguesa => "Pedra portuguesa",
        PavingCategory.Ceramico => "Cerâmico e tijolo",
        PavingCategory.Cobertura => "Pedras e coberturas soltas",
        _ => c.ToString(),
    };

    public static string Of(LayingPattern p) => p switch
    {
        LayingPattern.Grade => "Junta reta (grade)",
        LayingPattern.AmarracaoMeia => "Amarração 1/2",
        LayingPattern.AmarracaoTerco => "Amarração 1/3",
        LayingPattern.EspinhaDePeixe => "Espinha de peixe 90°",
        LayingPattern.EspinhaDePeixe45 => "Espinha de peixe 45°",
        LayingPattern.Trama => "Trama (cesto)",
        LayingPattern.Diagonal => "Diagonal 45°",
        LayingPattern.Hexagonal => "Hexagonal",
        LayingPattern.Opus => "Opus (quatro tamanhos)",
        LayingPattern.Cacos => "Cacos (irregular)",
        LayingPattern.Mosaico => "Mosaico (pedra portuguesa)",
        LayingPattern.Reguas => "Réguas (deck)",
        LayingPattern.Granular => "Granular (pedrisco, seixo)",
        LayingPattern.Vazado => "Vazado (grama-concreto)",
        LayingPattern.Seixos => "Seixos (grãos arredondados)",
        LayingPattern.Britas => "Britas (grãos angulosos)",
        LayingPattern.Lascas => "Lascas (casca, cavaco)",
        _ => p.ToString(),
    };

    public static string Of(PavingLayerKind k) => k switch
    {
        PavingLayerKind.Assentamento => "Assentamento",
        PavingLayerKind.Argamassa => "Argamassa",
        PavingLayerKind.Contrapiso => "Contrapiso",
        PavingLayerKind.Base => "Base",
        PavingLayerKind.SubBase => "Sub-base",
        PavingLayerKind.Manta => "Manta",
        PavingLayerKind.Estrutura => "Estrutura (barrotes)",
        _ => "Outra",
    };

    /// <summary>Cor de desenho da camada no detalhe e no material do Revit.</summary>
    public static Rgb LayerColor(PavingLayerKind k) => k switch
    {
        PavingLayerKind.Assentamento => new Rgb(0xE3, 0xD6, 0xB0),
        PavingLayerKind.Argamassa => new Rgb(0xC9, 0xC4, 0xBA),
        PavingLayerKind.Contrapiso => new Rgb(0xB1, 0xB1, 0xAD),
        PavingLayerKind.Base => new Rgb(0xA8, 0x9D, 0x88),
        PavingLayerKind.SubBase => new Rgb(0xC2, 0xB2, 0x92),
        PavingLayerKind.Manta => new Rgb(0x55, 0x6B, 0x8A),
        PavingLayerKind.Estrutura => new Rgb(0x8B, 0x6B, 0x4A),
        _ => new Rgb(0xC8, 0xC8, 0xC8),
    };

    /// <summary>Paginações de peças de tamanho definido (contagem de peças faz sentido).</summary>
    public static bool IsModular(PavingType t) =>
        !t.CastInPlace && !IsGranular(t.Pattern) && t.Pattern is not (LayingPattern.Cacos or LayingPattern.Mosaico);

    /// <summary>Materiais soltos (pedrisco, seixos, britas, lascas): compra por volume, sem peças.</summary>
    public static bool IsGranular(LayingPattern p) => p is LayingPattern.Granular or LayingPattern.Seixos or LayingPattern.Britas or LayingPattern.Lascas;
}
