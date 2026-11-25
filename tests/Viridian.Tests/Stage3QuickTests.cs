using Viridian.Core.Detailing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.Tests;

public class PurchaseComparisonTests
{
    private static AcquisitionRow Row(string id, string code, int quantity, int reserve = 0) =>
        new() { SpeciesId = id, Code = code, PopularName = "Planta " + code, Quantity = quantity, Reserve = reserve, Unit = "muda" };

    [Fact]
    public void NoLedger_EverythingToBuy()
    {
        var rows = PurchaseComparison.Build(new[] { Row("a", "AR01", 10, 1) }, null);
        PurchaseComparisonRow r = Assert.Single(rows);
        Assert.Equal(11, r.Needed);
        Assert.Equal(0, r.Purchased);
        Assert.Equal(-11, r.Difference);
        Assert.Equal("A comprar: 11", r.Status);
    }

    [Fact]
    public void Substitution_ShowsMissingAndSurplus()
    {
        // Comprou 10 de A; o projeto trocou A por B.
        var ledger = new PurchaseLedger { Items = { new PurchaseRecord { Key = "a", Code = "AR01", PopularName = "Ipê", Quantity = 10 } } };
        var rows = PurchaseComparison.Build(new[] { Row("b", "AR02", 10) }, ledger);
        Assert.Equal(2, rows.Count);
        PurchaseComparisonRow b = rows.Single(r => r.Key == "b");
        PurchaseComparisonRow a = rows.Single(r => r.Key == "a");
        Assert.Equal(-10, b.Difference);
        Assert.True(a.OutOfProject);
        Assert.Equal(10, a.Difference);
        Assert.Equal("Fora do projeto: sobram 10", a.Status);
        Assert.Same(a, rows.Last()); // itens fora do projeto vão para o fim
        (int items, int missing, int surplus) = PurchaseComparison.Summary(rows);
        Assert.Equal((1, 10, 10), (items, missing, surplus));
    }

    [Fact]
    public void PartialPurchase_AndEditingUpdatesStatus()
    {
        var ledger = new PurchaseLedger { Items = { new PurchaseRecord { Key = "a", Quantity = 4 } } };
        PurchaseComparisonRow r = Assert.Single(PurchaseComparison.Build(new[] { Row("a", "AR01", 6) }, ledger));
        Assert.Equal("Falta comprar 2", r.Status);
        var changed = new List<string?>();
        r.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        r.Purchased = 6;
        Assert.Equal("Atendido", r.Status);
        Assert.Contains(nameof(PurchaseComparisonRow.Difference), changed);
        r.Purchased = 9;
        Assert.Equal("Sobram 3", r.Status);
        r.Purchased = -5;
        Assert.Equal(0, r.Purchased);
    }

    [Fact]
    public void UnregisteredFamilies_UseCodeAndName()
    {
        var unregistered = new AcquisitionRow { Code = "X1", PopularName = "Família local", Quantity = 3 };
        Assert.Equal("X1|Família local", PurchaseComparison.KeyOf(unregistered));
        var rows = PurchaseComparison.Build(new[] { unregistered }, null);
        rows[0].Purchased = 3;
        PurchaseLedger ledger = PurchaseComparison.ToLedger(rows, new DateTime(2026, 1, 1));
        Assert.Equal("X1|Família local", Assert.Single(ledger.Items).Key);
        Assert.Equal("Atendido", PurchaseComparison.Build(new[] { unregistered }, ledger)[0].Status);
    }

    [Fact]
    public void DuplicatedLedgerEntries_AreSummed_AndZeroNotSaved()
    {
        var ledger = new PurchaseLedger { Items = { new() { Key = "a", Quantity = 2 }, new() { Key = "A", Quantity = 3 }, new() { Key = "", Quantity = 9 } } };
        var rows = PurchaseComparison.Build(new[] { Row("a", "AR01", 5), Row("c", "AR03", 2) }, ledger);
        Assert.Equal(5, rows.Single(r => r.Key == "a").Purchased);
        Assert.Single(PurchaseComparison.ToLedger(rows, DateTime.Now).Items);
    }

    [Fact]
    public void Ledger_SurvivesJson()
    {
        var ledger = new PurchaseLedger { UpdatedAt = new DateTime(2026, 5, 1), Items = { new() { Key = "a", Code = "AR01", Quantity = 7 } } };
        string json = Viridian.Core.Config.ViridianJson.Serialize(ledger);
        PurchaseLedger back = Viridian.Core.Config.ViridianJson.Deserialize<PurchaseLedger>(json)!;
        Assert.Equal(7, back.Items[0].Quantity);
        Assert.Equal(7, back.Clone().Items[0].Quantity);
    }
}

