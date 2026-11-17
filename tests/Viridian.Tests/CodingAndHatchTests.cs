using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Symbols;

namespace Viridian.Tests;

public class CodingTests
{
    [Fact]
    public void SpeciesCodesAreSequentialPerCategory()
    {
        var codes = new[] { "AR-001", "AR-002", "PA-001", "ar-010", "lixo" };
        Assert.Equal("AR-011", CodeFormat.NextSpeciesCode(codes, PlantCategory.Arvore));
        Assert.Equal("PA-002", CodeFormat.NextSpeciesCode(codes, PlantCategory.Palmeira));
        Assert.Equal("AQ-001", CodeFormat.NextSpeciesCode(codes, PlantCategory.Aquatica));
    }

    [Fact]
    public void EveryCategoryHasAUniquePrefix()
    {
        var prefixes = Labels.Categories.Select(Labels.Prefix).ToList();
        Assert.Equal(new[] { "AR", "PA", "AB", "HE", "FO", "GR", "TR", "AQ" }, prefixes);
        foreach (PlantCategory category in Labels.Categories)
        {
            Assert.True(Labels.TryCategoryFromPrefix(Labels.Prefix(category).ToLowerInvariant(), out PlantCategory parsed));
            Assert.Equal(category, parsed);
        }

        Assert.False(Labels.TryCategoryFromPrefix("ZZ", out _));
    }

    [Fact]
    public void InstanceCodeFormatRoundTrips()
    {
        var format = new InstanceCodeFormat();
        Assert.Equal("AR.0012", format.Format(PlantCategory.Arvore, "AR-003", 12));
        Assert.True(format.TryParse("ar.0012", PlantCategory.Arvore, "AR-003", out int number));
        Assert.Equal(12, number);
        Assert.False(format.TryParse("PA.0012", PlantCategory.Arvore, "AR-003", out _));
        Assert.False(format.TryParse("AR.", PlantCategory.Arvore, "AR-003", out _));

        var bySpecies = new InstanceCodeFormat { Template = "{ESP}/{N}", Digits = 3 };
        Assert.Equal("AR-003/007", bySpecies.Format(PlantCategory.Arvore, "AR-003", 7));
        Assert.True(bySpecies.TryParse("AR-003/007", PlantCategory.Arvore, "AR-003", out number));
        Assert.Equal(7, number);

        var invalid = new InstanceCodeFormat { Template = "sem numero", Digits = 99 };
        invalid.Normalize();
        Assert.Equal(InstanceCodeFormat.DefaultTemplate, invalid.Template);
        Assert.Equal(8, invalid.Digits);
    }

    [Fact]
    public void AllocatorContinuesAfterExistingCodesAndNeverRepeats()
    {
        var allocator = new InstanceCodeAllocator(new InstanceCodeFormat());
        allocator.Register("AR.0003");
        allocator.Register("AR.0009");
        allocator.Register("manual-1");
        allocator.Register(null);
        Assert.Equal("AR.0010", allocator.Next(PlantCategory.Arvore, "AR-001"));
        Assert.Equal("AR.0011", allocator.Next(PlantCategory.Arvore, "AR-005"));
        Assert.Equal("PA.0001", allocator.Next(PlantCategory.Palmeira, "PA-001"));
        Assert.True(allocator.IsUsed("ar.0010"));
        Assert.True(allocator.IsUsed("manual-1"));
        Assert.False(allocator.IsUsed("AR.0500"));
    }
}

