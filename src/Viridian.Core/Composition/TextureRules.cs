using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>
/// Proposta de textura e escala visual. A textura sai do gênero (quando conhecido) ou da categoria e da forma
/// da copa; é uma sugestão de partida, que o escritório confirma ou altera no cadastro.
/// </summary>
public static class TextureRules
{
    private static readonly Dictionary<string, Texture> Genus = new(StringComparer.OrdinalIgnoreCase)
    {
        // Folhas miúdas, finas, recortadas ou compostas miúdas.
        ["Zoysia"] = Texture.Fina, ["Paspalum"] = Texture.Fina, ["Axonopus"] = Texture.Fina, ["Cynodon"] = Texture.Fina,
        ["Stenotaphrum"] = Texture.Media, ["Ophiopogon"] = Texture.Fina, ["Liriope"] = Texture.Fina, ["Asparagus"] = Texture.Fina,
        ["Podocarpus"] = Texture.Fina, ["Juniperus"] = Texture.Fina, ["Cupressus"] = Texture.Fina, ["Thuja"] = Texture.Fina,
        ["Araucaria"] = Texture.Fina, ["Pinus"] = Texture.Fina, ["Casuarina"] = Texture.Fina, ["Schinus"] = Texture.Fina,
        ["Jacaranda"] = Texture.Fina, ["Cenostigma"] = Texture.Fina, ["Caesalpinia"] = Texture.Fina, ["Libidibia"] = Texture.Fina,
        ["Paubrasilia"] = Texture.Fina, ["Delonix"] = Texture.Fina, ["Albizia"] = Texture.Fina, ["Anadenanthera"] = Texture.Fina,
        ["Mimosa"] = Texture.Fina, ["Acacia"] = Texture.Fina, ["Calliandra"] = Texture.Fina, ["Parapiptadenia"] = Texture.Fina,
        ["Piptadenia"] = Texture.Fina, ["Enterolobium"] = Texture.Fina, ["Peltophorum"] = Texture.Fina, ["Schizolobium"] = Texture.Fina,
        ["Tipuana"] = Texture.Fina, ["Myracrodruon"] = Texture.Fina, ["Eugenia"] = Texture.Fina, ["Myrciaria"] = Texture.Fina,
        ["Murraya"] = Texture.Fina, ["Buxus"] = Texture.Fina, ["Ligustrum"] = Texture.Fina, ["Russelia"] = Texture.Fina,
        ["Cuphea"] = Texture.Fina, ["Lantana"] = Texture.Fina, ["Nephrolepis"] = Texture.Fina, ["Rumohra"] = Texture.Fina,
        ["Adiantum"] = Texture.Fina, ["Cyperus"] = Texture.Fina, ["Bambusa"] = Texture.Fina, ["Phyllostachys"] = Texture.Fina,
        ["Pennisetum"] = Texture.Fina, ["Muhlenbergia"] = Texture.Fina, ["Callistemon"] = Texture.Fina, ["Lagerstroemia"] = Texture.Media,
        ["Salix"] = Texture.Fina, ["Tamarindus"] = Texture.Fina, ["Dypsis"] = Texture.Grossa, ["Equisetum"] = Texture.Fina,

        // Folhas grandes e marcantes.
        ["Ficus"] = Texture.Grossa, ["Philodendron"] = Texture.Grossa, ["Thaumatophyllum"] = Texture.Grossa, ["Monstera"] = Texture.Grossa,
        ["Alocasia"] = Texture.Grossa, ["Colocasia"] = Texture.Grossa, ["Xanthosoma"] = Texture.Grossa, ["Heliconia"] = Texture.Grossa,
        ["Strelitzia"] = Texture.Grossa, ["Musa"] = Texture.Grossa, ["Ravenala"] = Texture.Grossa, ["Agave"] = Texture.Grossa,
        ["Furcraea"] = Texture.Grossa, ["Yucca"] = Texture.Grossa, ["Pandanus"] = Texture.Grossa, ["Aechmea"] = Texture.Grossa,
        ["Alcantarea"] = Texture.Grossa, ["Neoregelia"] = Texture.Grossa, ["Vriesea"] = Texture.Grossa, ["Ananas"] = Texture.Grossa,
        ["Asplenium"] = Texture.Grossa, ["Terminalia"] = Texture.Grossa, ["Cecropia"] = Texture.Grossa, ["Tectona"] = Texture.Grossa,
        ["Magnolia"] = Texture.Grossa, ["Calathea"] = Texture.Grossa, ["Goeppertia"] = Texture.Grossa, ["Ctenanthe"] = Texture.Media,
        ["Victoria"] = Texture.Grossa, ["Nymphaea"] = Texture.Grossa, ["Nelumbo"] = Texture.Grossa, ["Clusia"] = Texture.Grossa,
        ["Artocarpus"] = Texture.Grossa, ["Mangifera"] = Texture.Grossa, ["Spathiphyllum"] = Texture.Grossa, ["Anthurium"] = Texture.Grossa,
        ["Canna"] = Texture.Grossa, ["Zingiber"] = Texture.Grossa, ["Alpinia"] = Texture.Grossa, ["Costus"] = Texture.Grossa,
        ["Dracaena"] = Texture.Media, ["Cordyline"] = Texture.Media, ["Cycas"] = Texture.Media, ["Zamia"] = Texture.Media,
    };

    public static Texture Suggest(Species species)
    {
        string genus = (species.ScientificName ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (Genus.TryGetValue(genus, out Texture byGenus))
        {
            return byGenus;
        }

        return species.Category switch
        {
            PlantCategory.Gramado => Texture.Fina,
            PlantCategory.Palmeira => Texture.Grossa,
            PlantCategory.Forracao => Texture.Fina,
            PlantCategory.Aquatica => Texture.Media,
            PlantCategory.Arvore => species.CrownForm switch
            {
                CrownForm.Colunar or CrownForm.Piramidal => Texture.Fina,
                CrownForm.Irregular => Texture.Grossa,
                _ => Texture.Media,
            },
            _ => Texture.Media,
        };
    }

    /// <summary>Escala visual pelo porte adulto.</summary>
    public static VisualScale ScaleOf(Species species)
    {
        double height = Math.Max(0, species.AdultHeightM);
        double crown = Math.Max(0, species.CrownDiameterM);
        if (height >= 12 || crown >= 10)
        {
            return VisualScale.Monumental;
        }

        return height <= 1.0 && crown <= 1.2 ? VisualScale.Miuda : VisualScale.Media;
    }
}