public class ExcavationAndBoardTests
{
    [Fact]
    public void TreePit_InCentimetres()
    {
        var tree = new Species { Category = PlantCategory.Arvore, PlantingHeightM = 2.0 };
        PlantingDimensions d = PlantingDetails.Dimensions(tree);
        Assert.Equal("60 × 60 × 60 cm", PlantingDetails.ExcavationText(d));
        Assert.Equal(0.216, PlantingDetails.PitVolumeM3(d), 6);
        Assert.Equal(2.16, PlantingDetails.ExcavationM3(d, 10, false)!.Value, 6);
    }

    [Fact]
    public void Beds_ByArea()
    {
        var cover = new Species { Category = PlantCategory.Forracao };
        PlantingDimensions d = PlantingDetails.Dimensions(cover);
        Assert.Equal("camada de 20 cm", PlantingDetails.ExcavationText(d));
        Assert.Equal(10, PlantingDetails.ExcavationM3(d, 50, true)!.Value, 6);
        Assert.Null(PlantingDetails.ExcavationM3(d, 50, false)); // forração vendida por muda: depende da área do canteiro
        var lawn = new Species { Category = PlantCategory.Gramado };
        Assert.Equal("camada de 10 cm", PlantingDetails.ExcavationText(PlantingDetails.Dimensions(lawn)));
    }

    [Fact]
    public void Wrap_NeverExceedsWidth()
    {
        string text = string.Join(' ', Enumerable.Repeat("palavra", 40)) + " " + new string('x', 50);
        foreach (string line in TextLayout.Wrap(text, 30))
        {
            Assert.True(line.Length <= 30, line);
        }

        Assert.Empty(TextLayout.Wrap("", 30));
        Assert.Equal(new[] { "a", "b" }, TextLayout.Wrap("a\nb", 30));
    }

    [Fact]
    public void Board_HasRowPerSpecies_AndExcavation()
    {
        List<Species> all = SeedCatalog.CreateSpecies();
        var items = all.Take(6).Select((s, i) => new SpecificationItem(s, "AR0" + i, 3 + i, s.SupplyUnit)).ToList();
        Drawing board = SpecificationBoard.Build(items, new SpecificationSettings(), 2.0);
        foreach (SpecificationItem item in items)
        {
            Assert.Contains(board.Texts, t => t.Text == item.Code);
            Assert.Contains(board.Texts, t => t.Text.Contains(item.Species.PopularName.Split(' ')[0], StringComparison.Ordinal));
        }

        Assert.Contains(board.Texts, t => t.Text.EndsWith(" cm", StringComparison.Ordinal));
        Viridian.Core.Geometry.Bounds2 e = board.Extent();
        Assert.InRange(e.Width, SpecificationBoard.Width * 0.99, SpecificationBoard.Width * 1.6);

        // Textos dentro de cada coluna cabem na largura dela (a nota geral ocupa a largura toda).
        double charWidth = 2.0 * 0.6 / 1000;
        double specStart = SpecificationBoard.Columns.Take(SpecificationBoard.Columns.Length - 1).Sum();
        foreach (DrawText t in board.Texts.Where(t => t.At.X >= specStart && t.At.Y < 0))
        {
            Assert.True(t.Text.Length <= (SpecificationBoard.Columns[^1] - 0.003) / charWidth + 1, t.Text);
        }

        Assert.All(board.Texts, t => Assert.True(t.Text.Length <= SpecificationBoard.Width / charWidth + 1, t.Text));
    }

    [Fact]
    public void Board_TotalsByUnit()
    {
        var s = new Species { PopularName = "A", SupplyUnit = "muda" };
        var g = new Species { PopularName = "G", SupplyUnit = "m²" };
        var items = new[] { new SpecificationItem(s, "A1", 3, "muda"), new SpecificationItem(s, "A2", 4, "muda"), new SpecificationItem(g, "G1", 120.5, "m²") };
        string totals = SpecificationBoard.Totals(items);
        Assert.Contains("7 mudas", totals);
        Assert.Contains("120,5 m²", totals);
        Drawing board = SpecificationBoard.Build(items, new SpecificationSettings(), 2.0);
        Assert.Contains(board.Texts, t => t.Text == totals);
        Assert.Contains(board.Texts, t => t.Style == TextStyle.Cientifico);
    }

    [Fact]
    public void Board_Empty_IsJustHeader()
    {
        Drawing board = SpecificationBoard.Build(Array.Empty<SpecificationItem>(), new SpecificationSettings(), 2.0);
        Assert.NotEmpty(board.Lines);
    }
}
