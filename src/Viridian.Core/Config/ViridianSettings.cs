using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viridian.Core.Coding;
using Viridian.Core.Colors;
using Viridian.Core.Hatch;
using Viridian.Core.Integrity;
using Viridian.Core.Massing;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Core.Costs;
using Viridian.Core.Maintenance;
using Viridian.Core.Interference;
using Viridian.Core.Composition;
using Viridian.Core.Detailing;
using Viridian.Core.Model3D;

namespace Viridian.Core.Config;

/// <summary>Padrão gráfico de uma camada de símbolo: espessura de linha do Revit (1 a 16) e cor.</summary>
public sealed class TierStyle
{
    public SymbolTier Tier { get; set; }

    public int LineWeight { get; set; } = 1;

    /// <summary>Cor em hexadecimal (#RRGGBB).</summary>
    public string Color { get; set; } = "#000000";
}

public sealed class LabelSettings
{
    /// <summary>Altura do texto das etiquetas, em milímetros no papel.</summary>
    public double TextSizeMm { get; set; } = 2.0;

    /// <summary>Distância da etiqueta ao elemento, em milímetros no papel.</summary>
    public double OffsetMm { get; set; } = 8.0;

    public bool LeaderByDefault { get; set; } = true;
}

public sealed class QuantitySettings
{
    /// <summary>Reserva de reposição aplicada aos exemplares isolados, em porcentagem.</summary>
    public double IndividualReservePercent { get; set; }

    /// <summary>Reserva de reposição sugerida para novos maciços, em porcentagem.</summary>
    public double MassingReservePercent { get; set; } = 10;
}

public sealed class PlantingDefaults
{
    public double EdgeMarginM { get; set; } = 0.30;

    public double JitterPercent { get; set; } = 10;

    public int MaxCount { get; set; } = 2000;

    public bool RandomRotation { get; set; } = true;

    /// <summary>
    /// Deslocamento vertical dos exemplares em relação ao nível da vista, em metros.
    /// Útil em terrenos acidentados, para manter os símbolos acima da topografia.
    /// </summary>
    public double ElevationOffsetM { get; set; }
}

