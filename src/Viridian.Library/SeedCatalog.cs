using Viridian.Core.Coding;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Symbols;

namespace Viridian.Library;

/// <summary>
/// Catálogo padrão que acompanha o plugin: espécies de uso corrente no paisagismo brasileiro.
/// Alturas, copas, espaçamentos e densidades são valores de referência para projeto e devem
/// ser conferidos pelo responsável técnico; todos podem ser alterados no cadastro.
/// </summary>
public static class SeedCatalog
{
    /// <summary>Versão do catálogo padrão. Ao aumentá-la, as espécies novas são acrescentadas aos catálogos existentes.</summary>
    public const int Version = 1;

    public const string IdPrefix = "sp-";

    private const PlantOrigin Nat = PlantOrigin.Nativa;
    private const PlantOrigin Exo = PlantOrigin.Exotica;
    private const PlantOrigin Inv = PlantOrigin.ExoticaInvasora;

    private const GrowthRate Slow = GrowthRate.Lento;
    private const GrowthRate Mid = GrowthRate.Medio;
    private const GrowthRate Fast = GrowthRate.Rapido;

    private const WaterNeed Low = WaterNeed.Baixa;
    private const WaterNeed Med = WaterNeed.Media;
    private const WaterNeed High = WaterNeed.Alta;

    private const LightNeed Sun = LightNeed.SolPleno;
    private const LightNeed Half = LightNeed.MeiaSombra;
    private const LightNeed Shade = LightNeed.Sombra;

    public static SpeciesCatalog CreateCatalog() => new()
    {
        SeedVersion = Version,
        Species = CreateSpecies(),
    };

    public static List<Species> CreateSpecies()
    {
        var list = new List<Species>();
        Trees(list);
        Palms(list);
        Shrubs(list);
        Herbaceous(list);
        Groundcovers(list);
        Grasses(list);
        Climbers(list);
        Aquatics(list);

        // Códigos sequenciais por categoria, na ordem de cadastro.
        var codes = new List<string>();
        foreach (Species species in list)
        {
            species.Code = CodeFormat.NextSpeciesCode(codes, species.Category);
            codes.Add(species.Code);
        }

        return list;
    }

