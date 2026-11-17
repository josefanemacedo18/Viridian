using Viridian.Core.Model;

namespace Viridian.Core.Symbols;

/// <summary>Catálogo dos estilos de símbolo: nomes de apresentação e estilos sugeridos por categoria.</summary>
public static class SymbolStyles
{
    public static readonly IReadOnlyList<SymbolStyle> All = Enum.GetValues<SymbolStyle>();

    public static string DisplayName(SymbolStyle style) => style switch
    {
        SymbolStyle.Simples => "Simples (círculo e centro)",
        SymbolStyle.ArvoreArredondada => "Árvore — copa arredondada",
        SymbolStyle.ArvoreEspalhada => "Árvore — copa espalhada",
        SymbolStyle.ArvoreColunar => "Árvore — copa colunar",
        SymbolStyle.ArvorePiramidal => "Árvore — copa piramidal",
        SymbolStyle.ArvoreIrregular => "Árvore — copa irregular",
        SymbolStyle.ArvoreRadial => "Árvore — galharia radial",
        SymbolStyle.ArvoreFolhagem => "Árvore — folhagem densa",
        SymbolStyle.Palmeira => "Palmeira — folhas pinadas",
        SymbolStyle.PalmeiraLeque => "Palmeira — folhas em leque",
        SymbolStyle.ArbustoNuvem => "Arbusto — contorno em nuvem",
        SymbolStyle.ArbustoPontiagudo => "Arbusto — folhas pontiagudas",
        SymbolStyle.Herbacea => "Herbácea — touceira",
        SymbolStyle.Forracao => "Forração",
        SymbolStyle.Gramado => "Gramado — tufos",
        SymbolStyle.Trepadeira => "Trepadeira",
        SymbolStyle.Aquatica => "Aquática — folha flutuante",
        _ => style.ToString(),
    };

    /// <summary>Estilo sugerido para uma categoria e forma de copa.</summary>
    public static SymbolStyle Suggest(PlantCategory category, CrownForm form) => category switch
    {
        PlantCategory.Arvore => form switch
        {
            CrownForm.Espalhada => SymbolStyle.ArvoreEspalhada,
            CrownForm.Colunar => SymbolStyle.ArvoreColunar,
            CrownForm.Piramidal => SymbolStyle.ArvorePiramidal,
            CrownForm.Irregular => SymbolStyle.ArvoreIrregular,
            _ => SymbolStyle.ArvoreArredondada,
        },
        PlantCategory.Palmeira => SymbolStyle.Palmeira,
        PlantCategory.Arbusto => SymbolStyle.ArbustoNuvem,
        PlantCategory.Herbacea => SymbolStyle.Herbacea,
        PlantCategory.Forracao => SymbolStyle.Forracao,
        PlantCategory.Gramado => SymbolStyle.Gramado,
        PlantCategory.Trepadeira => SymbolStyle.Trepadeira,
        PlantCategory.Aquatica => SymbolStyle.Aquatica,
        _ => SymbolStyle.Simples,
    };

    /// <summary>Estilos oferecidos primeiro ao cadastrar uma espécie da categoria.</summary>
    public static IReadOnlyList<SymbolStyle> ForCategory(PlantCategory category)
    {
        SymbolStyle[] preferred = category switch
        {
            PlantCategory.Arvore => new[]
            {
                SymbolStyle.ArvoreArredondada, SymbolStyle.ArvoreEspalhada, SymbolStyle.ArvoreColunar,
                SymbolStyle.ArvorePiramidal, SymbolStyle.ArvoreIrregular, SymbolStyle.ArvoreRadial, SymbolStyle.ArvoreFolhagem,
            },
            PlantCategory.Palmeira => new[] { SymbolStyle.Palmeira, SymbolStyle.PalmeiraLeque },
            PlantCategory.Arbusto => new[] { SymbolStyle.ArbustoNuvem, SymbolStyle.ArbustoPontiagudo, SymbolStyle.ArvoreFolhagem },
            PlantCategory.Herbacea => new[] { SymbolStyle.Herbacea, SymbolStyle.ArbustoPontiagudo, SymbolStyle.PalmeiraLeque },
            PlantCategory.Forracao => new[] { SymbolStyle.Forracao, SymbolStyle.Herbacea },
            PlantCategory.Gramado => new[] { SymbolStyle.Gramado, SymbolStyle.Forracao },
            PlantCategory.Trepadeira => new[] { SymbolStyle.Trepadeira, SymbolStyle.ArbustoNuvem },
            PlantCategory.Aquatica => new[] { SymbolStyle.Aquatica, SymbolStyle.Herbacea },
            _ => Array.Empty<SymbolStyle>(),
        };

        return preferred.Concat(All.Where(s => !preferred.Contains(s))).ToList();
    }
}
