using Viridian.Core.Hatch;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.Tests;

public class LooseMaterialTests
{
    [Fact]
    public void Defaults_AreValid_Unique_AndCoverTheRequestedList()
    {
        List<LooseMaterial> all = LooseMaterialDefaults.Create();
        Assert.True(all.Count >= 30, all.Count.ToString());
        Assert.Equal(all.Count, all.Select(m => m.Id).Distinct().Count());
        foreach (LooseMaterial m in all)
        {
            Assert.Empty(m.Validate());
        }

        string[] requested =
        {
            "Seixo branco", "Seixo natural de rio", "Seixo preto", "Seixo bege", "Dolomita branca", "Mármore triturado", "Pedrisco branco",
            "Pedrisco de granito", "Pedrisco natural", "Pedrisco de basalto", "Brita branca", "Brita cinza", "Brita colorida", "Cascalho fino",
            "Cascalho grosso", "Quartzo triturado",
        };
        foreach (string name in requested)
        {
            Assert.Contains(all, m => m.Name.StartsWith(name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryUse_BecomesAValidPaving_WithValidHatch()
    {
        foreach (LooseMaterial m in LooseMaterialDefaults.Create())
        {
            foreach (LooseUse use in LooseLabels.AllUses.Where(u => m.Uses.HasFlag(u)))
            {
                PavingType t = LooseMaterialUse.ToPaving(m, use);
                Assert.True(t.Validate().Count == 0, $"{t.Name}: {string.Join("; ", t.Validate())}");
                Assert.Equal(PavingCategory.Cobertura, t.Category);
                Assert.StartsWith(LooseMaterialUse.IdPrefix, t.Id, StringComparison.Ordinal);
                HatchPattern hatch = PavingPatterns.Build(t, "teste");
                Assert.True(hatch.IsValid(out string problem), t.Name + ": " + problem);
                Assert.NotEmpty(hatch.Grids);
            }
        }
    }

    [Fact]
    public void Uses_SetLayersAndThickness()
    {
        LooseMaterial seixo = LooseMaterialDefaults.Create().First(m => m.Id == "vir-seixo-pequeno");
        PavingType piso = LooseMaterialUse.ToPaving(seixo, LooseUse.PisoSolto);
        Assert.Contains(piso.Layers, l => l.Kind == PavingLayerKind.Manta);
        Assert.Contains(piso.Layers, l => l.Kind == PavingLayerKind.Base && l.ThicknessMm == 100);
        Assert.Equal(150, LooseMaterialUse.Thickness(seixo, LooseUse.Drenagem));
        PavingType canteiro = LooseMaterialUse.ToPaving(seixo, LooseUse.Canteiro);
        Assert.Equal(seixo.LayerMm, canteiro.ThicknessMm);
        Assert.Single(canteiro.Layers);
        Assert.Contains("10 a 20 mm", canteiro.Material, StringComparison.Ordinal);
        List<LooseMaterial> all = LooseMaterialDefaults.Create();
        Assert.Equal(LayingPattern.Lascas, LooseMaterialUse.ToPaving(all.First(m => m.IsOrganic), LooseUse.CoberturaMorta).Pattern);
        Assert.Equal(LayingPattern.Seixos, LooseMaterialUse.PatternFor(all.First(m => m.Id == "vir-seixo-branco")));
        Assert.Equal(LayingPattern.Britas, LooseMaterialUse.PatternFor(all.First(m => m.Id == "vir-brita-cinza")));
        Assert.Equal(LayingPattern.Granular, LooseMaterialUse.PatternFor(all.First(m => m.Id == "vir-po-de-pedra")));
    }

    [Fact]
    public void Purchase_VolumeWeightAndBags()
    {
        var m = new LooseMaterial { Name = "Teste", LayerMm = 50, WastePercent = 10, DensityTM3 = 1.5, BagKg = 20 };
        (double volume, double? weight, int? bags) = LooseMaterialUse.Purchase(m, LooseUse.Canteiro, 10);
        Assert.Equal(0.55, volume, 6);       // 10 m² × 5 cm × 1,10
        Assert.Equal(0.825, weight!.Value, 6);
        Assert.Equal(42, bags);              // 825 kg / 20 kg = 41,25 → 42
        var casca = new LooseMaterial { Name = "Casca", LayerMm = 50, WastePercent = 0, DensityTM3 = 0.25, BagKg = 0, BagLiters = 50 };
        Assert.Equal(10, LooseMaterialUse.Purchase(casca, LooseUse.Canteiro, 10).Bags); // 0,5 m³ / 50 L
    }

    [Fact]
    public void PavingQuantity_ShowsWeightAndBags()
    {
        LooseMaterial m = LooseMaterialDefaults.Create().First(x => x.Id == "vir-pedrisco-branco");
        var q = new PavingQuantity { Type = LooseMaterialUse.ToPaving(m, LooseUse.Canteiro), AreaM2 = 20, Elements = 1 };
        Assert.Null(q.Pieces);
        Assert.NotNull(q.WeightT);
        Assert.Contains(" t", q.PurchaseText, StringComparison.Ordinal);
        Assert.Contains("sacos de 20 kg", q.PurchaseText, StringComparison.Ordinal);
        Viridian.Core.Export.XlsxSheet sheet = PavingSheets.Build(new[] { q }, "Projeto", DateTime.Now);
        Assert.Contains(sheet.Rows, r => r.Cells.Any(c => c is string s && s.StartsWith("Sacos de 20", StringComparison.Ordinal)));
    }

    [Fact]
    public void Normalize_RepairsAndValidateFlagsProblems()
    {
        var m = new LooseMaterial { Name = " X ", GrainMinMm = 50, GrainMaxMm = 10, Color = "azul", Uses = LooseUse.Nenhum, LayerMm = double.NaN, Id = "" };
        m.Normalize();
        Assert.Equal("X", m.Name);
        Assert.True(m.GrainMinMm <= m.GrainMaxMm);
        Assert.Equal("#C9C2B4", m.Color);
        Assert.Equal(LooseUse.Canteiro, m.Uses);
        Assert.Equal(50, m.LayerMm);
        Assert.NotEmpty(m.Id);
        Assert.NotEmpty(new LooseMaterial { Name = "", Uses = LooseUse.Nenhum }.Validate());
        Assert.Equal("até 4,8 mm", new LooseMaterial { GrainMinMm = 0, GrainMaxMm = 4.8 }.GrainText);
    }

    [Fact]
    public void Library_Merge_KeepsUserMaterials_AndUpdatesSeeds()
    {
        LooseMaterial seed = LooseMaterialDefaults.Create()[0];
        LooseMaterial edited = seed.Clone();
        edited.Name = "Meu seixo";
        edited.IsUserDefined = true;
        var mine = new LooseMaterial { Id = "meu", Name = "Pedra do escritório", IsUserDefined = true };
        var outdated = LooseMaterialDefaults.Create()[1];
        outdated.Name = "nome antigo";
        var file = new LooseMaterialFile { SeedVersion = 0, Materials = new() { edited, mine, outdated } };
        (List<LooseMaterial> merged, bool changed) = LooseMaterialLibrary.Merge(file);
        Assert.True(changed);
        Assert.Equal("Meu seixo", merged.First(m => m.Id == seed.Id).Name);
        Assert.Contains(merged, m => m.Id == "meu");
        Assert.NotEqual("nome antigo", merged.First(m => m.Id == outdated.Id).Name);
        Assert.Equal(LooseMaterialDefaults.Create().Count + 1, merged.Count);
        Assert.True(LooseMaterialLibrary.Merge(null).Changed);
    }
}

public class RainGardenMaterialTests
{
    [Fact]
    public void DetailAndReport_UseChosenMaterials()
    {
        var s = new Viridian.Core.Water.RainGardenSettings { GravelName = "Brita 2", MulchName = "Casca de pinus média" };
        s.Normalize();
        Viridian.Core.Water.RainGardenResult r = Viridian.Core.Water.RainGardenCalculator.Calculate(100, 0, s);
        Viridian.Core.Detailing.Drawing d = Viridian.Core.Water.RainGardenCalculator.Detail(s, r);
        Assert.Contains(d.Texts, t => t.Text.Contains("BRITA 2 (RESERVATÓRIO)", StringComparison.Ordinal));
        Assert.Contains(d.Texts, t => t.Text.Contains("COBERTURA MORTA (CASCA DE PINUS MÉDIA)", StringComparison.Ordinal));
        Assert.Contains(Viridian.Core.Water.RainGardenCalculator.Report(100, r, s), l => l.Contains("brita 2 30 cm", StringComparison.Ordinal));

        var old = new Viridian.Core.Water.RainGardenSettings { GravelName = null!, MulchName = null! };
        old.Normalize();
        Assert.Equal("Brita", old.GravelName);
        Assert.Equal(string.Empty, old.MulchName);
    }
}