public class HatchTests
{
    [Fact]
    public void BuiltInPatternsAreValidAndUnique()
    {
        Assert.True(HatchLibrary.BuiltIn.Count >= 6);
        Assert.Equal(HatchLibrary.BuiltIn.Count, HatchLibrary.BuiltIn.Select(p => p.Key).Distinct().Count());
        Assert.Equal(HatchLibrary.BuiltIn.Count, HatchLibrary.BuiltIn.Select(p => p.Name).Distinct().Count());
        foreach (HatchPattern pattern in HatchLibrary.BuiltIn)
        {
            Assert.True(pattern.IsValid(out string problem), pattern.Key + ": " + problem);
            Assert.True(pattern.IsBuiltIn);
            Assert.StartsWith("VIR ", pattern.Name);
        }

        foreach (PlantCategory category in Labels.Categories)
        {
            Assert.NotNull(HatchLibrary.Find(HatchLibrary.DefaultKeyFor(category)));
        }

        Assert.NotNull(HatchLibrary.Find(HatchLibrary.Simplified));
        Assert.Null(HatchLibrary.Find("inexistente"));
        Assert.Null(HatchLibrary.Find(null));
    }

    [Fact]
    public void PatFileIsParsed()
    {
        const string text = """
            ;%UNITS=MM
            ;%TYPE=DRAFTING
            *Pedrisco, pontos aleatorios
            0, 0, 0, 2, 3, 0, -4
            45, 1, 1, 0, 2.5
            *Vazio, sem linhas
            *Modelo
            ;%TYPE=MODEL
            90, 0, 0, 0, 200, 100, -50, 20, -30
            linha invalida
            """;
        IReadOnlyList<HatchPattern> patterns = PatFile.Parse(text, out IReadOnlyList<string> warnings);
        Assert.Equal(2, patterns.Count);
        Assert.NotEmpty(warnings);

        HatchPattern first = patterns[0];
        Assert.Equal("custom-pedrisco", first.Key);
        Assert.Equal("VIR Pedrisco", first.Name);
        Assert.Equal(HatchTarget.Drafting, first.Target);
        Assert.Equal(2, first.Grids.Count);
        Assert.Equal(new[] { HatchLibrary.DotMm, 4.0 }, first.Grids[0].Segments);
        Assert.Empty(first.Grids[1].Segments);

        HatchPattern model = patterns[1];
        Assert.Equal(HatchTarget.Model, model.Target);
        Assert.Equal(new[] { 100.0, 50.0, 20.0, 30.0 }, model.Grids[0].Segments);
    }

    [Fact]
    public void PatFileDefaultsToInches()
    {
        IReadOnlyList<HatchPattern> patterns = PatFile.Parse("*Linhas\n0,0,0,0,0.125\n", out _);
        Assert.Single(patterns);
        Assert.Equal(3.175, patterns[0].Grids[0].Offset, 9);
        Assert.Empty(PatFile.Parse(string.Empty, out _));
    }
}

public class SettingsTests
{
    [Fact]
    public void SettingsRoundTripThroughJson()
    {
        var settings = new ViridianSettings { LibraryRoot = @"X:\Biblioteca", MassingPrefix = "mac" };
        settings.Scales.Ranges[0].MaxDenominator = 40;
        settings.TierStyles[2].Color = "#1a7f37";
        settings.Normalize();

        ViridianSettings copy = ViridianJson.Deserialize<ViridianSettings>(ViridianJson.Serialize(settings))!;
        copy.Normalize();
        Assert.Equal(@"X:\Biblioteca", copy.LibraryRoot);
        Assert.Equal("MAC", copy.MassingPrefix);
        Assert.Equal(40, copy.Scales.Ranges[0].MaxDenominator);
        Assert.Equal("#1A7F37", copy.StyleOf(SymbolTier.Outline).Color);
        Assert.Equal(TierVisibility.AllTiers.Count, copy.TierStyles.Count);
    }

