using System.Globalization;
using System.Text;
using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Symbols;

namespace Viridian.Library;

/// <summary>Conteúdo do arquivo de catálogo botânico.</summary>
public sealed class SpeciesCatalog
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Versão do catálogo padrão já incorporada a este arquivo.</summary>
    public int SeedVersion { get; set; }

    public List<Species> Species { get; set; } = new();

    /// <summary>Espécies do catálogo padrão que o escritório excluiu (não voltam em atualizações).</summary>
    public List<string> RemovedSeedIds { get; set; } = new();
}

/// <summary>Critérios de pesquisa de espécies (filtros projetuais).</summary>
public sealed class SpeciesFilter
{
    public string Text { get; set; } = string.Empty;

    public PlantCategory? Category { get; set; }

    public PlantOrigin? Origin { get; set; }

    public CrownForm? CrownForm { get; set; }

    public WaterNeed? Water { get; set; }

    public LightNeed? Light { get; set; }

    public GrowthRate? Growth { get; set; }

    public double? MinHeightM { get; set; }

    public double? MaxHeightM { get; set; }

    public double? MinCrownM { get; set; }

    public double? MaxCrownM { get; set; }

    public bool OnlyUserDefined { get; set; }

    /// <summary>Bioma de ocorrência (somente espécies nativas daquele bioma).</summary>
    public Biome? Biome { get; set; }

    /// <summary>Mês de floração (1 a 12).</summary>
    public int? FloweringMonth { get; set; }

    /// <summary>Somente espécies com cor de floração cadastrada (floração vistosa).</summary>
    public bool OnlyShowyFlowers { get; set; }

    public bool Matches(Species species)
    {
        if (Category.HasValue && species.Category != Category.Value)
        {
            return false;
        }

        if (Origin.HasValue && species.Origin != Origin.Value)
        {
            return false;
        }

        if (CrownForm.HasValue && species.CrownForm != CrownForm.Value)
        {
            return false;
        }

        if (Water.HasValue && species.Water != Water.Value)
        {
            return false;
        }

        if (Light.HasValue && species.Light != Light.Value)
        {
            return false;
        }

        if (Growth.HasValue && species.Growth != Growth.Value)
        {
            return false;
        }

        if (MinHeightM.HasValue && species.AdultHeightM < MinHeightM.Value)
        {
            return false;
        }

        if (MaxHeightM.HasValue && species.AdultHeightM > MaxHeightM.Value)
        {
            return false;
        }

        if (MinCrownM.HasValue && species.CrownDiameterM < MinCrownM.Value)
        {
            return false;
        }

        if (MaxCrownM.HasValue && species.CrownDiameterM > MaxCrownM.Value)
        {
            return false;
        }

        if (OnlyUserDefined && !species.IsUserDefined)
        {
            return false;
        }

        if (Biome.HasValue && Biome.Value != Core.Model.Biome.Nenhum && (species.Biomes & Biome.Value) == 0)
        {
            return false;
        }

        if (FloweringMonth.HasValue && !MonthSet.Parse(species.Flowering).Contains(FloweringMonth.Value))
        {
            return false;
        }

        if (OnlyShowyFlowers && string.IsNullOrWhiteSpace(species.FlowerColor))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return true;
        }

        string haystack = TextSearch.Normalize(
            $"{species.Code} {species.PopularName} {species.ScientificName} {species.BotanicalFamily} {species.Notes} {Labels.Of(species.Biomes)}");
        return TextSearch.Normalize(Text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => haystack.Contains(term, StringComparison.Ordinal));
    }
}

/// <summary>Comparação de texto sem acentos e sem distinção de maiúsculas.</summary>
public static class TextSearch
{
    public static string Normalize(string? text)
    {
        string decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Transforma um texto em identificador simples (letras, dígitos e hífens).</summary>
    public static string Slug(string? text)
    {
        var sb = new StringBuilder();
        foreach (char c in Normalize(text))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().Trim('-');
    }
}

/// <summary>Leitura e gravação do catálogo em disco.</summary>
public sealed class CatalogStore
{
    private readonly string _file;

    public CatalogStore(string file)
    {
        _file = file;
    }

    public string File => _file;

    /// <summary>Aviso gerado na última leitura (por exemplo, arquivo corrompido recuperado).</summary>
    public string LastWarning { get; private set; } = string.Empty;