    private static void Trees(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Arvore;
        Add(l, c, "Handroanthus chrysotrichus", "Ipê-amarelo", "Bignoniaceae", Nat, CrownForm.Arredondada, 8, 5, 2.0, 5, Mid, Med, Sun, "ago–set");
        Add(l, c, "Handroanthus impetiginosus", "Ipê-roxo", "Bignoniaceae", Nat, CrownForm.Arredondada, 12, 8, 2.0, 8, Mid, Med, Sun, "jun–ago");
        Add(l, c, "Tabebuia roseoalba", "Ipê-branco", "Bignoniaceae", Nat, CrownForm.Arredondada, 10, 6, 2.0, 6, Mid, Med, Sun, "ago–out");
        Add(l, c, "Cenostigma pluviosum", "Sibipiruna", "Fabaceae", Nat, CrownForm.Espalhada, 14, 12, 2.5, 10, Fast, Med, Sun, "ago–nov", notes: "Sin. Caesalpinia pluviosa var. peltophoroides.");
        Add(l, c, "Paubrasilia echinata", "Pau-brasil", "Fabaceae", Nat, CrownForm.Arredondada, 12, 7, 2.0, 7, Slow, Med, Sun, "set–out", notes: "Sin. Caesalpinia echinata. Espécie ameaçada; verificar procedência das mudas.");
        Add(l, c, "Libidibia ferrea", "Pau-ferro", "Fabaceae", Nat, CrownForm.Espalhada, 15, 12, 2.5, 10, Mid, Med, Sun, "nov–fev", notes: "Sin. Caesalpinia ferrea.");
        Add(l, c, "Pleroma granulosum", "Quaresmeira", "Melastomataceae", Nat, CrownForm.Arredondada, 9, 6, 2.0, 6, Fast, Med, Sun, "jan–abr e jun–ago", notes: "Sin. Tibouchina granulosa.");
        Add(l, c, "Schinus terebinthifolia", "Aroeira-pimenteira", "Anacardiaceae", Nat, CrownForm.Irregular, 8, 7, 1.8, 6, Fast, Low, Sun, "set–jan");
        Add(l, c, "Schinus molle", "Aroeira-salsa", "Anacardiaceae", Nat, CrownForm.Irregular, 8, 7, 1.8, 6, Fast, Low, Sun, "ago–nov");
        Add(l, c, "Bauhinia forficata", "Pata-de-vaca", "Fabaceae", Nat, CrownForm.Irregular, 7, 5, 1.8, 5, Fast, Med, Sun, "out–jan");
        Add(l, c, "Erythrina speciosa", "Eritrina-candelabro", "Fabaceae", Nat, CrownForm.Irregular, 5, 4, 1.8, 4, Fast, High, Sun, "jun–set");
        Add(l, c, "Ceiba speciosa", "Paineira", "Malvaceae", Nat, CrownForm.Espalhada, 20, 15, 2.5, 15, Fast, Med, Sun, "fev–mai", notes: "Sin. Chorisia speciosa. Tronco com acúleos.");
        Add(l, c, "Moquilea tomentosa", "Oiti", "Chrysobalanaceae", Nat, CrownForm.Arredondada, 12, 10, 2.5, 8, Mid, Med, Sun, "jun–ago", notes: "Sin. Licania tomentosa.");
        Add(l, c, "Eugenia uniflora", "Pitangueira", "Myrtaceae", Nat, CrownForm.Arredondada, 6, 4, 1.5, 4, Mid, Med, Sun, "ago–nov");
        Add(l, c, "Plinia cauliflora", "Jabuticabeira", "Myrtaceae", Nat, CrownForm.Arredondada, 7, 5, 1.5, 5, Slow, High, Sun, "jul–set e nov–dez");
        Add(l, c, "Araucaria angustifolia", "Araucária", "Araucariaceae", Nat, CrownForm.Piramidal, 25, 10, 1.5, 10, Mid, Med, Sun, string.Empty, notes: "Espécie ameaçada; corte regulado por legislação específica.");
        Add(l, c, "Triplaris americana", "Pau-formiga", "Polygonaceae", Nat, CrownForm.Colunar, 15, 5, 2.0, 5, Fast, Med, Sun, "ago–out");
        Add(l, c, "Cordia superba", "Babosa-branca", "Boraginaceae", Nat, CrownForm.Arredondada, 8, 6, 1.8, 6, Mid, Med, Sun, "out–fev");
        Add(l, c, "Hymenaea courbaril", "Jatobá", "Fabaceae", Nat, CrownForm.Espalhada, 18, 14, 2.0, 12, Slow, Med, Sun, "out–dez");
        Add(l, c, "Cariniana legalis", "Jequitibá-rosa", "Lecythidaceae", Nat, CrownForm.Arredondada, 30, 14, 2.0, 14, Slow, Med, Sun, "dez–fev");
        Add(l, c, "Peltophorum dubium", "Canafístula", "Fabaceae", Nat, CrownForm.Espalhada, 18, 12, 2.0, 10, Fast, Med, Sun, "dez–fev");
        Add(l, c, "Lafoensia pacari", "Dedaleiro", "Lythraceae", Nat, CrownForm.Arredondada, 8, 5, 1.8, 5, Mid, Low, Sun, "ago–nov");
        Add(l, c, "Bixa orellana", "Urucum", "Bixaceae", Nat, CrownForm.Arredondada, 4, 3.5, 1.2, 3.5, Fast, Med, Sun, "jan–mar");
        Add(l, c, "Jacaranda mimosifolia", "Jacarandá-mimoso", "Bignoniaceae", Exo, CrownForm.Espalhada, 13, 10, 2.0, 9, Fast, Med, Sun, "ago–nov");
        Add(l, c, "Delonix regia", "Flamboyant", "Fabaceae", Exo, CrownForm.Espalhada, 10, 14, 2.5, 12, Fast, Low, Sun, "out–jan", notes: "Raízes superficiais vigorosas; manter afastado de pavimentos e redes.");
        Add(l, c, "Lagerstroemia indica", "Resedá", "Lythraceae", Exo, CrownForm.Arredondada, 5, 4, 1.8, 4, Mid, Med, Sun, "nov–mar");
        Add(l, c, "Magnolia grandiflora", "Magnólia-branca", "Magnoliaceae", Exo, CrownForm.Piramidal, 15, 8, 2.0, 8, Slow, Med, Sun, "set–dez");
        Add(l, c, "Cassia fistula", "Chuva-de-ouro", "Fabaceae", Exo, CrownForm.Arredondada, 10, 7, 2.0, 7, Mid, Med, Sun, "nov–jan");
        Add(l, c, "Tipuana tipu", "Tipuana", "Fabaceae", Exo, CrownForm.Espalhada, 15, 15, 2.5, 12, Fast, Med, Sun, "set–dez");
        Add(l, c, "Mangifera indica", "Mangueira", "Anacardiaceae", Exo, CrownForm.Arredondada, 15, 12, 1.5, 10, Mid, Med, Sun, "jul–out");
        Add(l, c, "Podocarpus macrophyllus", "Podocarpo", "Podocarpaceae", Exo, CrownForm.Colunar, 6, 2, 1.5, 1.5, Slow, Med, Sun, string.Empty);
        Add(l, c, "Cupressus sempervirens", "Cipreste-italiano", "Cupressaceae", Exo, CrownForm.Colunar, 15, 1.8, 2.0, 1.5, Mid, Low, Sun, string.Empty);
        Add(l, c, "Tecoma stans", "Ipê-de-jardim", "Bignoniaceae", Inv, CrownForm.Arredondada, 5, 4, 1.5, 4, Fast, Low, Sun, "ano todo", notes: "Exótica invasora: evitar o plantio; consta em listas oficiais de espécies invasoras.");
    }

