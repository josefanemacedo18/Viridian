using System.IO.Compression;
using System.Text;
using Viridian.Core.Costs;
using Viridian.Core.Export;
using Viridian.Core.Maintenance;
using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.Tests;

public class PriceTableTests
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("R$ 12,50", 12.50)]
    [InlineData("0,8", 0.8)]
    [InlineData("1,234.5", 1234.5)]
    public void TryPrice_ParsesBrazilianAndInvariantFormats(string text, double expected)
    {
        Assert.True(PriceTableReader.TryPrice(text, out decimal value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void TryPrice_RejectsInvalid(string text) => Assert.False(PriceTableReader.TryPrice(text, out _));

    [Fact]
    public void Csv_FindsHeaderBelowTitleLines()
    {
        string csv = "SISTEMA NACIONAL DE PESQUISA;;;;\n"
                     + "Data de referência: 03/2026;;;;\n"
                     + ";;;;\n"
                     + "CODIGO;DESCRICAO DO INSUMO;UNIDADE;ORIGEM DE PRECO;PRECO MEDIANO R$\n"
                     + "00012345;\"MUDA DE ARVORE NATIVA, H = 1,5 M\";UN;CR;45,90\n"
                     + "00067890;GRAMA ESMERALDA EM PLACAS;M2;C;\"1.234,10\"\n"
                     + "00011111;ITEM SEM PRECO;UN;C;\n";

        PriceTableReadResult result = PriceTableReader.FromRows(PriceTableReader.ReadCsv(csv), "SINAPI");

        Assert.Equal(string.Empty, result.Error);
        PriceTable table = result.Table!;
        Assert.Equal(2, table.Items.Count);
        Assert.Equal(45.90m, table.Find("12345")!.Price);
        Assert.Equal("MUDA DE ARVORE NATIVA, H = 1,5 M", table.Find("00012345")!.Description);
        Assert.Equal(1234.10m, table.Find("67890")!.Price);
        Assert.Equal("M2", table.Find("67890")!.Unit);
        Assert.Single(result.Warnings);
        Assert.Single(table.Search("grama esmeralda"));
        Assert.Single(table.Search("árvore nativa"));
    }

    [Fact]
    public void Csv_WithoutHeader_ReportsError()
    {
        PriceTableReadResult result = PriceTableReader.FromRows(PriceTableReader.ReadCsv("a;b;c\n1;2;3\n"), "x");
        Assert.Null(result.Table);
        Assert.NotEmpty(result.Error);
    }

    [Fact]
    public void Xlsx_ReadsSharedStringsAndNumbers()
    {
        // Planilha escrita pelo próprio Viridian e lida de volta.
        string file = Path.Combine(Path.GetTempPath(), "viridian-precos-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            var sheet = new XlsxSheet("Preços");
            sheet.TitleLines.Add("Tabela de preços do escritório");
            sheet.Columns.AddRange(new[] { new XlsxColumn("Código"), new XlsxColumn("Descrição"), new XlsxColumn("Unidade"), new XlsxColumn("Preço", 12, XlsxFormat.Currency) });
            sheet.Rows.Add(new XlsxRow("MD-01", "Muda de ipê-amarelo 2,0 m", "un", 89.5m));
            sheet.Rows.Add(new XlsxRow("SV-10", "Plantio de árvore com berço 60x60x60", "un", 120m));
            XlsxWriter.Write(file, new[] { sheet });

            PriceTableReadResult result = PriceTableReader.Read(file);

            Assert.Equal(string.Empty, result.Error);
            Assert.Equal(2, result.Table!.Items.Count);
            Assert.Equal(89.5m, result.Table.Find("md01")!.Price);
            Assert.Equal("Plantio de árvore com berço 60x60x60", result.Table.Find("SV-10")!.Description);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CodeKey_IgnoresSeparatorsAndLeadingZeros()
    {
        Assert.Equal(PriceTable.CodeKey("00012.345"), PriceTable.CodeKey("12345"));
        Assert.Equal("MD01", PriceTable.CodeKey("md-01"));
        Assert.Equal(string.Empty, PriceTable.CodeKey("  "));
    }
}

public class BudgetTests
{
    private static readonly Species Ipe = new()
    {
        Id = "sp-ipe", PopularName = "Ipê-amarelo", ScientificName = "Handroanthus albus", Category = PlantCategory.Arvore,
        PriceItemCode = "12345", ServiceItemCode = "SV-10", UnitPrice = 70m,
    };

    private static readonly Species Grama = new()
    {
        Id = "sp-grama", PopularName = "Grama-esmeralda", ScientificName = "Zoysia japonica", Category = PlantCategory.Gramado, UnitPrice = 9.5m,
    };

    private static PriceTable Table() => new()
    {
        Name = "SINAPI teste",
        Items =
        {
            new PriceItem { Code = "00012345", Description = "MUDA DE IPE", Unit = "UN", Price = 50m },
            new PriceItem { Code = "SV-10", Description = "PLANTIO DE ARVORE", Unit = "UN", Price = 100m },
        },
    };

    private static List<AcquisitionRow> Rows() => new()
    {
        new AcquisitionRow { SpeciesId = "sp-ipe", Code = "AR-001", PopularName = "Ipê-amarelo", ScientificName = "Handroanthus albus", Quantity = 10, Reserve = 1, Unit = "muda", UnitPrice = 70m },
        new AcquisitionRow { SpeciesId = "sp-grama", Code = "GR-001", PopularName = "Grama-esmeralda", Quantity = 200, Reserve = 10, Unit = "m²", UnitPrice = 9.5m },
        new AcquisitionRow { SpeciesId = "", Code = "—", PopularName = "Família X", Quantity = 3, Unit = "muda" },
    };

    private static Species? Lookup(string id) => id switch { "sp-ipe" => Ipe, "sp-grama" => Grama, _ => null };

    [Fact]
    public void TablePriceWinsOverCatalogAndBdiIsApplied()
    {
        Budget budget = BudgetBuilder.Build(Rows(), Lookup, Table(), 25);

        BudgetRow ipe = budget.Rows.Single(r => r.SpeciesId == "sp-ipe" && !r.IsService);
        Assert.Equal(PriceSource.Tabela, ipe.Source);
        Assert.Equal(11, ipe.Quantity);              // compra = projeto + reserva
        Assert.Equal(50m, ipe.UnitCost);
        Assert.Equal(62.50m, ipe.UnitPrice);
        Assert.Equal(687.50m, ipe.Total);
        Assert.Equal(string.Empty, ipe.Note);         // "UN" e "muda" são a mesma unidade

        BudgetRow plantio = budget.Rows.Single(r => r.IsService);
        Assert.Equal(10, plantio.Quantity);          // serviço só para a quantidade de projeto
        Assert.Equal(125m, plantio.UnitPrice);
        Assert.Equal("2.1", plantio.Item);

        BudgetRow grama = budget.Rows.Single(r => r.SpeciesId == "sp-grama");
        Assert.Equal(PriceSource.Cadastro, grama.Source);
        Assert.Equal(11.88m, grama.UnitPrice);        // 9,50 × 1,25 = 11,875 → 11,88

        BudgetRow unknown = budget.Rows.Single(r => r.SpeciesCode == "—");
        Assert.Equal(PriceSource.SemPreco, unknown.Source);
        Assert.Equal(1, budget.MissingPrices);
        Assert.Equal(budget.SupplyTotal + budget.ServiceTotal, budget.Total);
        Assert.Equal(new[] { "1.1", "1.2", "1.3", "2.1" }, budget.Rows.Select(r => r.Item));
    }

    [Fact]
    public void WithoutTable_UsesCatalogAndExplainsMissingService()
    {
        Budget budget = BudgetBuilder.Build(Rows(), Lookup, null, 0);

        BudgetRow ipe = budget.Rows.Single(r => r.SpeciesId == "sp-ipe" && !r.IsService);
        Assert.Equal(PriceSource.Cadastro, ipe.Source);
        Assert.Equal(70m, ipe.UnitPrice);
        Assert.Contains("nenhuma tabela", ipe.Note);
        BudgetRow service = budget.Rows.Single(r => r.IsService);
        Assert.Equal(PriceSource.SemPreco, service.Source);
        Assert.Equal(0m, service.Total);
    }

    [Fact]
    public void UnitMismatchIsReported()
    {
        PriceTable table = Table();
        table.Items[0].Unit = "M2";
        Budget budget = BudgetBuilder.Build(Rows(), Lookup, table, 0);
        Assert.Contains("Unidade da tabela", budget.Rows.Single(r => r.SpeciesId == "sp-ipe" && !r.IsService).Note);
        Assert.True(BudgetBuilder.SameUnit("m²", "M2"));
        Assert.True(BudgetBuilder.SameUnit("und", "muda"));
        Assert.False(BudgetBuilder.SameUnit("un", "m²"));
    }
}

public class MaintenanceTests
{
    private static MaintenanceItem Item(Species s, int newCount = 5, int kept = 0, double area = 0, int reserve = 1) =>
        new() { Species = s, SpeciesCode = s.Code, NewCount = newCount, KeptCount = kept, AreaM2 = area, Reserve = reserve };

    private static readonly Species FloweringTree = new()
    {
        Code = "AR-001", PopularName = "Ipê-amarelo", ScientificName = "Handroanthus albus", Category = PlantCategory.Arvore,
        Water = WaterNeed.Media, Flowering = "ago–set",
    };

    [Fact]
    public void NewTree_GetsImplantationTasksRelativeToPlantingMonth()
    {
        IReadOnlyList<MaintenanceTask> tasks = MaintenancePlanner.Build(new[] { Item(FloweringTree) }, new MaintenanceSettings { PlantingMonth = 11 });

        MaintenanceTask watering = tasks.Single(t => t.Activity == MaintenanceActivity.Rega && t.Phase == MaintenancePhase.Implantacao);
        Assert.Equal(MonthSet.Parse("nov–jan"), watering.Months);

        MaintenanceTask replacement = tasks.Single(t => t.Activity == MaintenanceActivity.Reposicao);
        Assert.True(replacement.Months.Contains(12) && replacement.Months.Contains(2));
        Assert.Contains("1 un", replacement.Notes);

        Assert.Contains(tasks, t => t.Activity == MaintenanceActivity.Tutoramento);
        Assert.Contains(tasks, t => t.Activity == MaintenanceActivity.Coroamento);
        Assert.Contains(tasks, t => t.Activity == MaintenanceActivity.Poda && t.Phase == MaintenancePhase.Implantacao);
    }

    [Fact]
    public void FloweringTree_IsPrunedRightAfterFloweringAndFertilizedBefore()
    {
        IReadOnlyList<MaintenanceTask> tasks = MaintenancePlanner.Build(new[] { Item(FloweringTree, 0, kept: 3) }, new MaintenanceSettings());

        MaintenanceTask pruning = tasks.Single(t => t.Activity == MaintenanceActivity.Poda && t.Phase == MaintenancePhase.Permanente);
        Assert.Equal(MonthSet.Parse("out"), pruning.Months);           // floração ago–set → poda em outubro
        MaintenanceTask fertilizing = tasks.Single(t => t.Activity == MaintenanceActivity.Adubacao);
        Assert.True(fertilizing.Months.Contains(7));                    // pré-floração: julho

        // Só existentes: nada de implantação.
        Assert.DoesNotContain(tasks, t => t.Phase == MaintenancePhase.Implantacao);
    }

    [Fact]
    public void Lawn_IsMownMoreOftenInRainySeason()
    {
        var lawn = new Species { Code = "GR-001", PopularName = "Grama-esmeralda", Category = PlantCategory.Gramado, Water = WaterNeed.Media, SupplyUnit = "m²" };
        IReadOnlyList<MaintenanceTask> tasks = MaintenancePlanner.Build(new[] { Item(lawn, 0, area: 300) }, new MaintenanceSettings { DrySeason = "mai–set" });

        List<MaintenanceTask> mowing = tasks.Where(t => t.Activity == MaintenanceActivity.Corte).ToList();
        Assert.Equal(2, mowing.Count);
        Assert.Equal(MonthSet.Parse("out–abr"), mowing.Single(t => t.Frequency == "quinzenal").Months);
        Assert.Equal(MonthSet.Parse("mai–set"), mowing.Single(t => t.Frequency == "mensal").Months);
        Assert.Contains("m²", tasks.Single(t => t.Activity == MaintenanceActivity.Reposicao).Notes);
    }

    [Fact]
    public void DroughtTolerantSpecies_NeedNoDrySeasonWatering()
    {
        var cactus = new Species { Code = "HE-009", PopularName = "Agave", Category = PlantCategory.Herbacea, Water = WaterNeed.Baixa };
        IReadOnlyList<MaintenanceTask> tasks = MaintenancePlanner.Build(new[] { Item(cactus, 0, kept: 4) }, new MaintenanceSettings());
        Assert.DoesNotContain(tasks, t => t.Activity == MaintenanceActivity.Rega);
    }

    [Fact]
    public void EveryTaskHasMonthsAndSpeciesForWholeCatalog()
    {
        List<MaintenanceItem> items = Viridian.Library.SeedCatalog.CreateSpecies().Select(s => Item(s, 3, 2, 10)).ToList();
        IReadOnlyList<MaintenanceTask> tasks = MaintenancePlanner.Build(items, new MaintenanceSettings());

        Assert.All(tasks, t =>
        {
            Assert.False(t.Months.IsEmpty, t.Title);
            Assert.False(string.IsNullOrWhiteSpace(t.SpeciesCode));
            Assert.False(string.IsNullOrWhiteSpace(t.Frequency), t.Title);
        });
        Assert.All(items, i => Assert.Contains(tasks, t => t.SpeciesCode == i.SpeciesCode));
    }

    [Theory]
    [InlineData(12, 1, 1)]
    [InlineData(1, -1, 12)]
    [InlineData(6, 13, 7)]
    public void Month_WrapsAround(int month, int offset, int expected) => Assert.Equal(expected, MaintenancePlanner.Month(month, offset));
}
