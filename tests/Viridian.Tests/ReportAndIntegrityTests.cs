using System.IO.Compression;
using System.Xml.Linq;
using Viridian.Core.Distribution;
using Viridian.Core.Export;
using Viridian.Core.Geometry;
using Viridian.Core.Integrity;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.Tests;

public class ReportTests
{
    private static PlantRecord Plant(long id, string speciesId, string code, PlantStatus status = PlantStatus.Novo, string massing = "", double x = 0, double y = 0) => new()
    {
        ElementId = id,
        SpeciesId = speciesId,
        SpeciesCode = code,
        PopularName = "Nome " + code,
        ScientificName = "Cientifico " + code,
        CategoryName = "Árvore",
        ProjectCode = "AR." + id.ToString("0000"),
        Status = status,
        MassingId = massing,
        Location = new Point2(x, y),
        CrownDiameterM = 6,
        RecommendedSpacingM = 5,
        SupplyUnit = "muda",
        FamilyName = "VIR_" + code,
        TypeName = "Tipo",
        LevelName = "Térreo",
    };

    private static MassingInfo Massing(long id, string code, string speciesId, int quantity, long view = 1, double reserve = 10) => new()
    {
        ElementId = id,
        ViewId = view,
        ViewName = "Planta " + view,
        Record = new MassingRecord
        {
            Id = code,
            SpeciesId = speciesId,
            SpeciesCode = "FO-001",
            PopularName = "Grama-amendoim",
            ScientificName = "Arachis repens",
            AreaM2 = quantity / 16.0,
            DensityPerM2 = 16,
            Quantity = quantity,
            SupplyUnit = "muda",
            ReservePercent = reserve,
            Mode = MassingMode.Hachura,
        },
        Boundary = new Polygon2(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) }),
    };

    [Fact]
    public void SpeciesReportCountsByStatusAndIncludesMassings()
    {
        var plants = new[]
        {
            Plant(1, "a", "AR-001"),
            Plant(2, "a", "AR-001"),
            Plant(3, "a", "AR-001", PlantStatus.Existente),
            Plant(4, "a", "AR-001", PlantStatus.Removido),
            Plant(5, "a", "AR-001", PlantStatus.Transplantado),
            Plant(6, "fo", "FO-001", massing: "MC-001"),
            Plant(7, "fo", "FO-001", massing: "MC-404"),
            Plant(8, string.Empty, string.Empty),
        };
        var massings = new[] { Massing(100, "MC-001", "fo", 160), Massing(101, "MC-001", "fo", 160, view: 2) };

        IReadOnlyList<SpeciesReportRow> rows = ReportBuilder.BuildSpecies(plants, massings);
        SpeciesReportRow tree = rows.Single(r => r.Code == "AR-001");
        Assert.Equal((2, 1, 1, 1, 0, 4), (tree.New, tree.Existing, tree.Transplanted, tree.Removed, tree.InMassings, tree.Total));

        // O maciço aparece em duas vistas, mas conta uma vez; o símbolo órfão conta como exemplar isolado.
        SpeciesReportRow cover = rows.Single(r => r.Code == "FO-001");
        Assert.Equal(160, cover.InMassings);
        Assert.Equal(1, cover.New);
        Assert.Equal(161, cover.Total);

        Assert.Contains(rows, r => r.Code == "—" && r.New == 1);
    }

    [Fact]
    public void MassingReportListsEachMassingOnce()
    {
        var massings = new[] { Massing(100, "MC-002", "fo", 80), Massing(101, "MC-001", "fo", 160), Massing(102, "mc-001", "fo", 160, view: 2) };
        IReadOnlyList<MassingReportRow> rows = ReportBuilder.BuildMassings(massings);
        Assert.Equal(new[] { "MC-001", "MC-002" }, rows.Select(r => r.Id));
        Assert.Equal(16, rows[0].DensityPerM2, 9);
        Assert.Equal(10, rows[0].AreaM2, 9);
    }

    [Fact]
    public void AcquisitionAppliesReserveAndPrices()
    {
        var plants = new[]
        {
            Plant(1, "a", "AR-001"),
            Plant(2, "a", "AR-001"),
            Plant(3, "a", "AR-001", PlantStatus.Existente),
            Plant(4, "fo", "FO-001", massing: "MC-001"),
        };
        var massings = new[] { Massing(100, "MC-001", "fo", 160, reserve: 10) };
        IReadOnlyList<AcquisitionRow> rows = ReportBuilder.BuildAcquisition(
            plants, massings, id => id == "a" ? 120.50m : 2.75m, individualReservePercent: 50);

        AcquisitionRow tree = rows.Single(r => r.Code == "AR-001");
        Assert.Equal((2, 1, 3), (tree.Quantity, tree.Reserve, tree.PurchaseQuantity));
        Assert.Equal(361.50m, tree.Total);

        AcquisitionRow cover = rows.Single(r => r.Code == "FO-001");
        Assert.Equal((160, 16, 176), (cover.Quantity, cover.Reserve, cover.PurchaseQuantity));
        Assert.Equal(484.00m, cover.Total);
    }

    [Fact]
    public void AreaBasedUnitsAreRecognised()
    {
        Assert.True(SupplyUnits.IsAreaBased("m²"));
        Assert.True(SupplyUnits.IsAreaBased(" M2 "));
        Assert.False(SupplyUnits.IsAreaBased("muda"));
        Assert.False(SupplyUnits.IsAreaBased(null));
    }

    [Fact]
    public void XlsxFileIsAWellFormedPackage()
    {
        var sheet = new XlsxSheet("Espécies / Quantitativos: [teste]");
        sheet.TitleLines.Add("Relatório por espécie");
        sheet.TitleLines.Add("Projeto <Praça & Jardim>");
        sheet.Columns.Add(new XlsxColumn("Código", 12));
        sheet.Columns.Add(new XlsxColumn("Quantidade", 12, XlsxFormat.Integer));
        sheet.Columns.Add(new XlsxColumn("Área (m²)", 12, XlsxFormat.Decimal));
        sheet.Columns.Add(new XlsxColumn("Preço", 12, XlsxFormat.Currency));
        sheet.Rows.Add(new XlsxRow("AR-001", 12, 45.375, 120.5m));
        sheet.Rows.Add(new XlsxRow("Ipê \"amarelo\" <&>", null, double.NaN, 0m));
        sheet.Rows.Add(new XlsxRow("Total", 12, 45.375, 120.5m) { Bold = true });
        var second = new XlsxSheet("Espécies / Quantitativos: [teste]");
        second.Columns.Add(new XlsxColumn("Vazio"));

        using var stream = new MemoryStream();
        XlsxWriter.Write(stream, new[] { sheet, second });
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/styles.xml", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
        Assert.Contains("xl/worksheets/sheet2.xml", names);

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream s = entry.Open();
            XDocument.Load(s); // precisa ser XML bem formado
        }

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using Stream workbook = zip.GetEntry("xl/workbook.xml")!.Open();
        var sheetNames = XDocument.Load(workbook).Descendants(ns + "sheet").Select(e => e.Attribute("name")!.Value).ToList();
        Assert.Equal(2, sheetNames.Distinct().Count());
        Assert.All(sheetNames, n => Assert.True(n.Length <= 31 && !n.Contains('/') && !n.Contains('[')));

        using Stream sheetXml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XDocument doc = XDocument.Load(sheetXml);
        var cells = doc.Descendants(ns + "c").ToList();
        Assert.Contains(cells, c => c.Attribute("r")!.Value == "B5" && c.Element(ns + "v")!.Value == "12");
        Assert.Contains(cells, c => c.Attribute("r")!.Value == "C5" && c.Element(ns + "v")!.Value == "45.375");
        Assert.Contains(cells, c => c.Attribute("r")!.Value == "A6" && c.Value == "Ipê \"amarelo\" <&>");
        Assert.Equal(4, doc.Descendants(ns + "col").Count());
    }

    [Fact]
    public void XlsxRequiresAtLeastOneSheet()
    {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => XlsxWriter.Write(stream, Array.Empty<XlsxSheet>()));
    }
}

