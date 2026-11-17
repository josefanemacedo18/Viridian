using System.Globalization;
using System.Reflection;
using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Symbols;

namespace Viridian.Library;

/// <summary>Crédito de uma fotografia do catálogo padrão.</summary>
public sealed class PhotoInfo
{
    public string File { get; set; } = string.Empty;

    public string Credit { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Taxon { get; set; } = string.Empty;
}

/// <summary>
/// Catálogo padrão que acompanha o plugin: espécies nativas do Brasil e exóticas de uso corrente no
/// paisagismo brasileiro. Os dados ficam em <c>Data/catalogo-padrao.txt</c> e as fotografias, com autor e
/// licença, em <c>Data/fotos.json</c>. Alturas, copas, espaçamentos e densidades são referências de
/// projeto e devem ser conferidos pelo responsável técnico; todos podem ser alterados no cadastro.
/// </summary>
public static class SeedCatalog
{
    /// <summary>Versão do catálogo padrão. Ao aumentá-la, os catálogos existentes são atualizados.</summary>
    public const int Version = 3;

    public const string IdPrefix = "sp-";

    private const string CatalogResource = "Viridian.Library.Data.catalogo-padrao.txt";
    private const string PhotosResource = "Viridian.Library.Data.fotos.json";

    private static readonly Lazy<IReadOnlyDictionary<string, PhotoInfo>> Photos = new(LoadPhotos);

    public static SpeciesCatalog CreateCatalog() => new()
    {
        SeedVersion = Version,
        Species = CreateSpecies(),
    };

    public static List<Species> CreateSpecies()
    {
        List<Species> list = Parse(ReadResource(CatalogResource));

        // Códigos sequenciais por categoria, na ordem do arquivo.
        var codes = new List<string>();
        foreach (Species species in list)
        {
            species.Code = CodeFormat.NextSpeciesCode(codes, species.Category);
            codes.Add(species.Code);
        }

        return list;
    }

    /// <summary>Créditos das fotografias do catálogo padrão, por identificador de espécie.</summary>
    public static IReadOnlyDictionary<string, PhotoInfo> PhotoCredits => Photos.Value;

    /// <summary>Interpreta o arquivo de dados do catálogo. Linhas inválidas geram exceção com o número da linha.</summary>
    public static List<Species> Parse(string text)
    {
        var list = new List<Species>();
        int lineNumber = 0;
        foreach (string raw in text.Split('\n'))
        {
            lineNumber++;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] f = line.Split('|');
            if (f.Length != 21)
            {
                throw new FormatException($"Catálogo padrão, linha {lineNumber}: esperados 21 campos, encontrados {f.Length}.");
            }

            try
            {
                list.Add(FromFields(f));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new FormatException($"Catálogo padrão, linha {lineNumber} ({f[1]}): {ex.Message}", ex);
            }
        }

        return list;
    }

    private static Species FromFields(string[] f)
    {
        PlantCategory category = Labels.TryCategoryFromPrefix(f[0], out PlantCategory c)
            ? c
            : throw new FormatException("categoria desconhecida: " + f[0]);
        CrownForm form = f[5] switch
        {
            "R" => CrownForm.Arredondada,
            "E" => CrownForm.Espalhada,
            "C" => CrownForm.Colunar,
            "P" => CrownForm.Piramidal,
            "I" => CrownForm.Irregular,
            _ => throw new FormatException("forma desconhecida: " + f[5]),
        };
        string scientific = f[1].Trim();
        string id = IdPrefix + TextSearch.Slug(scientific);
        SymbolStyle style = f[16].Length == 0
            ? SymbolStyles.Suggest(category, form)
            : Enum.Parse<SymbolStyle>(f[16]);

        var species = new Species
        {
            Id = id,
            ScientificName = scientific,
            PopularName = f[2].Trim(),
            BotanicalFamily = f[3].Trim(),
            Category = category,
            Origin = f[4] switch
            {
                "N" => PlantOrigin.Nativa,
                "E" => PlantOrigin.Exotica,
                "I" => PlantOrigin.ExoticaInvasora,
                _ => throw new FormatException("origem desconhecida: " + f[4]),
            },
            CrownForm = form,
            AdultHeightM = Number(f[6]),
            CrownDiameterM = Number(f[7]),
            PlantingHeightM = Number(f[8]),
            RecommendedSpacingM = Number(f[9]),
            Growth = f[10] switch { "L" => GrowthRate.Lento, "M" => GrowthRate.Medio, "R" => GrowthRate.Rapido, _ => throw new FormatException("crescimento: " + f[10]) },
            Water = f[11] switch { "B" => WaterNeed.Baixa, "M" => WaterNeed.Media, "A" => WaterNeed.Alta, _ => throw new FormatException("água: " + f[11]) },
            Light = f[12] switch { "S" => LightNeed.SolPleno, "MS" => LightNeed.MeiaSombra, "SO" => LightNeed.Sombra, _ => throw new FormatException("luz: " + f[12]) },
            Flowering = f[13].Trim(),
            DensityPerM2 = f[14].Length == 0 ? 0 : Number(f[14]),
            SupplyUnit = f[15].Length == 0 ? "muda" : f[15],
            SymbolStyle = style,
            Biomes = ParseBiomes(f[17]),
            FoliageColor = f[18].Trim(),
            FlowerColor = f[19].Trim(),
            Notes = f[20].Trim(),
            SymbolVariants = category == PlantCategory.Arvore ? 3 : category == PlantCategory.Palmeira ? 2 : 1,
            HatchPatternKey = HatchLibrary.DefaultKeyFor(category),
            IsUserDefined = false,
        };

        // Textura: proposta inicial pela categoria, forma da copa e gênero (editável no cadastro).
        species.Texture = Core.Composition.TextureRules.Suggest(species);

        if (PhotoCredits.TryGetValue(id, out PhotoInfo? photo))
        {
            species.PhotoFile = photo.File;
            species.PhotoCredit = photo.Credit;
            species.PhotoLicense = photo.License;
            species.PhotoSource = photo.Source;
        }

        return species;
    }

    public static Biome ParseBiomes(string text)
    {
        Biome result = Biome.Nenhum;
        foreach (char ch in text.Trim())
        {
            result |= ch switch
            {
                'A' => Biome.Amazonia,
                'C' => Biome.Caatinga,
                'E' => Biome.Cerrado,
                'M' => Biome.MataAtlantica,
                'P' => Biome.Pampa,
                'N' => Biome.Pantanal,
                _ => throw new FormatException("bioma desconhecido: " + ch),
            };
        }

        return result;
    }

    private static double Number(string text) =>
        double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string ReadResource(string name)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static IReadOnlyDictionary<string, PhotoInfo> LoadPhotos()
    {
        try
        {
            string json = ReadResource(PhotosResource);
            if (json.Length > 0)
            {
                Dictionary<string, PhotoInfo>? photos = ViridianJson.Deserialize<Dictionary<string, PhotoInfo>>(json);
                if (photos is not null)
                {
                    return new Dictionary<string, PhotoInfo>(photos, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            ViridianLog.Warn("Créditos de fotografias ilegíveis.", ex);
        }

        return new Dictionary<string, PhotoInfo>(StringComparer.OrdinalIgnoreCase);
    }
}
