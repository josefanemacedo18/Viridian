using System.Globalization;
using Viridian.Core.Massing;
using Viridian.Core.Reports;

namespace Viridian.Core.Identification;

/// <summary>Modelos de identificação disponíveis para etiquetas.</summary>
public enum LabelModel
{
    CodigoSimples = 1,
    CodigoNomePopular = 2,
    CodigoNomeCientifico = 3,
    CodigoLinhaIndicadora = 4,
    CodigoMacico = 5,
    AreaQuantidade = 6,
}

/// <summary>Composição do texto das etiquetas de exemplares e de maciços.</summary>
public static class LabelText
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyList<LabelModel> Models = Enum.GetValues<LabelModel>();

    public static string DisplayName(LabelModel model) => model switch
    {
        LabelModel.CodigoSimples => "Código simples",
        LabelModel.CodigoNomePopular => "Código com nome popular",
        LabelModel.CodigoNomeCientifico => "Código com nome científico",
        LabelModel.CodigoLinhaIndicadora => "Código com linha indicadora",
        LabelModel.CodigoMacico => "Código de maciço",
        LabelModel.AreaQuantidade => "Etiqueta com área e quantidade",
        _ => model.ToString(),
    };

    /// <summary>O modelo exige linha indicadora independentemente da opção escolhida.</summary>
    public static bool ForcesLeader(LabelModel model) => model == LabelModel.CodigoLinhaIndicadora;

    /// <summary>Texto da etiqueta de um ou mais exemplares da mesma espécie.</summary>
    /// <param name="model">Modelo de identificação.</param>
    /// <param name="speciesCode">Código da espécie.</param>
    /// <param name="popularName">Nome popular.</param>
    /// <param name="scientificName">Nome científico.</param>
    /// <param name="count">Quantidade de exemplares representados pela etiqueta.</param>
    /// <param name="projectCode">Código do exemplar no projeto (usado quando a etiqueta é individual).</param>
    public static string ForPlants(LabelModel model, string speciesCode, string popularName, string scientificName, int count, string projectCode)
    {
        string code = string.IsNullOrWhiteSpace(speciesCode) ? "?" : speciesCode.Trim();
        string prefix = count > 1 ? count.ToString(PtBr) + " " : string.Empty;
        return model switch
        {
            LabelModel.CodigoNomePopular => Join(prefix + code, popularName),
            LabelModel.CodigoNomeCientifico => Join(prefix + code, scientificName),
            LabelModel.AreaQuantidade => count > 1
                ? Join(code, popularName) + "\n" + count.ToString(PtBr) + " un"
                : Join(code, popularName) + (string.IsNullOrWhiteSpace(projectCode) ? string.Empty : "\n" + projectCode.Trim()),
            _ => prefix + code,
        };
    }

    /// <summary>Texto da etiqueta de um maciço.</summary>
    public static string ForMassing(LabelModel model, MassingRecord record)
    {
        string id = string.IsNullOrWhiteSpace(record.Id) ? "?" : record.Id.Trim();
        string code = string.IsNullOrWhiteSpace(record.SpeciesCode) ? "?" : record.SpeciesCode.Trim();
        return model switch
        {
            LabelModel.CodigoSimples => code,
            LabelModel.CodigoNomePopular => Join(code, record.PopularName),
            LabelModel.CodigoNomeCientifico => Join(code, record.ScientificName),
            LabelModel.CodigoLinhaIndicadora => id + " " + code,
            LabelModel.AreaQuantidade => id + "\n" + Join(code, record.PopularName) + "\n" + AreaAndQuantity(record),
            _ => id,
        };
    }

    /// <summary>Linha de área e quantidade, por exemplo "45,30 m² · 725 mudas".</summary>
    public static string AreaAndQuantity(MassingRecord record)
    {
        string area = record.AreaM2.ToString("N2", PtBr) + " m²";
        if (SupplyUnits.IsAreaBased(record.SupplyUnit))
        {
            return area;
        }

        string unit = string.IsNullOrWhiteSpace(record.SupplyUnit) ? "un" : record.SupplyUnit.Trim();
        return area + " · " + record.Quantity.ToString("N0", PtBr) + " " + unit;
    }

    private static string Join(string code, string? name) =>
        string.IsNullOrWhiteSpace(name) ? code : code + " " + name.Trim();
}