public class IntegrityTests
{
    private static PlantRecord Plant(long id, double x, double y, string speciesId = "a", string code = "AR.0001", string massing = "", double crown = 6, double spacing = 5) => new()
    {
        ElementId = id,
        SpeciesId = speciesId,
        SpeciesCode = "AR-001",
        PopularName = "Ipê",
        ScientificName = "Handroanthus",
        ProjectCode = code,
        MassingId = massing,
        Location = new Point2(x, y),
        CrownDiameterM = crown,
        RecommendedSpacingM = spacing,
        FamilyName = "VIR_AR",
        TypeName = "Ipê",
        LevelName = "Térreo",
    };

    private static Species Known(string id) => new() { Id = id, Code = "AR-001", PopularName = "Ipê", ScientificName = "Handroanthus" };

    private static IReadOnlyList<IntegrityIssue> Run(IEnumerable<PlantRecord> plants, IEnumerable<MassingInfo>? massings = null, AnalysisSettings? settings = null) =>
        IntegrityChecker.Run(new IntegrityInput
        {
            Plants = plants.ToList(),
            Massings = (massings ?? Array.Empty<MassingInfo>()).ToList(),
            SpeciesLookup = id => id == "a" ? Known(id) : null,
            Settings = settings ?? new AnalysisSettings(),
        });