/// <summary>Configurações do escritório. Gravadas em JSON e compartilháveis entre computadores.</summary>
public sealed class ViridianSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Pasta raiz da biblioteca. Vazio = pasta padrão do usuário.</summary>
    public string LibraryRoot { get; set; } = string.Empty;

    /// <summary>Arquivo de modelo de família (.rft) usado para gerar os símbolos. Vazio = localizar automaticamente.</summary>
    public string FamilyTemplateFile { get; set; } = string.Empty;

    public ScaleProfileSet Scales { get; set; } = new();

    /// <summary>Reaplica o perfil de representação automaticamente quando a escala de uma vista muda.</summary>
    public bool AutoApplyScaleProfiles { get; set; } = true;

    public List<TierStyle> TierStyles { get; set; } = DefaultTierStyles();

    public DensityRules Density { get; set; } = new();

    public InstanceCodeFormat InstanceCode { get; set; } = new();

    public string MassingPrefix { get; set; } = "MC";

    public AnalysisSettings Analysis { get; set; } = new();

    public LabelSettings Labels { get; set; } = new();

    public QuantitySettings Quantities { get; set; } = new();

    public PlantingDefaults Planting { get; set; } = new();

    /// <summary>Grava o código da espécie em "Marca de tipo" e o código do exemplar em "Marca".</summary>
    public bool SyncMarks { get; set; } = true;

    /// <summary>Espécies usadas por último (identificadores), mostradas primeiro nos seletores rápidos.</summary>
    public List<string> RecentSpecies { get; set; } = new();

    /// <summary>Registra o uso de uma espécie na lista de recentes (no máximo 12).</summary>
    public void TouchRecent(string speciesId)
    {
        if (string.IsNullOrWhiteSpace(speciesId))
        {
            return;
        }

        RecentSpecies ??= new List<string>();
        RecentSpecies.RemoveAll(id => string.Equals(id, speciesId, StringComparison.OrdinalIgnoreCase));
        RecentSpecies.Insert(0, speciesId);
        if (RecentSpecies.Count > 12)
        {
            RecentSpecies.RemoveRange(12, RecentSpecies.Count - 12);
        }
    }

    public List<HatchPattern> CustomHatches { get; set; } = new();

    /// <summary>Linhas em tons de verde-escuro e marrom, que leem bem sobre as copas coloridas e também em preto e branco.</summary>
    public static List<TierStyle> DefaultTierStyles() => new()
    {
        new TierStyle { Tier = SymbolTier.MinimalOutline, LineWeight = 3, Color = "#2E4A36" },
        new TierStyle { Tier = SymbolTier.Center, LineWeight = 2, Color = "#5A4030" },
        new TierStyle { Tier = SymbolTier.Outline, LineWeight = 3, Color = "#2E4A36" },
        new TierStyle { Tier = SymbolTier.Essential, LineWeight = 2, Color = "#5E4A36" },
        new TierStyle { Tier = SymbolTier.Principal, LineWeight = 1, Color = "#6E5A44" },
        new TierStyle { Tier = SymbolTier.Detail, LineWeight = 1, Color = "#4F6B55" },
    };

    /// <summary>Cores da vegetação: preenchimento das copas, floração e maciços.</summary>
    public ColorSettings Colors { get; set; } = new();

    /// <summary>Orçamento: BDI e referência da tabela de preços.</summary>
    public CostSettings Costs { get; set; } = new();

    /// <summary>Cronograma de manutenção: mês de plantio e estação seca da região.</summary>
    public MaintenanceSettings Maintenance { get; set; } = new();

    /// <summary>Distâncias mínimas entre o plantio e redes, postes, edificações e pavimentos.</summary>
    public InterferenceSettings Interference { get; set; } = new();

    /// <summary>Tamanho do texto e contraste das janelas do Viridian.</summary>
    public AccessibilitySettings Accessibility { get; set; } = new();

    /// <summary>Regras de composição dos canteiros.</summary>
    public CompositionSettings Composition { get; set; } = new();

    /// <summary>Modelos das notas de especificação de plantio.</summary>
    public SpecificationSettings Specifications { get; set; } = new();

    /// <summary>Maquete 3D.</summary>
    public ModelSettings Model3D { get; set; } = new();

    public TierStyle StyleOf(SymbolTier tier) =>
        TierStyles.FirstOrDefault(s => s.Tier == tier) ?? DefaultTierStyles().FirstOrDefault(s => s.Tier == tier) ?? new TierStyle { Tier = tier };

    /// <summary>Garante valores válidos depois de ler um arquivo editado manualmente ou de versão anterior.</summary>
    public void Normalize()
    {
        LibraryRoot ??= string.Empty;
        FamilyTemplateFile ??= string.Empty;
        Scales ??= new ScaleProfileSet();
        Scales.Normalize();
        RecentSpecies ??= new List<string>();
        Colors ??= new ColorSettings();
        Colors.Normalize();
        Costs ??= new CostSettings();
        Costs.Normalize();
        Maintenance ??= new MaintenanceSettings();
        Maintenance.Normalize();
        Interference ??= new InterferenceSettings();
        Interference.Normalize();
        Accessibility ??= new AccessibilitySettings();
        Accessibility.Normalize();
        Composition ??= new CompositionSettings();
        Composition.Normalize();
        Specifications ??= new SpecificationSettings();
        Specifications.Normalize();
        Model3D ??= new ModelSettings();
        Model3D.Normalize();
        Density ??= new DensityRules();
        Density.Normalize();
        InstanceCode ??= new InstanceCodeFormat();
        InstanceCode.Normalize();
        Analysis ??= new AnalysisSettings();
        Analysis.Normalize();
        Labels ??= new LabelSettings();
        Labels.TextSizeMm = Clamp(Labels.TextSizeMm, 0.8, 20, 2.0);
        Labels.OffsetMm = Clamp(Labels.OffsetMm, 0, 100, 8.0);
        Quantities ??= new QuantitySettings();
        Quantities.IndividualReservePercent = Clamp(Quantities.IndividualReservePercent, 0, 100, 0);
        Quantities.MassingReservePercent = Clamp(Quantities.MassingReservePercent, 0, 100, 10);
        Planting ??= new PlantingDefaults();
        Planting.EdgeMarginM = Clamp(Planting.EdgeMarginM, 0, 50, 0.30);
        Planting.JitterPercent = Clamp(Planting.JitterPercent, 0, 50, 10);
        Planting.MaxCount = Math.Clamp(Planting.MaxCount, 1, Distribution.PlantDistributor.HardLimit);
        Planting.ElevationOffsetM = Clamp(Planting.ElevationOffsetM, -1000, 1000, 0);

        MassingPrefix = string.IsNullOrWhiteSpace(MassingPrefix)
            ? "MC"
            : new string(MassingPrefix.Trim().Where(char.IsLetterOrDigit).Take(6).ToArray()).ToUpperInvariant();
        if (MassingPrefix.Length == 0)
        {
            MassingPrefix = "MC";
        }

        TierStyles ??= new List<TierStyle>();
        List<TierStyle> defaults = DefaultTierStyles();
        TierStyles = TierVisibility.LineTiers
            .Select(tier => TierStyles.FirstOrDefault(s => s is not null && s.Tier == tier) ?? defaults.First(d => d.Tier == tier))
            .ToList();
        foreach (TierStyle style in TierStyles)
        {
            style.LineWeight = Math.Clamp(style.LineWeight, 1, 16);
            style.Color = ColorHex.Normalize(style.Color);
        }

        CustomHatches ??= new List<HatchPattern>();
        CustomHatches = CustomHatches.Where(h => h is not null && h.IsValid(out _)).ToList();
        foreach (HatchPattern hatch in CustomHatches)
        {
            hatch.IsBuiltIn = false;
        }
    }

    public ViridianSettings Clone() => ViridianJson.Deserialize<ViridianSettings>(ViridianJson.Serialize(this)) ?? new ViridianSettings();

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>Acessibilidade das janelas.</summary>
public sealed class AccessibilitySettings
{
    /// <summary>Escalas de interface oferecidas (1 = 100%).</summary>
    public static readonly IReadOnlyList<double> Scales = new[] { 1.0, 1.15, 1.3, 1.5 };

    /// <summary>Ampliação de textos e controles das janelas.</summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>Cores de alto contraste (o alto contraste do Windows é seguido automaticamente).</summary>
    public bool HighContrast { get; set; }

    public void Normalize()
    {
        TextScale = double.IsFinite(TextScale) ? Math.Clamp(TextScale, 1.0, 2.0) : 1.0;
    }
}

/// <summary>Leitura e escrita de cores no formato #RRGGBB.</summary>
public static class ColorHex
{
    public static string Normalize(string? text) =>
        TryParse(text, out byte r, out byte g, out byte b) ? $"#{r:X2}{g:X2}{b:X2}" : "#000000";

    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        string value = (text ?? string.Empty).Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return false;
        }

        return byte.TryParse(value.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && byte.TryParse(value.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && byte.TryParse(value.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }
}

/// <summary>Serialização JSON padronizada do Viridian.</summary>
public static class ViridianJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