    private static void Palms(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Palmeira;
        Add(l, c, "Syagrus romanzoffiana", "Jerivá", "Arecaceae", Nat, CrownForm.Arredondada, 12, 5, 3.0, 4, Mid, Med, Sun, "set–mar");
        Add(l, c, "Euterpe edulis", "Palmito-juçara", "Arecaceae", Nat, CrownForm.Arredondada, 10, 3.5, 1.5, 3, Slow, High, Half, "set–dez", notes: "Espécie ameaçada; verificar procedência das mudas.");
        Add(l, c, "Euterpe oleracea", "Açaizeiro", "Arecaceae", Nat, CrownForm.Arredondada, 12, 4, 1.5, 4, Mid, High, Sun, "ano todo", notes: "Forma touceira.");
        Add(l, c, "Acrocomia aculeata", "Macaúba", "Arecaceae", Nat, CrownForm.Arredondada, 12, 5, 2.0, 5, Mid, Low, Sun, "out–jan", notes: "Estipe com espinhos.");
        Add(l, c, "Butia capitata", "Butiá", "Arecaceae", Nat, CrownForm.Arredondada, 5, 4, 1.5, 4, Slow, Low, Sun, "set–jan");
        Add(l, c, "Roystonea oleracea", "Palmeira-imperial", "Arecaceae", Exo, CrownForm.Arredondada, 30, 6, 4.0, 6, Mid, Med, Sun, string.Empty);
        Add(l, c, "Roystonea regia", "Palmeira-real", "Arecaceae", Exo, CrownForm.Arredondada, 20, 6, 4.0, 6, Mid, Med, Sun, string.Empty);
        Add(l, c, "Dypsis lutescens", "Areca-bambu", "Arecaceae", Exo, CrownForm.Arredondada, 6, 4, 2.0, 3, Mid, Med, Half, string.Empty, notes: "Sin. Chrysalidocarpus lutescens. Forma touceira.");
        Add(l, c, "Phoenix roebelenii", "Fênix", "Arecaceae", Exo, CrownForm.Arredondada, 3, 2.5, 1.2, 2.5, Slow, Med, Sun, string.Empty, notes: "Espinhos na base das folhas.");
        Add(l, c, "Phoenix canariensis", "Tamareira-das-canárias", "Arecaceae", Exo, CrownForm.Arredondada, 15, 8, 3.0, 8, Slow, Low, Sun, string.Empty);
        Add(l, c, "Wodyetia bifurcata", "Palmeira-rabo-de-raposa", "Arecaceae", Exo, CrownForm.Arredondada, 10, 4, 3.0, 4, Fast, Med, Sun, string.Empty);
        Add(l, c, "Livistona chinensis", "Palmeira-leque-da-china", "Arecaceae", Exo, CrownForm.Arredondada, 10, 4, 2.0, 4, Slow, Med, Sun, string.Empty, style: SymbolStyle.PalmeiraLeque);
        Add(l, c, "Licuala grandis", "Licuala", "Arecaceae", Exo, CrownForm.Arredondada, 3, 2, 1.0, 2, Slow, High, Half, string.Empty, style: SymbolStyle.PalmeiraLeque);
        Add(l, c, "Rhapis excelsa", "Palmeira-ráfis", "Arecaceae", Exo, CrownForm.Arredondada, 3, 1.5, 1.0, 1.2, Slow, Med, Half, string.Empty, style: SymbolStyle.PalmeiraLeque, notes: "Forma touceira.");
        Add(l, c, "Archontophoenix cunninghamiana", "Palmeira-seafórtia", "Arecaceae", Inv, CrownForm.Arredondada, 15, 4, 3.0, 4, Fast, Med, Sun, string.Empty, notes: "Exótica invasora da Mata Atlântica: evitar o plantio.");
    }