    [Fact]
    public void NormalizeRepairsBrokenSettings()
    {
        ViridianSettings settings = ViridianJson.Deserialize<ViridianSettings>(
            "{ \"TierStyles\": [ { \"Tier\": \"Outline\", \"LineWeight\": 99, \"Color\": \"azul\" } ], \"Scales\": null, \"MassingPrefix\": \"  \", " +
            "\"Labels\": { \"TextSizeMm\": -3 }, \"Planting\": { \"MaxCount\": 0, \"JitterPercent\": 900 }, \"CustomHatches\": [ null ] }")!;
        settings.Normalize();
        Assert.Equal(16, settings.StyleOf(SymbolTier.Outline).LineWeight);
        Assert.Equal("#000000", settings.StyleOf(SymbolTier.Outline).Color);
        Assert.Equal(TierVisibility.AllTiers.Count, settings.TierStyles.Count);
        Assert.NotNull(settings.Scales);
        Assert.Equal("MC", settings.MassingPrefix);
        Assert.Equal(0.8, settings.Labels.TextSizeMm);
        Assert.Equal(1, settings.Planting.MaxCount);
        Assert.Equal(50, settings.Planting.JitterPercent);
        Assert.Empty(settings.CustomHatches);
    }

    [Fact]
    public void ColorHexParsing()
    {
        Assert.True(ColorHex.TryParse("#2F6B4F", out byte r, out byte g, out byte b));
        Assert.Equal((0x2F, 0x6B, 0x4F), (r, g, b));
        Assert.False(ColorHex.TryParse("#12", out _, out _, out _));
        Assert.Equal("#000000", ColorHex.Normalize(null));
    }
}

public class LabelTextTests
{
    [Fact]
    public void PlantLabelsFollowTheModels()
    {
        Assert.Equal("AR-001", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoSimples, "AR-001", "Ipê-amarelo", "Handroanthus chrysotrichus", 1, "AR.0001"));
        Assert.Equal("5 AR-001", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoSimples, "AR-001", "Ipê-amarelo", "H. c.", 5, string.Empty));
        Assert.Equal("AR-001 Ipê-amarelo", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoNomePopular, "AR-001", "Ipê-amarelo", "H. c.", 1, string.Empty));
        Assert.Equal("3 AR-001 Handroanthus chrysotrichus", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoNomeCientifico, "AR-001", "Ipê", "Handroanthus chrysotrichus", 3, string.Empty));
        Assert.Equal("AR-001", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoLinhaIndicadora, "AR-001", "Ipê", "H", 1, string.Empty));
        Assert.Equal("AR-001 Ipê\n12 un", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.AreaQuantidade, "AR-001", "Ipê", "H", 12, string.Empty));
        Assert.Equal("?", Viridian.Core.Identification.LabelText.ForPlants(Viridian.Core.Identification.LabelModel.CodigoSimples, " ", string.Empty, string.Empty, 1, string.Empty));
        Assert.True(Viridian.Core.Identification.LabelText.ForcesLeader(Viridian.Core.Identification.LabelModel.CodigoLinhaIndicadora));
    }

    [Fact]
    public void MassingLabelsFollowTheModels()
    {
        var record = new Viridian.Core.Massing.MassingRecord
        {
            Id = "MC-001", SpeciesCode = "FO-001", PopularName = "Grama-amendoim", ScientificName = "Arachis repens",
            AreaM2 = 45.297, Quantity = 1725, SupplyUnit = "muda",
        };
        Assert.Equal("MC-001", Viridian.Core.Identification.LabelText.ForMassing(Viridian.Core.Identification.LabelModel.CodigoMacico, record));
        Assert.Equal("FO-001 Grama-amendoim", Viridian.Core.Identification.LabelText.ForMassing(Viridian.Core.Identification.LabelModel.CodigoNomePopular, record));
        Assert.Equal("MC-001\nFO-001 Grama-amendoim\n45,30 m² · 1.725 muda", Viridian.Core.Identification.LabelText.ForMassing(Viridian.Core.Identification.LabelModel.AreaQuantidade, record));

        record.SupplyUnit = "m²";
        Assert.Equal("45,30 m²", Viridian.Core.Identification.LabelText.AreaAndQuantity(record));
        Assert.All(Viridian.Core.Identification.LabelText.Models, m => Assert.False(string.IsNullOrWhiteSpace(Viridian.Core.Identification.LabelText.DisplayName(m))));
    }
}
