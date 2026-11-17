using System.Globalization;
using System.Text.RegularExpressions;
using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Modelos de notas de especificação, editáveis pelo escritório.</summary>
public sealed class SpecificationSettings
{
    public Dictionary<string, string> Templates { get; set; } = SpecificationTemplates.Defaults();

    public string TemplateOf(DetailKind kind) =>
        Templates.TryGetValue(kind.ToString(), out string? t) && !string.IsNullOrWhiteSpace(t) ? t : SpecificationTemplates.Defaults()[kind.ToString()];

    public string General => Templates.TryGetValue(SpecificationTemplates.GeneralKey, out string? t) && !string.IsNullOrWhiteSpace(t)
        ? t
        : SpecificationTemplates.Defaults()[SpecificationTemplates.GeneralKey];

    public void Normalize()
    {
        Templates ??= new Dictionary<string, string>();
        foreach ((string key, string value) in SpecificationTemplates.Defaults())
        {
            if (!Templates.TryGetValue(key, out string? current) || string.IsNullOrWhiteSpace(current))
            {
                Templates[key] = value;
            }
        }
    }
}

/// <summary>
/// Notas de especificação com campos variáveis entre chaves, preenchidos pelos dados da espécie e do plantio:
/// {codigo}, {nome_popular}, {nome_cientifico}, {altura_muda}, {altura_adulta}, {copa_adulta}, {espacamento},
/// {densidade}, {unidade}, {cova_largura}, {cova_profundidade}, {torrao}, {tutor}, {coroamento}, {camada_solo}.
/// </summary>
public static class SpecificationTemplates
{
    public const string GeneralKey = "Geral";

    public static readonly IReadOnlyList<string> Fields = new[]
    {
        "codigo", "nome_popular", "nome_cientifico", "altura_muda", "altura_adulta", "copa_adulta", "espacamento", "densidade",
        "unidade", "cova_largura", "cova_profundidade", "torrao", "tutor", "coroamento", "camada_solo",
    };

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex Token = new(@"\{([a-z_]+)\}", RegexOptions.Compiled);

    public static Dictionary<string, string> Defaults() => new()
    {
        [GeneralKey] =
            "As mudas devem ser sadias, isentas de pragas e doenças, com torrão íntegro e sistema radicular bem formado, sem raízes enoveladas. " +
            "Mudas de espécies nativas devem vir de viveiro registrado no RENASEM. Antes do plantio, remover entulho, corrigir a acidez do solo " +
            "conforme análise e garantir a drenagem. Mudas fora do padrão especificado serão recusadas na obra.",
        [nameof(DetailKind.CovaArvore)] =
            "{codigo} {nome_popular} ({nome_cientifico}): muda com altura mínima de {altura_muda}, haste única e retilínea. " +
            "Cova de {cova_largura} × {cova_largura} × {cova_profundidade}, preenchida com terra vegetal e composto orgânico (3:1) e adubação de plantio. " +
            "Torrão de aproximadamente {torrao}, colo no nível do solo. Tutoramento com {tutor}, amarrilho em \"8\". " +
            "Coroamento de {coroamento} de diâmetro com cobertura morta. Rega abundante logo após o plantio.",
        [nameof(DetailKind.BercoPalmeira)] =
            "{codigo} {nome_popular} ({nome_cientifico}): muda com altura mínima de {altura_muda} e estipe sem danos. " +
            "Berço de {cova_largura} × {cova_largura} × {cova_profundidade}, com camada drenante no fundo e terra vegetal com composto orgânico. " +
            "Escoramento com {tutor}, protegendo a estipe nos pontos de contato. Folhas amarradas no transporte e soltas após o plantio. " +
            "Coroamento de {coroamento} com cobertura morta.",
        [nameof(DetailKind.CovaArbusto)] =
            "{codigo} {nome_popular} ({nome_cientifico}): muda com altura mínima de {altura_muda}, bem enfolhada desde a base. " +
            "Cova de {cova_largura} × {cova_largura} × {cova_profundidade} com terra vegetal e composto orgânico (3:1). " +
            "Espaçamento de {espacamento} entre mudas. Cobertura morta de 5 cm.",
        [nameof(DetailKind.CanteiroForracao)] =
            "{codigo} {nome_popular} ({nome_cientifico}): plantio em canteiro com preparo do solo em {camada_solo} de profundidade " +
            "(terra vegetal e composto orgânico 3:1). Densidade de {densidade}, espaçamento de {espacamento} em quincôncio. " +
            "Fornecimento em {unidade}. Cobertura morta de 5 cm até o fechamento do canteiro.",
        [nameof(DetailKind.Gramado)] =
            "{codigo} {nome_popular} ({nome_cientifico}): placas sobre camada de terra vegetal peneirada de {camada_solo}, nivelada e adubada. " +
            "Placas justapostas com juntas desencontradas, compactadas levemente. Rega diária nas duas primeiras semanas. " +
            "Fornecimento em {unidade}.",
    };

    /// <summary>Valores dos campos para uma espécie.</summary>
    public static Dictionary<string, string> Values(Species species)
    {
        PlantingDimensions d = PlantingDetails.Dimensions(species);
        string density = species.DensityPerM2 > 0 ? species.DensityPerM2.ToString("0.#", Pt) + " un/m²" : "conforme espaçamento";
        string stakes = d.Stakes switch
        {
            0 => "sem tutor",
            1 => "1 tutor de " + PlantingDetails.M(d.StakeHeightM),
            _ => d.Stakes + (d.Kind == DetailKind.BercoPalmeira ? " escoras de " : " tutores de ") + PlantingDetails.M(d.StakeHeightM),
        };
        return new Dictionary<string, string>
        {
            ["codigo"] = species.Code,
            ["nome_popular"] = species.PopularName,
            ["nome_cientifico"] = species.ScientificName,
            ["altura_muda"] = PlantingDetails.M(d.SeedlingHeightM),
            ["altura_adulta"] = species.AdultHeightM > 0 ? PlantingDetails.M(species.AdultHeightM) : "—",
            ["copa_adulta"] = PlantingDetails.M(species.CrownDiameterM),
            ["espacamento"] = PlantingDetails.M(species.EffectiveSpacingM),
            ["densidade"] = density,
            ["unidade"] = string.IsNullOrWhiteSpace(species.SupplyUnit) ? "muda" : species.SupplyUnit,
            ["cova_largura"] = PlantingDetails.M(d.PitWidthM),
            ["cova_profundidade"] = PlantingDetails.M(d.PitDepthM),
            ["torrao"] = "Ø " + PlantingDetails.M(d.RootBallM),
            ["tutor"] = stakes,
            ["coroamento"] = PlantingDetails.M(d.CrownRingM),
            ["camada_solo"] = PlantingDetails.M(d.SoilLayerM),
        };
    }

    /// <summary>Preenche o modelo. Campos desconhecidos permanecem entre chaves, para o usuário notar.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Token.Replace(template ?? string.Empty, m => values.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);

    public static string Render(SpecificationSettings settings, Species species) =>
        Render(settings.TemplateOf(PlantingDetails.KindFor(species.Category)), Values(species));

    /// <summary>Campos usados no modelo que não existem (erros de digitação).</summary>
    public static IReadOnlyList<string> UnknownFields(string template) =>
        Token.Matches(template ?? string.Empty).Select(m => m.Groups[1].Value).Where(f => !Fields.Contains(f)).Distinct().ToList();
}