    private static void Shrubs(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Arbusto;
        Add(l, c, "Clusia fluminensis", "Clúsia", "Clusiaceae", Nat, CrownForm.Arredondada, 3, 2.5, 0.6, 1.0, Mid, Low, Sun, "set–jan");
        Add(l, c, "Calliandra brevipes", "Esponjinha", "Fabaceae", Nat, CrownForm.Arredondada, 2, 2, 0.5, 1.2, Mid, Med, Sun, "out–mar");
        Add(l, c, "Brunfelsia uniflora", "Manacá-de-cheiro", "Solanaceae", Nat, CrownForm.Arredondada, 2.5, 1.8, 0.6, 1.5, Mid, Med, Half, "set–dez");
        Add(l, c, "Lantana camara", "Lantana", "Verbenaceae", Nat, CrownForm.Arredondada, 1.2, 1.2, 0.3, 0.8, Fast, Low, Sun, "ano todo", notes: "Frutos tóxicos.");
        Add(l, c, "Ixora coccinea", "Ixora", "Rubiaceae", Exo, CrownForm.Arredondada, 2, 1.5, 0.4, 0.8, Mid, Med, Sun, "ano todo");
        Add(l, c, "Buxus sempervirens", "Buxinho", "Buxaceae", Exo, CrownForm.Arredondada, 1.5, 1.0, 0.3, 0.3, Slow, Med, Sun, string.Empty, notes: "Indicado para cercas vivas e topiaria.");
        Add(l, c, "Duranta erecta", "Pingo-de-ouro", "Verbenaceae", Exo, CrownForm.Arredondada, 1.5, 1.2, 0.3, 0.3, Fast, Med, Sun, "ano todo", notes: "Nativa das Américas e naturalizada no Brasil. Frutos tóxicos.");
        Add(l, c, "Hibiscus rosa-sinensis", "Hibisco", "Malvaceae", Exo, CrownForm.Arredondada, 3, 2, 0.6, 1.2, Fast, Med, Sun, "ano todo");
        Add(l, c, "Rhododendron simsii", "Azaleia", "Ericaceae", Exo, CrownForm.Arredondada, 1.5, 1.5, 0.4, 1.0, Slow, Med, Half, "jun–set");
        Add(l, c, "Gardenia jasminoides", "Gardênia", "Rubiaceae", Exo, CrownForm.Arredondada, 1.5, 1.2, 0.4, 1.0, Slow, Med, Half, "set–dez");
        Add(l, c, "Plumbago auriculata", "Bela-emília", "Plumbaginaceae", Exo, CrownForm.Irregular, 1.5, 1.5, 0.4, 1.0, Fast, Low, Sun, "set–mai");
        Add(l, c, "Russelia equisetiformis", "Russélia", "Plantaginaceae", Exo, CrownForm.Irregular, 1.0, 1.2, 0.3, 0.8, Fast, Low, Sun, "ano todo");
        Add(l, c, "Codiaeum variegatum", "Cróton", "Euphorbiaceae", Exo, CrownForm.Arredondada, 2, 1.2, 0.4, 0.8, Mid, Med, Sun, string.Empty);
        Add(l, c, "Pittosporum tobira", "Pitósporo", "Pittosporaceae", Exo, CrownForm.Arredondada, 2.5, 2, 0.5, 1.2, Mid, Low, Sun, "set–nov");
        Add(l, c, "Loropetalum chinense", "Loropétalo", "Hamamelidaceae", Exo, CrownForm.Arredondada, 2, 1.5, 0.4, 1.0, Mid, Med, Sun, "ago–out");
        Add(l, c, "Nandina domestica", "Nandina", "Berberidaceae", Exo, CrownForm.Arredondada, 1.5, 1.0, 0.4, 0.8, Mid, Med, Half, "set–nov");
        Add(l, c, "Agave attenuata", "Agave-dragão", "Asparagaceae", Exo, CrownForm.Arredondada, 1.2, 1.2, 0.4, 1.0, Slow, Low, Sun, string.Empty, style: SymbolStyle.ArbustoPontiagudo);
        Add(l, c, "Cordyline fruticosa", "Cordiline", "Asparagaceae", Exo, CrownForm.Arredondada, 2, 1.0, 0.5, 0.8, Mid, Med, Half, string.Empty, style: SymbolStyle.ArbustoPontiagudo);
    }