    public SpeciesCatalog Load()
    {
        LastWarning = string.Empty;
        if (!System.IO.File.Exists(_file))
        {
            SpeciesCatalog fresh = SeedCatalog.CreateCatalog();
            TrySave(fresh);
            return fresh;
        }

        try
        {
            string json = System.IO.File.ReadAllText(_file);
            SpeciesCatalog catalog = ViridianJson.Deserialize<SpeciesCatalog>(json)
                ?? throw new InvalidDataException("O arquivo de catálogo está vazio.");
            bool changed = Normalize(catalog);
            changed |= MergeSeed(catalog);
            if (changed)
            {
                TrySave(catalog);
            }

            return catalog;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or NotSupportedException)
        {
            string backup = _file + ".corrompido-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try
            {
                System.IO.File.Copy(_file, backup, overwrite: true);
                LastWarning = $"O catálogo não pôde ser lido e foi recriado a partir do padrão. Uma cópia do arquivo anterior foi salva em:\n{backup}";
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                LastWarning = "O catálogo não pôde ser lido e foi recriado a partir do padrão.";
            }

            ViridianLog.Error("Catálogo ilegível: " + _file, ex);
            SpeciesCatalog fresh = SeedCatalog.CreateCatalog();
            TrySave(fresh);
            return fresh;
        }
    }

    public void Save(SpeciesCatalog catalog)
    {
        SafeFile.WriteAllTextAtomic(_file, ViridianJson.Serialize(catalog));
    }

    private void TrySave(SpeciesCatalog catalog)
    {
        try
        {
            Save(catalog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Biblioteca somente leitura (pasta de rede, por exemplo): segue em memória.
            ViridianLog.Warn("Não foi possível gravar o catálogo em " + _file, ex);
        }
    }

    /// <summary>Corrige registros inválidos. Retorna true quando algo foi alterado.</summary>
    internal static bool Normalize(SpeciesCatalog catalog)
    {
        bool changed = false;
        catalog.Species ??= new List<Species>();
        catalog.RemovedSeedIds ??= new List<string>();

        int before = catalog.Species.Count;
        catalog.Species.RemoveAll(s => s is null);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        catalog.Species.RemoveAll(s => string.IsNullOrWhiteSpace(s.Id) || !seenIds.Add(s.Id));
        changed |= catalog.Species.Count != before;

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Species species in catalog.Species)
        {
            species.ScientificName ??= string.Empty;
            species.PopularName ??= string.Empty;
            species.BotanicalFamily ??= string.Empty;
            species.SupplyUnit = string.IsNullOrWhiteSpace(species.SupplyUnit) ? "muda" : species.SupplyUnit.Trim();
            species.Flowering ??= string.Empty;
            if (species.Flowering.Trim() == "string.Empty")
            {
                species.Flowering = string.Empty; // sobra de uma versão anterior do catálogo padrão
            }
            species.Notes ??= string.Empty;
            species.HatchPatternKey ??= string.Empty;
            species.PhotoFile ??= string.Empty;
            species.CustomPlantingFamilyFile ??= string.Empty;
            species.CustomPlantingTypeName ??= string.Empty;
            species.CustomDetailFamilyFile ??= string.Empty;
            species.CustomDetailTypeName ??= string.Empty;
            species.PhotoCredit ??= string.Empty;
            species.PhotoLicense ??= string.Empty;
            species.PhotoSource ??= string.Empty;
            species.PriceItemCode ??= string.Empty;
            species.ServiceItemCode ??= string.Empty;
            species.FoliageColor = ValidColor(species.FoliageColor);
            species.FlowerColor = ValidColor(species.FlowerColor);

            if (!Enum.IsDefined(species.SymbolStyle))
            {
                species.SymbolStyle = SymbolStyles.Suggest(species.Category, species.CrownForm);
                changed = true;
            }

            if (!(species.CrownDiameterM >= SymbolGenerator.MinDiameter) || species.CrownDiameterM > SymbolGenerator.MaxDiameter)
            {
                species.CrownDiameterM = double.IsFinite(species.CrownDiameterM)
                    ? Math.Clamp(species.CrownDiameterM, SymbolGenerator.MinDiameter, SymbolGenerator.MaxDiameter)
                    : 1.0;
                changed = true;
            }

            if (species.SymbolVariants < 1 || species.SymbolVariants > Species.MaxVariants)
            {
                species.SymbolVariants = Math.Clamp(species.SymbolVariants, 1, Species.MaxVariants);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(species.Code) || !usedCodes.Add(species.Code.Trim()))
            {
                species.Code = CodeFormat.NextSpeciesCode(usedCodes, species.Category);
                usedCodes.Add(species.Code);
                changed = true;
            }
        }

        return changed;
    }