    [Fact]
    public void CleanProjectHasNoIssues()
    {
        var plants = new[] { Plant(1, 0, 0, code: "AR.0001"), Plant(2, 20, 0, code: "AR.0002"), Plant(3, 40, 0, code: "AR.0003") };
        Assert.Empty(Run(plants));
    }

    [Fact]
    public void MissingAndDuplicateCodesAreReported()
    {
        var plants = new[] { Plant(1, 0, 0, code: string.Empty), Plant(2, 20, 0, code: "AR.0002"), Plant(3, 40, 0, code: "ar.0002") };
        IReadOnlyList<IntegrityIssue> issues = Run(plants);
        Assert.Contains(issues, i => i.Kind == IssueKind.SemCodigo && i.ElementIds.SequenceEqual(new long[] { 1 }));
        Assert.Contains(issues, i => i.Kind == IssueKind.CodigoDuplicado && i.ElementIds.Count == 2);
    }

    [Fact]
    public void UnregisteredFamiliesAndUnknownSpeciesAreReported()
    {
        var plants = new[] { Plant(1, 0, 0, speciesId: string.Empty, code: "X1"), Plant(2, 30, 0, speciesId: "zz", code: "X2") };
        IReadOnlyList<IntegrityIssue> issues = Run(plants);
        Assert.Contains(issues, i => i.Kind == IssueKind.FamiliaNaoCadastrada);
        Assert.Contains(issues, i => i.Kind == IssueKind.EspecieSemIdentificacao);
    }

    [Fact]
    public void DuplicatedElementsAreReported()
    {
        var plants = new[] { Plant(1, 5, 5, code: "A1"), Plant(2, 5.005, 5, code: "A2"), Plant(3, 50, 5, code: "A3") };
        IReadOnlyList<IntegrityIssue> issues = Run(plants);
        IntegrityIssue duplicate = Assert.Single(issues, i => i.Kind == IssueKind.ElementoDuplicado);
        Assert.Equal(new long[] { 1, 2 }, duplicate.ElementIds.OrderBy(x => x));
        Assert.DoesNotContain(issues, i => i.Kind == IssueKind.ConflitoDeCopa);
    }

    [Fact]
    public void CrownConflictsAndSpacingUseTolerances()
    {
        // Copas de 6 m: soma dos raios = 6 m; com 20% de tolerância o mínimo é 4,8 m.
        var close = new[] { Plant(1, 0, 0, code: "A1"), Plant(2, 4.0, 0, code: "A2") };
        IReadOnlyList<IntegrityIssue> issues = Run(close);
        Assert.Contains(issues, i => i.Kind == IssueKind.ConflitoDeCopa);
        Assert.Contains(issues, i => i.Kind == IssueKind.EspacamentoInsuficiente);

        var ok = new[] { Plant(1, 0, 0, code: "A1"), Plant(2, 5.0, 0, code: "A2") };
        Assert.Empty(Run(ok));

        var disabled = new AnalysisSettings { CheckCrownConflicts = false, CheckSpacing = false };
        Assert.Empty(Run(close, settings: disabled));

        // Arbustos pequenos não entram na verificação de copa.
        var shrubs = new[] { Plant(1, 0, 0, code: "A1", crown: 1, spacing: 0), Plant(2, 0.5, 0, code: "A2", crown: 1, spacing: 0) };
        Assert.Empty(Run(shrubs));
    }