    private static void Herbaceous(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Herbacea;
        Add(l, c, "Heliconia psittacorum", "Helicônia-papagaio", "Heliconiaceae", Nat, CrownForm.Arredondada, 1.2, 0.8, 0.4, 0.6, Fast, High, Half, "ano todo", density: 4);
        Add(l, c, "Neomarica caerulea", "Falso-íris", "Iridaceae", Nat, CrownForm.Arredondada, 0.9, 0.7, 0.3, 0.5, Mid, Med, Half, "set–mar", density: 5);
        Add(l, c, "Thaumatophyllum bipinnatifidum", "Guaimbê", "Araceae", Nat, CrownForm.Arredondada, 2.0, 2.5, 0.5, 1.5, Mid, Med, Half, string.Empty, style: SymbolStyle.ArbustoNuvem, notes: "Sin. Philodendron bipinnatifidum. Planta tóxica se ingerida.");
        Add(l, c, "Alcantarea imperialis", "Bromélia-imperial", "Bromeliaceae", Nat, CrownForm.Arredondada, 1.5, 1.5, 0.4, 1.2, Slow, Low, Sun, string.Empty, style: SymbolStyle.ArbustoPontiagudo);
        Add(l, c, "Aechmea blanchetiana", "Bromélia-porto-seguro", "Bromeliaceae", Nat, CrownForm.Arredondada, 0.9, 0.9, 0.3, 0.7, Mid, Low, Sun, string.Empty, style: SymbolStyle.ArbustoPontiagudo);
        Add(l, c, "Goeppertia zebrina", "Maranta-zebrada", "Marantaceae", Nat, CrownForm.Arredondada, 0.8, 0.6, 0.3, 0.5, Mid, High, Shade, string.Empty, density: 5, notes: "Sin. Calathea zebrina.");
        Add(l, c, "Ctenanthe setosa", "Maranta-cinza", "Marantaceae", Nat, CrownForm.Arredondada, 1.0, 0.8, 0.3, 0.6, Mid, High, Shade, string.Empty, density: 4);
        Add(l, c, "Canna indica", "Cana-da-índia", "Cannaceae", Nat, CrownForm.Arredondada, 1.5, 0.6, 0.3, 0.5, Fast, High, Sun, "ano todo", density: 5);
        Add(l, c, "Cortaderia selloana", "Capim-dos-pampas", "Poaceae", Nat, CrownForm.Arredondada, 2.5, 1.5, 0.5, 1.5, Fast, Low, Sun, "fev–mai");
        Add(l, c, "Strelitzia reginae", "Estrelítzia", "Strelitziaceae", Exo, CrownForm.Arredondada, 1.2, 1.0, 0.4, 0.8, Slow, Med, Sun, "ano todo");
        Add(l, c, "Agapanthus africanus", "Agapanto", "Amaryllidaceae", Exo, CrownForm.Arredondada, 0.6, 0.5, 0.2, 0.4, Mid, Med, Sun, "out–jan", density: 6);
        Add(l, c, "Dietes bicolor", "Moreia", "Iridaceae", Exo, CrownForm.Arredondada, 0.7, 0.6, 0.3, 0.5, Mid, Low, Sun, "set–mar", density: 5);
        Add(l, c, "Hemerocallis × hybrida", "Hemerocale", "Asphodelaceae", Exo, CrownForm.Arredondada, 0.6, 0.5, 0.2, 0.4, Mid, Med, Sun, "set–fev", density: 6);
        Add(l, c, "Alpinia purpurata", "Alpínia", "Zingiberaceae", Exo, CrownForm.Arredondada, 2.0, 1.2, 0.5, 1.0, Fast, High, Half, "ano todo");
        Add(l, c, "Monstera deliciosa", "Costela-de-adão", "Araceae", Exo, CrownForm.Arredondada, 2.0, 2.0, 0.5, 1.2, Mid, Med, Half, string.Empty, style: SymbolStyle.ArbustoNuvem, notes: "Planta tóxica se ingerida.");
        Add(l, c, "Liriope muscari", "Liríope", "Asparagaceae", Exo, CrownForm.Arredondada, 0.3, 0.3, 0.15, 0.25, Mid, Med, Half, "dez–mar", density: 16);
        Add(l, c, "Asparagus densiflorus", "Aspargo-pluma", "Asparagaceae", Exo, CrownForm.Arredondada, 0.6, 0.6, 0.2, 0.5, Mid, Med, Half, string.Empty, density: 5);
        Add(l, c, "Phormium tenax", "Fórmio", "Asphodelaceae", Exo, CrownForm.Arredondada, 1.5, 1.2, 0.4, 1.0, Mid, Med, Sun, string.Empty, style: SymbolStyle.ArbustoPontiagudo);
    }

