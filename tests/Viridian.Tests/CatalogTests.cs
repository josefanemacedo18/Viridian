using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "viridian-tests-" + Guid.NewGuid().ToString("N"));

    public CatalogTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string CatalogFile => Path.Combine(_folder, "Dados", "catalogo.json");

    [Fact]
    public void SeedCatalogFile_HasNoProgrammingLeftovers()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Viridian.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        string text = File.ReadAllText(Path.Combine(root!, "src", "Viridian.Library", "Data", "catalogo-padrao.txt"));
        foreach (string leftover in new[] { "string.Empty", "null", "undefined", "NaN", "TODO" })
        {
            Assert.DoesNotContain("|" + leftover + "|", text);
        }
    }

    [Fact]
    public void SeedCatalog_PhotosCoverAlmostEverySpeciesAndFilesExist()
    {
        List<Species> species = SeedCatalog.CreateSpecies();
        List<Species> withPhoto = species.Where(s => !string.IsNullOrEmpty(s.PhotoFile)).ToList();
        Assert.True(withPhoto.Count >= species.Count - 5, $"{withPhoto.Count} de {species.Count} espécies com foto");

        // As fotos acompanham o plugin: precisam existir na pasta Fotos do projeto do add-in.
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Viridian.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        string folder = Path.Combine(root!, "src", "Viridian.Addin", "Fotos");
        Assert.All(withPhoto, s => Assert.True(File.Exists(Path.Combine(folder, s.PhotoFile)), s.PhotoFile));
        Assert.True(File.Exists(Path.Combine(folder, "CREDITOS.txt")));
    }

    [Fact]
    public void SeedCatalogIsConsistent()
    {
        List<Species> species = SeedCatalog.CreateSpecies();
        Assert.True(species.Count >= 350, $"apenas {species.Count} espécies");
        Assert.Equal(species.Count, species.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(species.Count, species.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(species.Count, species.Select(s => s.ScientificName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (Species s in species)
        {
            Assert.Empty(SpeciesValidator.Validate(s));
            Assert.StartsWith(SeedCatalog.IdPrefix, s.Id);
            Assert.StartsWith(Labels.Prefix(s.Category) + "-", s.Code);
            Assert.False(string.IsNullOrWhiteSpace(s.BotanicalFamily), s.ScientificName);
            Assert.NotEqual(PlantOrigin.NaoInformada, s.Origin);
            if (s.Origin == PlantOrigin.Nativa)
            {
                Assert.NotEqual(Biome.Nenhum, s.Biomes);
            }
            else
            {
                Assert.Equal(Biome.Nenhum, s.Biomes);
            }

            Assert.True(string.IsNullOrEmpty(s.FoliageColor) || Viridian.Core.Colors.Rgb.TryParse(s.FoliageColor, out _), s.ScientificName);
            Assert.True(string.IsNullOrEmpty(s.FlowerColor) || Viridian.Core.Colors.Rgb.TryParse(s.FlowerColor, out _), s.ScientificName);
            if (!string.IsNullOrEmpty(s.PhotoFile))
            {
                Assert.False(string.IsNullOrWhiteSpace(s.PhotoCredit), s.ScientificName);
                Assert.Contains(s.PhotoLicense, new[] { "CC0", "CC BY", "CC BY-SA" });
                Assert.StartsWith("https://", s.PhotoSource);
            }
            Assert.True(s.AdultHeightM > 0, s.ScientificName);
            Assert.NotNull(HatchLibrary.Find(s.HatchPatternKey));
            Assert.Contains(s.SymbolStyle, SymbolStyles.All);
            Assert.False(s.IsUserDefined);

            // Todo registro precisa gerar um símbolo válido.
            PlantSymbol symbol = SymbolGenerator.Generate(s.SymbolStyle, s.CrownDiameterM, s.EffectiveSymbolSeed);
            Assert.NotEmpty(symbol.Primitives);

            if (s.Category == PlantCategory.Gramado)
            {
                Assert.Equal("m²", s.SupplyUnit);
            }
            else
            {
                Assert.True(s.RecommendedSpacingM > 0, s.ScientificName);
            }
        }

        foreach (PlantCategory category in Labels.Categories)
        {
            Assert.Contains(species, s => s.Category == category);
        }

        // Os identificadores do catálogo padrão são iguais em qualquer computador.
        Assert.Equal(species.Select(s => s.Id), SeedCatalog.CreateSpecies().Select(s => s.Id));
    }

    [Fact]
    public void CatalogIsCreatedSavedAndReloaded()
    {
        var service = new CatalogService(new CatalogStore(CatalogFile));
        Assert.True(File.Exists(CatalogFile));
        int initial = service.All.Count;

        Species draft = service.NewDraft(PlantCategory.Arbusto);
        draft.PopularName = "Murta";
        draft.ScientificName = "Murraya paniculata";
        service.Save(draft);

        var reloaded = new CatalogService(new CatalogStore(CatalogFile));
        Assert.Equal(initial + 1, reloaded.All.Count);
        Species saved = reloaded.Find(draft.Id)!;
        Assert.Equal("Murta", saved.PopularName);
        Assert.True(saved.IsUserDefined);
        Assert.Equal(saved.Id, reloaded.FindByCode(saved.Code)!.Id);
    }

    [Fact]
    public void InvalidOrDuplicatedSpeciesAreRejected()
    {
        var service = new CatalogService(new CatalogStore(CatalogFile));
        Species empty = service.NewDraft(PlantCategory.Arvore);
        Assert.Throws<InvalidOperationException>(() => service.Save(empty));

        Species clash = service.NewDraft(PlantCategory.Arvore);
        clash.PopularName = "Teste";
        clash.ScientificName = "Testus";
        clash.Code = service.All[0].Code;
        Assert.Throws<InvalidOperationException>(() => service.Save(clash));
    }

    [Fact]
    public void RemovedSeedSpeciesDoNotComeBack()
    {
        var service = new CatalogService(new CatalogStore(CatalogFile));
        string id = service.All[0].Id;
        Assert.True(service.Remove(id));
        Assert.False(service.Remove(id));

        var catalog = new CatalogStore(CatalogFile).Load();
        catalog.SeedVersion = 0; // simula atualização do catálogo padrão
        new CatalogStore(CatalogFile).Save(catalog);

        var reloaded = new CatalogService(new CatalogStore(CatalogFile));
        Assert.Null(reloaded.Find(id));
        Assert.Equal(SeedCatalog.CreateSpecies().Count - 1, reloaded.All.Count);
    }

    [Fact]
    public void SeedUpgradeAddsMissingSpeciesAndKeepsEdits()
    {
        var store = new CatalogStore(CatalogFile);
        SpeciesCatalog catalog = store.Load();
        Species edited = catalog.Species[0];
        edited.PopularName = "Nome do escritório";
        edited.IsUserDefined = true;
        edited.FlowerColor = string.Empty;
        Species untouched = catalog.Species[1];
        untouched.PopularName = "Nome antigo do padrão";
        string removedCode = catalog.Species[5].Code;
        catalog.Species.RemoveAt(5);
        catalog.SeedVersion = 0;
        store.Save(catalog);

        SpeciesCatalog upgraded = new CatalogStore(CatalogFile).Load();
        Assert.Equal(SeedCatalog.CreateSpecies().Count, upgraded.Species.Count);
        Species upgradedEdited = upgraded.Species.First(s => s.Id == edited.Id);
        Assert.Equal("Nome do escritório", upgradedEdited.PopularName);
        Assert.Equal(edited.Code, upgradedEdited.Code);
        Assert.False(string.IsNullOrEmpty(upgradedEdited.FlowerColor)); // campo novo completado
        Assert.NotEqual("Nome antigo do padrão", upgraded.Species.First(s => s.Id == untouched.Id).PopularName);
        Assert.Equal(untouched.Code, upgraded.Species.First(s => s.Id == untouched.Id).Code);
        Assert.Equal(upgraded.Species.Count, upgraded.Species.Select(s => s.Code).Distinct().Count());
        Assert.Contains(upgraded.Species, s => s.Code == removedCode);
        Assert.Equal(SeedCatalog.Version, upgraded.SeedVersion);
    }

    [Fact]
    public void CorruptCatalogIsBackedUpAndRecreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogFile)!);
        File.WriteAllText(CatalogFile, "{ isto não é json");
        var store = new CatalogStore(CatalogFile);
        SpeciesCatalog catalog = store.Load();
        Assert.NotEmpty(catalog.Species);
        Assert.NotEmpty(store.LastWarning);
        Assert.Contains(Directory.GetFiles(Path.GetDirectoryName(CatalogFile)!), f => f.Contains("corrompido"));
    }

    [Fact]
    public void BrokenRecordsAreNormalizedOnLoad()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogFile)!);
        File.WriteAllText(CatalogFile, """
            { "SeedVersion": 999, "Species": [
                null,
                { "Id": "x1", "Code": "AR-001", "PopularName": "A", "ScientificName": "A a", "Category": "Arvore", "CrownDiameterM": 0, "SymbolVariants": 50, "SymbolStyle": 12345 },
                { "Id": "x1", "Code": "AR-002", "PopularName": "Duplicada", "ScientificName": "B b", "Category": "Arvore" },
                { "Id": "x2", "Code": "AR-001", "PopularName": "C", "ScientificName": "C c", "Category": "Arvore" },
                { "Id": "x3", "Code": "", "PopularName": "D", "ScientificName": "D d", "Category": "Palmeira" }
            ] }
            """);
        SpeciesCatalog catalog = new CatalogStore(CatalogFile).Load();
        Assert.Equal(3, catalog.Species.Count);
        Assert.Equal(3, catalog.Species.Select(s => s.Code).Distinct().Count());
        Species first = catalog.Species.Single(s => s.Id == "x1");
        Assert.Equal(SymbolGenerator.MinDiameter, first.CrownDiameterM);
        Assert.Equal(Species.MaxVariants, first.SymbolVariants);
        Assert.Contains(first.SymbolStyle, SymbolStyles.All);
        Assert.Equal("PA-001", catalog.Species.Single(s => s.Id == "x3").Code);
    }

    [Fact]
    public void SearchIgnoresAccentsAndCombinesFilters()
    {
        var service = new CatalogService(new CatalogStore(CatalogFile));
        Assert.Contains(service.Search(new SpeciesFilter { Text = "ipe amarelo" }), s => s.ScientificName == "Handroanthus chrysotrichus");
        Assert.Contains(service.Search(new SpeciesFilter { Text = "HANDROANTHUS" }), s => s.PopularName == "Ipê-roxo");
        Assert.Empty(service.Search(new SpeciesFilter { Text = "especie inexistente xyz" }));

        IReadOnlyList<Species> small = service.Search(new SpeciesFilter
        {
            Category = PlantCategory.Arvore,
            Origin = PlantOrigin.Nativa,
            MinHeightM = 5,
            MaxHeightM = 8,
            MaxCrownM = 6,
        });
        Assert.NotEmpty(small);
        Assert.All(small, s =>
        {
            Assert.Equal(PlantCategory.Arvore, s.Category);
            Assert.Equal(PlantOrigin.Nativa, s.Origin);
            Assert.InRange(s.AdultHeightM, 5, 8);
            Assert.True(s.CrownDiameterM <= 6);
        });

        Assert.Empty(service.Search(new SpeciesFilter { OnlyUserDefined = true }));
    }

    [Fact]
    public void DuplicateCreatesIndependentRecord()
    {
        var service = new CatalogService(new CatalogStore(CatalogFile));
        Species source = service.All[0];
        Species copy = service.Duplicate(source);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.NotEqual(source.Code, copy.Code);
        service.Save(copy);
        Assert.Equal(source.PopularName, service.Find(source.Id)!.PopularName);
    }

    [Fact]
    public void SettingsAreSavedAndLoaded()
    {
        string file = Path.Combine(_folder, "Config", "viridian.config.json");
        var settings = SettingsStore.Load(file);
        settings.AutoApplyScaleProfiles = false;
        settings.Quantities.MassingReservePercent = 15;
        SettingsStore.Save(settings, file);

        var loaded = SettingsStore.Load(file);
        Assert.False(loaded.AutoApplyScaleProfiles);
        Assert.Equal(15, loaded.Quantities.MassingReservePercent);

        File.WriteAllText(file, "lixo");
        Assert.True(SettingsStore.Load(file).AutoApplyScaleProfiles);
    }

    [Fact]
    public void NamesAreSanitizedForFilesAndFamilies()
    {
        Assert.Equal("VIR AR Handroanthus chrysotrichus V1", SafeFile.SanitizeName("VIR/AR: Handroanthus  chrysotrichus [V1]"));
        Assert.Equal("sem-nome", SafeFile.SanitizeName("///"));
        Assert.Equal("handroanthus-chrysotrichus", TextSearch.Slug("Handroanthus chrysotrichus"));
        Assert.Equal("hemerocallis-hybrida", TextSearch.Slug("Hemerocallis × hybrida"));
        var paths = new LibraryPaths(_folder);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.GeneratedFolder(PlantCategory.Arvore)));
        Assert.True(Directory.Exists(paths.CustomHatchesFolder));
    }
}

public class TemplateNameTests
{
    [Theory]
    [InlineData("Metric Planting", 103)]
    [InlineData("Planting", 100)]
    [InlineData("Vegetação métrica", 103)]
    [InlineData("Metric Generic Model", 83)]
    [InlineData("Generic Model", 80)]
    [InlineData("Modelo genérico métrico", 83)]
    [InlineData("Metric Site", 43)]
    [InlineData("Metric Entourage", 33)]
    public void SuitableTemplatesAreRanked(string name, int expected) => Assert.Equal(expected, TemplateNames.Score(name));

    [Theory]
    [InlineData("Metric Generic Model face based")]
    [InlineData("Metric Generic Model wall based")]
    [InlineData("Metric Generic Model Adaptive")]
    [InlineData("Metric Generic Model two level based")]
    [InlineData("Modelo genérico métrico baseado em face")]
    [InlineData("Modelo genérico métrico baseado em linha")]
    [InlineData("Metric RPC Family")]
    [InlineData("Metric Door")]
    [InlineData("Metric Generic Tag")]
    [InlineData("Metric Detail Item")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsuitableTemplatesAreRejected(string? name) => Assert.Equal(0, TemplateNames.Score(name));
}
