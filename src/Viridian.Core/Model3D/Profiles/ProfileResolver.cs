using Viridian.Core.Model;

namespace Viridian.Core.Model3D.Profiles;

/// <summary>
/// Escolhe o perfil 3D de cada espécie: o perfil cadastrado na ficha; senão o perfil indicado para o gênero (ou a
/// espécie); senão o perfil-base da categoria e da forma da copa. Não altera os dados botânicos da espécie.
/// </summary>
public static class ProfileResolver
{
    private static readonly Dictionary<string, string> BySpecies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Syagrus romanzoffiana"] = "jeriva",
        ["Dracaena trifasciata"] = "roseta-rigida",
        ["Euterpe oleracea"] = "palmeira-touceira",
        ["Tradescantia spathacea"] = "bromelia",
        ["Asplenium nidus"] = "bromelia",
        ["Asparagus densiflorus"] = "graminea",
        ["Ophiopogon japonicus"] = "graminea",
        ["Chlorophytum comosum"] = "touceira-fita",
        ["Bulbine frutescens"] = "touceira-fita",
        ["Hydrocotyle bonariensis"] = "aquatica-flutuante",
        ["Myriophyllum aquaticum"] = "forracao",
        ["Russelia equisetiformis"] = "graminea",
    };

    private static readonly Dictionary<string, string> ByGenus = new(StringComparer.OrdinalIgnoreCase)
    {
        // Árvores de referência e especiais.
        ["Handroanthus"] = "ipe", ["Tabebuia"] = "ipe", ["Cybistax"] = "ipe",
        ["Delonix"] = "flamboyant",
        ["Araucaria"] = "araucaria",
        ["Pinus"] = "conifera", ["Cupressus"] = "conifera", ["Podocarpus"] = "conifera", ["Juniperus"] = "conifera",
        ["Platycladus"] = "conifera", ["Casuarina"] = "conifera", ["Thuja"] = "conifera",

        // Palmeiras.
        ["Roystonea"] = "palmeira-imperial",
        ["Livistona"] = "palmeira-leque", ["Washingtonia"] = "palmeira-leque", ["Bismarckia"] = "palmeira-leque",
        ["Copernicia"] = "palmeira-leque", ["Mauritia"] = "palmeira-leque", ["Licuala"] = "palmeira-leque",
        ["Dypsis"] = "palmeira-touceira", ["Rhapis"] = "palmeira-touceira", ["Bactris"] = "palmeira-touceira",
        ["Caryota"] = "palmeira-touceira", ["Chamaedorea"] = "palmeira-touceira", ["Ptychosperma"] = "palmeira-touceira",
        ["Allagoptera"] = "palmeira-touceira",
        ["Cycas"] = "cica",
        ["Ravenala"] = "ravenala",

        // Rosetas, tufos e folhagens.
        ["Agave"] = "agave",
        ["Yucca"] = "tufos-em-caule", ["Dracaena"] = "tufos-em-caule", ["Cordyline"] = "tufos-em-caule",
        ["Phormium"] = "roseta-rigida",
        ["Alcantarea"] = "bromelia", ["Aechmea"] = "bromelia", ["Neoregelia"] = "bromelia-tanque", ["Nidularium"] = "bromelia-tanque", ["Vriesea"] = "bromelia",
        ["Billbergia"] = "bromelia", ["Ananas"] = "bromelia",
        ["Nephrolepis"] = "samambaia", ["Rumohra"] = "samambaia",
        ["Strelitzia"] = "estrelitzia",
        ["Heliconia"] = "herbacea-folha-larga", ["Canna"] = "herbacea-folha-larga", ["Alpinia"] = "herbacea-folha-larga",
        ["Musa"] = "herbacea-folha-larga", ["Monstera"] = "herbacea-folha-larga", ["Thaumatophyllum"] = "herbacea-folha-larga",
        ["Alocasia"] = "herbacea-folha-larga", ["Caladium"] = "herbacea-folha-larga", ["Anthurium"] = "herbacea-folha-larga",
        ["Spathiphyllum"] = "herbacea-folha-larga", ["Zantedeschia"] = "herbacea-folha-larga", ["Aglaonema"] = "herbacea-folha-larga",
        ["Dieffenbachia"] = "herbacea-folha-larga", ["Goeppertia"] = "herbacea-folha-larga", ["Ctenanthe"] = "herbacea-folha-larga",
        ["Stromanthe"] = "herbacea-folha-larga", ["Maranta"] = "herbacea-folha-larga", ["Costus"] = "herbacea-folha-larga",
        ["Hedychium"] = "herbacea-folha-larga", ["Etlingera"] = "herbacea-folha-larga", ["Thalia"] = "herbacea-folha-larga",
        ["Echinodorus"] = "herbacea-folha-larga", ["Sagittaria"] = "herbacea-folha-larga",
        ["Neomarica"] = "touceira-fita", ["Agapanthus"] = "touceira-fita", ["Dietes"] = "touceira-fita", ["Hemerocallis"] = "touceira-fita",
        ["Liriope"] = "touceira-fita", ["Dianella"] = "touceira-fita", ["Clivia"] = "touceira-fita", ["Hippeastrum"] = "touceira-fita",
        ["Cortaderia"] = "graminea", ["Cenchrus"] = "graminea", ["Pennisetum"] = "graminea",

        // Aquáticas.
        ["Victoria"] = "aquatica-flutuante", ["Nymphaea"] = "aquatica-flutuante", ["Nymphoides"] = "aquatica-flutuante",
        ["Nelumbo"] = "aquatica-flutuante", ["Pistia"] = "aquatica-flutuante", ["Hydrocleys"] = "aquatica-flutuante",
        ["Salvinia"] = "aquatica-flutuante",
        ["Typha"] = "aquatica-emergente", ["Cyperus"] = "aquatica-emergente", ["Equisetum"] = "aquatica-emergente", ["Iris"] = "aquatica-emergente",
    };

    /// <summary>Perfil indicado pela regra (sem considerar o perfil cadastrado na ficha), para mostrar "automático: ...".</summary>
    public static string AutomaticId(Species species)
    {
        ArgumentNullException.ThrowIfNull(species);
        string name = string.Join(' ', (species.ScientificName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        string genus = name.Split(' ').FirstOrDefault() ?? string.Empty;
        if (BySpecies.TryGetValue(name, out string? id))
        {
            return id;
        }

        if (species.Category == PlantCategory.Aquatica && genus == "Pontederia")
        {
            return name.EndsWith("crassipes", StringComparison.OrdinalIgnoreCase) ? "aquatica-flutuante" : "herbacea-folha-larga";
        }

        if (species.Category == PlantCategory.Aquatica && genus == "Canna")
        {
            return "aquatica-emergente";
        }

        if (ByGenus.TryGetValue(genus, out id) && Compatible(id, species.Category))
        {
            return id;
        }

        return species.Category switch
        {
            PlantCategory.Palmeira => "palmeira-estipe",
            PlantCategory.Arbusto => species.CrownForm == CrownForm.Colunar ? "arbusto-ereto" : "arbusto-arredondado",
            PlantCategory.Herbacea => species.EffectiveTexture switch
            {
                Texture.Grossa => "herbacea-folha-larga",
                Texture.Fina => "touceira-fita",
                _ => "arbusto-arredondado",
            },
            PlantCategory.Forracao => "forracao",
            PlantCategory.Gramado => "gramado",
            PlantCategory.Trepadeira => "trepadeira",
            PlantCategory.Aquatica => "aquatica-emergente",
            _ => species.CrownForm switch
            {
                CrownForm.Espalhada => "arvore-espalhada",
                CrownForm.Colunar => "arvore-colunar",
                CrownForm.Piramidal => "arvore-conica",
                CrownForm.Irregular => "arvore-irregular",
                _ => "arvore-arredondada",
            },
        };
    }

    /// <summary>Perfil usado: o da ficha (se existir na biblioteca), senão o automático; nunca nulo.</summary>
    public static (Plant3DProfile Profile, bool FromSpecies) Resolve(Species species, IReadOnlyList<Plant3DProfile> library)
    {
        ArgumentNullException.ThrowIfNull(species);
        Plant3DProfile? Find(string? id) => string.IsNullOrWhiteSpace(id) ? null : library.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (Find(species.Plant3DProfileId) is Plant3DProfile chosen)
        {
            return (chosen, true);
        }

        Plant3DProfile? automatic = Find(AutomaticId(species));
        if (automatic is not null)
        {
            return (automatic, false);
        }

        // Biblioteca incompleta (perfil apagado): perfil padrão seguro da mesma arquitetura.
        List<Plant3DProfile> defaults = Plant3DProfileDefaults.Create();
        return (defaults.FirstOrDefault(p => p.Id == AutomaticId(species)) ?? defaults[0], false);
    }

    /// <summary>Um perfil de árvore não deve ser usado numa herbácea, e vice-versa, só por coincidência de gênero.</summary>
    private static bool Compatible(string id, PlantCategory category) => category switch
    {
        PlantCategory.Arvore => id is "ipe" or "flamboyant" or "araucaria" or "conifera" || id.StartsWith("arvore", StringComparison.Ordinal),
        PlantCategory.Palmeira => id.StartsWith("palmeira", StringComparison.Ordinal) || id is "jeriva" or "cica" or "ravenala",
        _ => !(id is "ipe" or "flamboyant" or "araucaria" || id.StartsWith("arvore", StringComparison.Ordinal) || id.StartsWith("palmeira", StringComparison.Ordinal) || id == "jeriva"),
    };
}