    private static void Groundcovers(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Forracao;
        Add(l, c, "Arachis repens", "Grama-amendoim", "Fabaceae", Nat, CrownForm.Arredondada, 0.2, 0.3, 0.1, 0.25, Fast, Med, Sun, "ano todo", density: 16);
        Add(l, c, "Sphagneticola trilobata", "Vedélia", "Asteraceae", Nat, CrownForm.Arredondada, 0.3, 0.4, 0.1, 0.3, Fast, Med, Sun, "ano todo", density: 12);
        Add(l, c, "Evolvulus glomeratus", "Azulzinha", "Convolvulaceae", Nat, CrownForm.Arredondada, 0.3, 0.4, 0.1, 0.3, Mid, Low, Sun, "ano todo", density: 12);
        Add(l, c, "Pilea microphylla", "Brilhantina", "Urticaceae", Nat, CrownForm.Arredondada, 0.2, 0.3, 0.1, 0.25, Fast, Med, Half, string.Empty, density: 16);
        Add(l, c, "Callisia repens", "Dinheiro-em-penca", "Commelinaceae", Nat, CrownForm.Arredondada, 0.15, 0.3, 0.05, 0.25, Fast, Med, Half, string.Empty, density: 16);
        Add(l, c, "Ophiopogon japonicus", "Grama-preta", "Asparagaceae", Exo, CrownForm.Arredondada, 0.2, 0.2, 0.1, 0.2, Slow, Med, Half, string.Empty, density: 25);
        Add(l, c, "Tradescantia zebrina", "Lambari", "Commelinaceae", Exo, CrownForm.Arredondada, 0.2, 0.3, 0.1, 0.25, Fast, Med, Half, string.Empty, density: 16);
        Add(l, c, "Tradescantia pallida", "Trapoeraba-roxa", "Commelinaceae", Exo, CrownForm.Arredondada, 0.3, 0.4, 0.1, 0.3, Fast, Low, Sun, string.Empty, density: 12);
        Add(l, c, "Chlorophytum comosum", "Clorofito", "Asparagaceae", Exo, CrownForm.Arredondada, 0.3, 0.3, 0.1, 0.25, Fast, Med, Half, string.Empty, density: 16);
        Add(l, c, "Ajuga reptans", "Ajuga", "Lamiaceae", Exo, CrownForm.Arredondada, 0.15, 0.3, 0.05, 0.25, Mid, High, Half, "ago–nov", density: 16);
        Add(l, c, "Bulbine frutescens", "Bulbine", "Asphodelaceae", Exo, CrownForm.Arredondada, 0.4, 0.4, 0.15, 0.35, Fast, Low, Sun, "ano todo", density: 9);
    }