    private static string ValidColor(string? text) =>
        Core.Colors.Rgb.TryParse(text, out Core.Colors.Rgb color) ? color.Hex : string.Empty;

    /// <summary>
    /// Atualiza o catálogo com uma nova versão do catálogo padrão: acrescenta as espécies novas, atualiza as do
    /// padrão que o escritório não alterou e, nas alteradas, apenas completa os campos novos que estiverem vazios.
    /// Códigos, preços e famílias personalizadas do escritório são sempre preservados.
    /// </summary>
    internal static bool MergeSeed(SpeciesCatalog catalog)
    {
        if (catalog.SeedVersion >= SeedCatalog.Version)
        {
            return false;
        }

        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < catalog.Species.Count; i++)
        {
            index[catalog.Species[i].Id] = i;
        }

        var removed = new HashSet<string>(catalog.RemovedSeedIds, StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(catalog.Species.Select(s => s.Code), StringComparer.OrdinalIgnoreCase);
        foreach (Species seed in SeedCatalog.CreateSpecies())
        {
            if (removed.Contains(seed.Id))
            {
                continue;
            }

            if (index.TryGetValue(seed.Id, out int position))
            {
                Species current = catalog.Species[position];
                if (!current.IsUserDefined)
                {
                    seed.Code = current.Code;
                    seed.SymbolSeed = current.SymbolSeed;
                    seed.UnitPrice = current.UnitPrice;
                    seed.PriceItemCode = current.PriceItemCode;
                    seed.ServiceItemCode = current.ServiceItemCode;
                    seed.CustomPlantingFamilyFile = current.CustomPlantingFamilyFile;
                    seed.CustomPlantingTypeName = current.CustomPlantingTypeName;
                    seed.CustomDetailFamilyFile = current.CustomDetailFamilyFile;
                    seed.CustomDetailTypeName = current.CustomDetailTypeName;
                    if (!string.IsNullOrWhiteSpace(current.PhotoFile) && string.IsNullOrWhiteSpace(seed.PhotoFile))
                    {
                        seed.PhotoFile = current.PhotoFile;
                    }

                    catalog.Species[position] = seed;
                }
                else
                {
                    FillMissing(current, seed);

                    // Espécie editada que ainda usa a foto do catálogo: a foto foi revisada, então os créditos acompanham.
                    if (string.Equals(current.PhotoFile, seed.PhotoFile, StringComparison.OrdinalIgnoreCase))
                    {
                        current.PhotoCredit = seed.PhotoCredit;
                        current.PhotoLicense = seed.PhotoLicense;
                        current.PhotoSource = seed.PhotoSource;
                    }
                }

                continue;
            }

            if (!codes.Add(seed.Code))
            {
                seed.Code = CodeFormat.NextSpeciesCode(codes, seed.Category);
                codes.Add(seed.Code);
            }

            catalog.Species.Add(seed);
        }

        catalog.SeedVersion = SeedCatalog.Version;
        return true;
    }

    /// <summary>Completa, em uma espécie alterada pelo escritório, apenas os campos que estão vazios.</summary>
    private static void FillMissing(Species current, Species seed)
    {
        if (current.Biomes == Biome.Nenhum)
        {
            current.Biomes = seed.Biomes;
        }

        if (current.Texture == Texture.NaoInformada)
        {
            current.Texture = seed.Texture;
        }

        if (string.IsNullOrWhiteSpace(current.FoliageColor))
        {
            current.FoliageColor = seed.FoliageColor;
        }

        if (string.IsNullOrWhiteSpace(current.FlowerColor))
        {
            current.FlowerColor = seed.FlowerColor;
        }

        if (string.IsNullOrWhiteSpace(current.PhotoFile))
        {
            current.PhotoFile = seed.PhotoFile;
            current.PhotoCredit = seed.PhotoCredit;
            current.PhotoLicense = seed.PhotoLicense;
            current.PhotoSource = seed.PhotoSource;
        }
    }
}

/// <summary>Operações sobre o catálogo carregado: pesquisa, cadastro, alteração e exclusão.</summary>
public sealed class CatalogService
{
    private readonly CatalogStore _store;
    private SpeciesCatalog _catalog;