    [Fact]
    public void MassingProblemsAreReported()
    {
        var square = new Polygon2(new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10), new Point2(0, 10) });
        var bowtie = new Polygon2(new[] { new Point2(0, 0), new Point2(4, 4), new Point2(4, 0), new Point2(0, 4) });
        var massings = new[]
        {
            new MassingInfo { ElementId = 10, ViewId = 1, Record = new MassingRecord { Id = "MC-001", SpacingM = 0.25, Pattern = DistributionPattern.Triangular }, Boundary = square },
            new MassingInfo { ElementId = 11, ViewId = 1, Record = new MassingRecord { Id = "MC-001", SpacingM = 0.25 }, Boundary = square },
            new MassingInfo { ElementId = 12, ViewId = 1, Record = new MassingRecord { Id = "MC-002" }, Boundary = square },
            new MassingInfo { ElementId = 13, ViewId = 1, Record = new MassingRecord { Id = "MC-003", DensityPerM2 = 4 }, Boundary = null },
            new MassingInfo { ElementId = 14, ViewId = 1, Record = new MassingRecord { Id = "MC-004", DensityPerM2 = 4 }, Boundary = bowtie },
            new MassingInfo { ElementId = 15, ViewId = 1, Record = new MassingRecord { Id = "MC-005", SupplyUnit = "m²" }, Boundary = square },
        };
        var plants = new[]
        {
            Plant(1, 5, 5, code: "A1", massing: "MC-002"),
            Plant(2, 50, 50, code: "A2", massing: "MC-002"),
            Plant(3, 5, 6, code: "A3", massing: "MC-999"),
        };

        IReadOnlyList<IntegrityIssue> issues = Run(plants, massings);
        Assert.Contains(issues, i => i.Kind == IssueKind.MacicoDuplicado && i.ElementIds.Count == 2);
        Assert.Contains(issues, i => i.Kind == IssueKind.MacicoSemDensidade && i.ElementIds.Contains(12));
        Assert.DoesNotContain(issues, i => i.Kind == IssueKind.MacicoSemDensidade && i.ElementIds.Contains(15));
        Assert.Contains(issues, i => i.Kind == IssueKind.LimiteInvalido && i.ElementIds.Contains(13));
        Assert.Contains(issues, i => i.Kind == IssueKind.LimiteInvalido && i.ElementIds.Contains(14));
        Assert.Contains(issues, i => i.Kind == IssueKind.ForaDoCanteiro && i.ElementIds.SequenceEqual(new long[] { 2 }));
        Assert.Contains(issues, i => i.Kind == IssueKind.RegistroOrfao && i.ElementIds.SequenceEqual(new long[] { 3 }));
    }

    [Fact]
    public void OrphanLabelsAndOutdatedTypesAreReported()
    {
        IReadOnlyList<IntegrityIssue> issues = IntegrityChecker.Run(new IntegrityInput
        {
            OrphanLabels = new[] { new OrphanLabel { ElementId = 77, Text = "AR-001\nIpê" } },
            OutdatedTypes = new[] { new OutdatedType { ElementId = 88, Name = "Ipê", Reason = "nome popular" } },
        });
        Assert.Contains(issues, i => i.Kind == IssueKind.EtiquetaOrfa && i.ElementIds.Contains(77));
        Assert.Contains(issues, i => i.Kind == IssueKind.DadosDesatualizados && i.ElementIds.Contains(88));
        Assert.All(issues, i => Assert.False(string.IsNullOrWhiteSpace(i.KindName)));
    }

    [Fact]
    public void LongListsAreTruncatedWithASummary()
    {
        var plants = Enumerable.Range(1, IntegrityChecker.MaxPerKind + 50).Select(i => Plant(i, i * 30, 0, code: string.Empty)).ToList();
        IReadOnlyList<IntegrityIssue> issues = Run(plants);
        Assert.Equal(IntegrityChecker.MaxPerKind + 1, issues.Count(i => i.Kind == IssueKind.SemCodigo));
        Assert.Contains(issues, i => i.Kind == IssueKind.SemCodigo && i.Severity == IssueSeverity.Info && i.Message.Contains("50"));
    }
}

public class ReportSheetTests
{
    [Fact]
    public void SheetsHaveTotalsMatchingTheRows()
    {
        var species = new[]
        {
            new SpeciesReportRow { Code = "AR-001", PopularName = "Ipê", New = 3, Existing = 1, InMassings = 0, Unit = "muda" },
            new SpeciesReportRow { Code = "FO-001", PopularName = "Grama", New = 0, InMassings = 160, Unit = "muda" },
        };
        var massings = new[] { new MassingReportRow { Id = "MC-001", AreaM2 = 10.004, Quantity = 160 } };
        var acquisition = new[] { new AcquisitionRow { Code = "AR-001", Quantity = 3, Reserve = 1, UnitPrice = 10m } };

        IReadOnlyList<XlsxSheet> sheets = ReportSheets.Build("Praça", species, massings, acquisition, new DateTime(2026, 11, 17, 10, 30, 0));
        Assert.Equal(3, sheets.Count);
        Assert.Equal(new[] { "Por espécie", "Maciços", "Aquisição" }, sheets.Select(s => s.Name));
        Assert.Contains("17/11/2026 10:30", sheets[0].TitleLines[1]);

        XlsxRow speciesTotal = sheets[0].Rows[^1];
        Assert.True(speciesTotal.Bold);
        Assert.Equal(164, speciesTotal.Cells[9]);
        Assert.Equal(sheets[0].Columns.Count, speciesTotal.Cells.Length);

        XlsxRow massingTotal = sheets[1].Rows[^1];
        Assert.Equal(10.0, massingTotal.Cells[4]);
        Assert.Equal(160, massingTotal.Cells[7]);

        XlsxRow purchaseTotal = sheets[2].Rows[^1];
        Assert.Equal(4, purchaseTotal.Cells[5]);
        Assert.Equal(40m, purchaseTotal.Cells[8]);
        Assert.All(sheets, s => Assert.All(s.Rows, r => Assert.Equal(s.Columns.Count, r.Cells.Length)));

        using var stream = new MemoryStream();
        XlsxWriter.Write(stream, sheets);
        Assert.True(stream.Length > 1000);
    }
}