    private static void Grasses(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Gramado;
        Add(l, c, "Axonopus compressus", "Grama-são-carlos", "Poaceae", Nat, CrownForm.Arredondada, 0.1, 0.3, 0.05, 0, Mid, High, Half, string.Empty, unit: "m²");
        Add(l, c, "Paspalum notatum", "Grama-batatais", "Poaceae", Nat, CrownForm.Arredondada, 0.15, 0.3, 0.05, 0, Mid, Low, Sun, string.Empty, unit: "m²");
        Add(l, c, "Stenotaphrum secundatum", "Grama-santo-agostinho", "Poaceae", Nat, CrownForm.Arredondada, 0.15, 0.3, 0.05, 0, Mid, Med, Half, string.Empty, unit: "m²");
        Add(l, c, "Zoysia japonica", "Grama-esmeralda", "Poaceae", Exo, CrownForm.Arredondada, 0.1, 0.3, 0.05, 0, Mid, Med, Sun, string.Empty, unit: "m²");
        Add(l, c, "Cynodon dactylon", "Grama-bermuda", "Poaceae", Exo, CrownForm.Arredondada, 0.1, 0.3, 0.05, 0, Fast, Med, Sun, string.Empty, unit: "m²");
    }

    private static void Climbers(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Trepadeira;
        Add(l, c, "Bougainvillea spectabilis", "Primavera", "Nyctaginaceae", Nat, CrownForm.Irregular, 5, 3, 0.8, 2.5, Fast, Low, Sun, "ano todo", notes: "Ramos com espinhos.");
        Add(l, c, "Pyrostegia venusta", "Cipó-de-são-joão", "Bignoniaceae", Nat, CrownForm.Irregular, 5, 2.5, 0.5, 2.0, Fast, Low, Sun, "mai–set");
        Add(l, c, "Allamanda cathartica", "Alamanda-amarela", "Apocynaceae", Nat, CrownForm.Irregular, 4, 2.5, 0.5, 2.0, Fast, Med, Sun, "ano todo", notes: "Látex tóxico.");
        Add(l, c, "Petrea volubilis", "Flor-de-são-miguel", "Verbenaceae", Nat, CrownForm.Irregular, 6, 2.5, 0.5, 2.5, Mid, Med, Sun, "ago–nov");
        Add(l, c, "Mandevilla sanderi", "Dipladênia", "Apocynaceae", Nat, CrownForm.Irregular, 3, 1.0, 0.4, 0.8, Mid, Med, Sun, "set–mai", notes: "Látex tóxico.");
        Add(l, c, "Passiflora edulis", "Maracujá", "Passifloraceae", Nat, CrownForm.Irregular, 5, 2.5, 0.4, 2.5, Fast, Med, Sun, "set–mai");
        Add(l, c, "Thunbergia grandiflora", "Tumbérgia-azul", "Acanthaceae", Exo, CrownForm.Irregular, 6, 3, 0.5, 2.5, Fast, Med, Sun, "ano todo");
        Add(l, c, "Trachelospermum jasminoides", "Jasmim-estrela", "Apocynaceae", Exo, CrownForm.Irregular, 5, 2, 0.5, 1.5, Mid, Med, Sun, "set–dez");
        Add(l, c, "Ficus pumila", "Unha-de-gato", "Moraceae", Exo, CrownForm.Irregular, 4, 1.5, 0.3, 0.8, Fast, Med, Half, string.Empty);
    }

