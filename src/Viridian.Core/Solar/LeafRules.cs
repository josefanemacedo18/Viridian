using Viridian.Core.Model;

namespace Viridian.Core.Solar;

/// <summary>
/// Estimativa da folhagem pelo gênero ou pela espécie, para as fichas sem a informação cadastrada. Referências usuais
/// da arborização brasileira; confira e cadastre na ficha quando o comportamento local for diferente.
/// </summary>
public static class LeafRules
{
    private static readonly HashSet<string> DeciduousGenera = new(StringComparer.OrdinalIgnoreCase)
    {
        "Handroanthus", "Tabebuia", "Cybistax", "Ceiba", "Pseudobombax", "Erythrina", "Delonix", "Lagerstroemia", "Plumeria",
        "Schizolobium", "Spondias", "Myracrodruon", "Enterolobium", "Peltophorum", "Melia", "Acer", "Hovenia", "Kielmeyera",
        "Cordia", "Jacaranda", "Bauhinia", "Platanus", "Liquidambar", "Prunus", "Ginkgo", "Cavanillesia", "Amburana", "Astronium",
    };

    private static readonly HashSet<string> SemideciduousGenera = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tipuana", "Cenostigma", "Libidibia", "Anadenanthera", "Cassia", "Senna", "Hymenaea", "Cariniana", "Luehea", "Guazuma",
        "Terminalia", "Couroupita", "Piptadenia", "Pterogyne", "Copaifera", "Dipteryx", "Caryocar", "Lecythis", "Paubrasilia",
        "Sapindus", "Triplaris", "Mimosa", "Dalbergia", "Qualea", "Moringa", "Albizia", "Machaerium", "Pachira", "Clitoria",
    };

    /// <summary>Exceções dentro dos gêneros (espécie → folhagem).</summary>
    private static readonly Dictionary<string, LeafHabit> Species = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jacaranda mimosifolia"] = LeafHabit.Semidecidua,
        ["Jacaranda puberula"] = LeafHabit.Semidecidua,
        ["Bauhinia variegata"] = LeafHabit.Semidecidua,
        ["Bauhinia forficata"] = LeafHabit.Semidecidua,
        ["Cassia grandis"] = LeafHabit.Decidua,
        ["Cenostigma pyramidale"] = LeafHabit.Decidua,
        ["Cordia superba"] = LeafHabit.Perenifolia,
        ["Ceiba pentandra"] = LeafHabit.Decidua,
    };

    public static LeafHabit Estimate(Model.Species species)
    {
        if (species.Category is not (PlantCategory.Arvore or PlantCategory.Arbusto))
        {
            return LeafHabit.Perenifolia; // palmeiras, forrações e herbáceas: folhagem constante para a sombra
        }

        string name = string.Join(' ', (species.ScientificName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (Species.TryGetValue(name, out LeafHabit habit))
        {
            return habit;
        }

        string genus = name.Split(' ').FirstOrDefault() ?? string.Empty;
        return DeciduousGenera.Contains(genus) ? LeafHabit.Decidua
            : SemideciduousGenera.Contains(genus) ? LeafHabit.Semidecidua
            : LeafHabit.Perenifolia;
    }

    /// <summary>Meses sem folhas mais comuns no hemisfério sul: o fim da estação seca e fria.</summary>
    public static MonthSet DefaultLeafless(Model.Species species)
    {
        string genus = (species.ScientificName ?? string.Empty).Split(' ').FirstOrDefault() ?? string.Empty;
        // Ipês e afins ficam sem folhas pouco antes e durante a floração (jul a set).
        return genus is "Handroanthus" or "Tabebuia" or "Cybistax" ? MonthSet.Range(7, 9) : MonthSet.Range(6, 8);
    }

    /// <summary>Fração da opacidade normal da copa nos meses sem folhas.</summary>
    public static double LeaflessFactor(LeafHabit habit) => habit switch
    {
        LeafHabit.Decidua => 0.3,      // só galhos
        LeafHabit.Semidecidua => 0.6,  // folhagem rala
        _ => 1.0,
    };
}