    public CatalogService(CatalogStore store)
    {
        _store = store;
        _catalog = store.Load();
    }

    public string File => _store.File;

    public string LoadWarning => _store.LastWarning;

    public IReadOnlyList<Species> All => _catalog.Species;

    public void Reload() => _catalog = _store.Load();

    public Species? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : _catalog.Species.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public Species? FindByCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : _catalog.Species.FirstOrDefault(s => string.Equals(s.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Species> Search(SpeciesFilter filter) =>
        _catalog.Species
            .Where(filter.Matches)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string NextCode(PlantCategory category) =>
        CodeFormat.NextSpeciesCode(_catalog.Species.Select(s => s.Code), category);

    /// <summary>Cria um registro em branco com os padrões da categoria.</summary>
    public Species NewDraft(PlantCategory category)
    {
        return new Species
        {
            Category = category,
            Code = NextCode(category),
            SymbolStyle = SymbolStyles.Suggest(category, CrownForm.Arredondada),
            SymbolVariants = category is PlantCategory.Arvore ? 3 : category is PlantCategory.Palmeira ? 2 : 1,
            HatchPatternKey = HatchLibrary.DefaultKeyFor(category),
            SupplyUnit = category == PlantCategory.Gramado ? "m²" : "muda",
            CrownDiameterM = category switch
            {
                PlantCategory.Arvore => 6,
                PlantCategory.Palmeira => 4,
                PlantCategory.Arbusto => 1.2,
                PlantCategory.Herbacea => 0.6,
                PlantCategory.Trepadeira => 1.0,
                PlantCategory.Aquatica => 0.6,
                _ => 0.3,
            },
            IsUserDefined = true,
        };
    }

    /// <summary>Grava uma espécie nova ou alterada. Lança <see cref="InvalidOperationException"/> com a mensagem do problema.</summary>
    public void Save(Species species)
    {
        ArgumentNullException.ThrowIfNull(species);
        IReadOnlyList<string> problems = SpeciesValidator.Validate(species);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }

        species.Code = (species.Code ?? string.Empty).Trim();
        if (species.Code.Length == 0)
        {
            species.Code = NextCode(species.Category);
        }

        Species? sameCode = _catalog.Species.FirstOrDefault(s =>
            string.Equals(s.Code, species.Code, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(s.Id, species.Id, StringComparison.OrdinalIgnoreCase));
        if (sameCode is not null)
        {
            throw new InvalidOperationException($"O código {species.Code} já pertence a \"{sameCode.PopularName}\".");
        }

        int index = _catalog.Species.FindIndex(s => string.Equals(s.Id, species.Id, StringComparison.OrdinalIgnoreCase));
        species.IsUserDefined = true;
        if (index >= 0)
        {
            _catalog.Species[index] = species;
        }
        else
        {
            _catalog.Species.Add(species);
        }

        _store.Save(_catalog);
    }

    public bool Remove(string id)
    {
        Species? species = Find(id);
        if (species is null)
        {
            return false;
        }

        _catalog.Species.Remove(species);
        if (id.StartsWith(SeedCatalog.IdPrefix, StringComparison.OrdinalIgnoreCase)
            && !_catalog.RemovedSeedIds.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            _catalog.RemovedSeedIds.Add(id);
        }

        _store.Save(_catalog);
        return true;
    }

    /// <summary>Cria uma cópia editável de uma espécie, com novo identificador e código.</summary>
    public Species Duplicate(Species source)
    {
        Species copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Code = NextCode(source.Category);
        copy.PopularName = source.PopularName + " (cópia)";
        copy.SymbolSeed = 0;
        copy.IsUserDefined = true;
        return copy;
    }
}

/// <summary>Leitura e gravação das configurações do escritório.</summary>
public static class SettingsStore
{
    public static ViridianSettings Load(string? file = null)
    {
        string path = file ?? LibraryPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                ViridianSettings? settings = ViridianJson.Deserialize<ViridianSettings>(File.ReadAllText(path));
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            ViridianLog.Error("Configurações ilegíveis: " + path, ex);
        }

        var defaults = new ViridianSettings();
        defaults.Normalize();
        return defaults;
    }

    public static void Save(ViridianSettings settings, string? file = null)
    {
        settings.Normalize();
        SafeFile.WriteAllTextAtomic(file ?? LibraryPaths.SettingsFile, ViridianJson.Serialize(settings));
    }
}