    private static void Aquatics(List<Species> l)
    {
        const PlantCategory c = PlantCategory.Aquatica;
        Add(l, c, "Victoria amazonica", "Vitória-régia", "Nymphaeaceae", Nat, CrownForm.Arredondada, 0.1, 1.8, 0.1, 3.0, Fast, High, Sun, "ano todo");
        Add(l, c, "Nymphoides humboldtiana", "Estrela-branca", "Menyanthaceae", Nat, CrownForm.Arredondada, 0.1, 0.5, 0.1, 0.5, Fast, High, Sun, "ano todo");
        Add(l, c, "Pontederia crassipes", "Aguapé", "Pontederiaceae", Nat, CrownForm.Arredondada, 0.3, 0.4, 0.1, 0.4, Fast, High, Sun, "ano todo", notes: "Sin. Eichhornia crassipes. Crescimento agressivo: exige controle.");
        Add(l, c, "Pistia stratiotes", "Alface-d'água", "Araceae", Nat, CrownForm.Arredondada, 0.15, 0.2, 0.05, 0.2, Fast, High, Sun, string.Empty, notes: "Crescimento agressivo: exige controle.");
        Add(l, c, "Pontederia cordata", "Rainha-dos-lagos", "Pontederiaceae", Nat, CrownForm.Arredondada, 0.8, 0.5, 0.3, 0.4, Fast, High, Sun, "set–mar", style: SymbolStyle.Herbacea);
        Add(l, c, "Cyperus giganteus", "Papiro-brasileiro", "Cyperaceae", Nat, CrownForm.Arredondada, 2.0, 1.0, 0.5, 0.8, Fast, High, Sun, string.Empty, style: SymbolStyle.Herbacea);
        Add(l, c, "Typha domingensis", "Taboa", "Typhaceae", Nat, CrownForm.Arredondada, 2.0, 0.5, 0.5, 0.4, Fast, High, Sun, string.Empty, style: SymbolStyle.Herbacea);
        Add(l, c, "Echinodorus grandiflorus", "Chapéu-de-couro", "Alismataceae", Nat, CrownForm.Arredondada, 1.0, 0.8, 0.3, 0.6, Mid, High, Sun, "set–mar", style: SymbolStyle.Herbacea);
        Add(l, c, "Thalia geniculata", "Tália", "Marantaceae", Nat, CrownForm.Arredondada, 2.0, 1.0, 0.5, 0.8, Fast, High, Sun, "nov–mar", style: SymbolStyle.Herbacea);
        Add(l, c, "Nymphaea caerulea", "Ninfeia-azul", "Nymphaeaceae", Exo, CrownForm.Arredondada, 0.1, 1.0, 0.1, 1.0, Mid, High, Sun, "set–mar");
        Add(l, c, "Cyperus papyrus", "Papiro", "Cyperaceae", Exo, CrownForm.Arredondada, 2.0, 1.0, 0.5, 0.8, Fast, High, Sun, string.Empty, style: SymbolStyle.Herbacea);
    }

    private static void Add(
        List<Species> list,
        PlantCategory category,
        string scientific,
        string popular,
        string family,
        PlantOrigin origin,
        CrownForm form,
        double height,
        double crown,
        double plantingHeight,
        double spacing,
        GrowthRate growth,
        WaterNeed water,
        LightNeed light,
        string flowering,
        double density = 0,
        string unit = "muda",
        SymbolStyle? style = null,
        string notes = "")
    {
        list.Add(new Species
        {
            Id = IdPrefix + TextSearch.Slug(scientific),
            ScientificName = scientific,
            PopularName = popular,
            BotanicalFamily = family,
            Category = category,
            Origin = origin,
            CrownForm = form,
            AdultHeightM = height,
            CrownDiameterM = crown,
            PlantingHeightM = plantingHeight,
            RecommendedSpacingM = spacing,
            DensityPerM2 = density,
            SupplyUnit = unit,
            Growth = growth,
            Water = water,
            Light = light,
            Flowering = flowering,
            Notes = notes,
            SymbolStyle = style ?? SymbolStyles.Suggest(category, form),
            SymbolVariants = category == PlantCategory.Arvore ? 3 : category == PlantCategory.Palmeira ? 2 : 1,
            HatchPatternKey = HatchLibrary.DefaultKeyFor(category),
            IsUserDefined = false,
        });
    }
}
