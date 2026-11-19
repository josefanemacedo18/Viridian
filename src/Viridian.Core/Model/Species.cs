using Viridian.Core.Symbols;

namespace Viridian.Core.Model;

/// <summary>Registro de uma espécie no catálogo botânico.</summary>
public sealed class Species
{
    /// <summary>Identificador permanente (não muda quando o código é renumerado).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Código de identificação, por exemplo AR-001.</summary>
    public string Code { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string BotanicalFamily { get; set; } = string.Empty;

    public PlantCategory Category { get; set; }

    public PlantOrigin Origin { get; set; }

    /// <summary>Biomas de ocorrência natural (espécies nativas).</summary>
    public Biome Biomes { get; set; }

    public CrownForm CrownForm { get; set; }

    /// <summary>Altura na fase adulta, em metros.</summary>
    public double AdultHeightM { get; set; }

    /// <summary>Diâmetro de copa na fase adulta, em metros. Define a dimensão real do símbolo.</summary>
    public double CrownDiameterM { get; set; } = 1.0;

    /// <summary>Altura da muda no momento do plantio, em metros.</summary>
    public double PlantingHeightM { get; set; }

    /// <summary>Espaçamento recomendado entre exemplares, em metros.</summary>
    public double RecommendedSpacingM { get; set; }

    /// <summary>Densidade recomendada em maciços, em unidades por m². Zero = derivar do espaçamento.</summary>
    public double DensityPerM2 { get; set; }

    /// <summary>Unidade comercial de fornecimento (muda, m², placa, bandeja, vaso...).</summary>
    public string SupplyUnit { get; set; } = "muda";

    public decimal UnitPrice { get; set; }

    public GrowthRate Growth { get; set; }

    public WaterNeed Water { get; set; }

    public LightNeed Light { get; set; }

    public string Flowering { get; set; } = string.Empty;

    /// <summary>Textura visual da folhagem. Não informada = sugestão pela categoria e pelo gênero.</summary>
    public Texture Texture { get; set; }

    /// <summary>Textura considerada nas análises: a cadastrada ou a sugerida.</summary>
    public Texture EffectiveTexture => Texture != Texture.NaoInformada ? Texture : Composition.TextureRules.Suggest(this);

    /// <summary>Folhagem cadastrada. Não informada = estimativa pelo gênero (ipês, paineiras, eritrinas... são decíduos).</summary>
    public LeafHabit LeafHabit { get; set; }

    /// <summary>Meses sem folhas (decíduas) ou com folhagem rala (semidecíduas), por exemplo "jul–set". Vazio = jun–ago.</summary>
    public string LeaflessMonths { get; set; } = string.Empty;

    /// <summary>Folhagem considerada nas análises: a cadastrada ou a estimada.</summary>
    public LeafHabit EffectiveLeafHabit => LeafHabit != LeafHabit.NaoInformada ? LeafHabit : Viridian.Core.Solar.LeafRules.Estimate(this);

    /// <summary>Verdadeiro quando a folhagem usada é estimada (não cadastrada).</summary>
    public bool LeafHabitEstimated => LeafHabit == LeafHabit.NaoInformada;

    /// <summary>Meses sem folhas (ou com folhagem rala) no hemisfério sul; vazio para perenifólias.</summary>
    public MonthSet EffectiveLeaflessMonths
    {
        get
        {
            if (EffectiveLeafHabit is not (LeafHabit.Decidua or LeafHabit.Semidecidua))
            {
                return MonthSet.None;
            }

            MonthSet typed = MonthSet.Parse(LeaflessMonths);
            return typed.IsEmpty ? Viridian.Core.Solar.LeafRules.DefaultLeafless(this) : typed;
        }
    }

    /// <summary>Tolerância ao encharcamento cadastrada. Não informada = estimativa pela categoria e pela necessidade de água.</summary>
    public WaterloggingTolerance Waterlogging { get; set; }

    /// <summary>Tolerância considerada nas análises: a cadastrada ou a estimada.</summary>
    public WaterloggingTolerance EffectiveWaterlogging =>
        Waterlogging != WaterloggingTolerance.NaoInformada ? Waterlogging : Viridian.Core.Water.WaterloggingRules.Estimate(this);

    /// <summary>Verdadeiro quando a tolerância usada é estimada (não cadastrada).</summary>
    public bool WaterloggingEstimated => Waterlogging == WaterloggingTolerance.NaoInformada;

    /// <summary>Escala visual pelo porte adulto.</summary>
    public VisualScale Scale => Composition.TextureRules.ScaleOf(this);

    /// <summary>Cor da folhagem (#RRGGBB). Vazio = cor derivada da categoria.</summary>
    public string FoliageColor { get; set; } = string.Empty;

    /// <summary>Cor da floração (#RRGGBB). Vazio = floração pouco vistosa, não representada.</summary>
    public string FlowerColor { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public SymbolStyle SymbolStyle { get; set; }

    /// <summary>Semente do desenho do símbolo. Zero = derivada do nome científico.</summary>
    public int SymbolSeed { get; set; }

    /// <summary>Quantidade de variantes gráficas disponíveis (1 a 4).</summary>
    public int SymbolVariants { get; set; } = 1;

    /// <summary>Hachura padrão usada quando a espécie é representada como maciço.</summary>
    public string HatchPatternKey { get; set; } = string.Empty;

    /// <summary>Arquivo de fotografia de referência (nome relativo à pasta de fotos da biblioteca).</summary>
    public string PhotoFile { get; set; } = string.Empty;

    /// <summary>Autor da fotografia (crédito obrigatório nas licenças Creative Commons).</summary>
    public string PhotoCredit { get; set; } = string.Empty;

    /// <summary>Licença da fotografia (por exemplo, CC BY 4.0).</summary>
    public string PhotoLicense { get; set; } = string.Empty;

    /// <summary>Endereço de origem da fotografia.</summary>
    public string PhotoSource { get; set; } = string.Empty;

    /// <summary>Código do insumo da muda na tabela de preços (SINAPI ou outra).</summary>
    public string PriceItemCode { get; set; } = string.Empty;

    /// <summary>Código do serviço de plantio na tabela de preços (SINAPI ou outra).</summary>
    public string ServiceItemCode { get; set; } = string.Empty;

    /// <summary>Família Planting personalizada (.rfa) que substitui o símbolo gerado.</summary>
    public string CustomPlantingFamilyFile { get; set; } = string.Empty;

    public string CustomPlantingTypeName { get; set; } = string.Empty;

    /// <summary>Família Detail Item personalizada (.rfa), usada como símbolo gráfico de maciços.</summary>
    public string CustomDetailFamilyFile { get; set; } = string.Empty;

    public string CustomDetailTypeName { get; set; } = string.Empty;

    /// <summary>Indica registro criado ou alterado pelo escritório (preservado em atualizações do catálogo padrão).</summary>
    public bool IsUserDefined { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Code) ? PopularName : $"{Code} · {PopularName}";

    public int EffectiveSymbolSeed => SymbolSeed != 0
        ? SymbolSeed
        : Geometry.DeterministicRandom.StableHash(string.IsNullOrWhiteSpace(ScientificName) ? Id : ScientificName);

    public int EffectiveVariants => Math.Clamp(SymbolVariants, 1, MaxVariants);

    public const int MaxVariants = 4;

    /// <summary>Espaçamento a usar quando nenhum foi informado: o diâmetro adulto da copa.</summary>
    public double EffectiveSpacingM => RecommendedSpacingM > 0 ? RecommendedSpacingM : Math.Max(0.05, CrownDiameterM);

    public bool HasCustomPlantingFamily => !string.IsNullOrWhiteSpace(CustomPlantingFamilyFile);

    public bool HasCustomDetailFamily => !string.IsNullOrWhiteSpace(CustomDetailFamilyFile);

    public Species Clone() => (Species)MemberwiseClone();
}

/// <summary>Validação dos dados de uma espécie antes de gravar no catálogo.</summary>
public static class SpeciesValidator
{
    public static IReadOnlyList<string> Validate(Species species)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(species.PopularName))
        {
            problems.Add("Informe o nome popular.");
        }

        if (string.IsNullOrWhiteSpace(species.ScientificName))
        {
            problems.Add("Informe o nome científico.");
        }

        if (!(species.CrownDiameterM >= SymbolGenerator.MinDiameter && species.CrownDiameterM <= SymbolGenerator.MaxDiameter))
        {
            problems.Add($"O diâmetro de copa deve ficar entre {SymbolGenerator.MinDiameter:0.00} m e {SymbolGenerator.MaxDiameter:0} m.");
        }

        if (species.AdultHeightM < 0 || species.AdultHeightM > 120)
        {
            problems.Add("A altura adulta deve ficar entre 0 e 120 m.");
        }

        if (species.PlantingHeightM < 0 || species.PlantingHeightM > 60)
        {
            problems.Add("A altura de implantação deve ficar entre 0 e 60 m.");
        }

        if (species.RecommendedSpacingM < 0 || species.RecommendedSpacingM > 100)
        {
            problems.Add("O espaçamento recomendado deve ficar entre 0 e 100 m.");
        }

        if (species.DensityPerM2 < 0 || species.DensityPerM2 > 400)
        {
            problems.Add("A densidade deve ficar entre 0 e 400 un/m².");
        }

        if (species.UnitPrice < 0)
        {
            problems.Add("O preço unitário não pode ser negativo.");
        }

        if (!string.IsNullOrWhiteSpace(species.FoliageColor) && !Colors.Rgb.TryParse(species.FoliageColor, out _))
        {
            problems.Add("A cor da folhagem deve estar no formato #RRGGBB.");
        }

        if (!string.IsNullOrWhiteSpace(species.FlowerColor) && !Colors.Rgb.TryParse(species.FlowerColor, out _))
        {
            problems.Add("A cor da floração deve estar no formato #RRGGBB.");
        }

        if (species.SymbolVariants < 1 || species.SymbolVariants > Species.MaxVariants)
        {
            problems.Add($"A quantidade de variantes deve ficar entre 1 e {Species.MaxVariants}.");
        }

        return problems;
    }
}
