using System.IO.Compression;
using System.Xml.Linq;
using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Detailing;
using Viridian.Core.Export;
using Viridian.Core.Geometry;
using Viridian.Core.Memorial;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.Tests;

public class PlantingDetailTests
{
    [Theory]
    [InlineData(0.8, 0.40)]
    [InlineData(2.0, 0.60)]
    [InlineData(2.5, 0.80)]
    [InlineData(4.0, 1.00)]
    public void TreePit_GrowsWithSeedlingHeight(double seedling, double pit)
    {
        var tree = new Species { Category = PlantCategory.Arvore, PlantingHeightM = seedling };
        PlantingDimensions d = PlantingDetails.Dimensions(tree);
        Assert.Equal(DetailKind.CovaArvore, d.Kind);
        Assert.Equal(pit, d.PitWidthM, 6);
        Assert.Equal(pit, d.PitDepthM, 6);
        Assert.True(d.RootBallM < d.PitWidthM);
        Assert.True(d.StakeHeightM > seedling * 0.5);
    }

    [Fact]
    public void EveryCatalogSpecies_ProducesADrawing()
    {
        var colors = new ColorSettings();
        foreach (Species s in SeedCatalog.CreateSpecies())
        {
            Drawing d = PlantingDetails.Draw(s, colors);
            Assert.NotEmpty(d.Lines);
            Assert.NotEmpty(d.Texts);
            Assert.All(d.Lines, l => Assert.True(double.IsFinite(l.A.X) && double.IsFinite(l.B.Y)));
        }
    }

    [Fact]
    public void Specification_FillsAllFieldsForEveryKind()
    {
        var settings = new SpecificationSettings();
        settings.Normalize();
        foreach (Species s in SeedCatalog.CreateSpecies().GroupBy(x => x.Category).Select(g => g.First()))
        {
            string text = SpecificationTemplates.Render(settings, s);
            Assert.DoesNotContain("{", text);
            Assert.Contains(s.PopularName, text);
        }

        Assert.Equal(new[] { "altrua" }, SpecificationTemplates.UnknownFields("Muda {altrua} {altura_muda}"));
    }
}

public class SectionAndGrowthTests
{
    private static Species Tree(double h, double crown, CrownForm form = CrownForm.Arredondada) =>
        new() { Id = Guid.NewGuid().ToString("N"), Code = "AR", PopularName = "Árvore", Category = PlantCategory.Arvore, AdultHeightM = h, CrownDiameterM = crown, CrownForm = form, Growth = GrowthRate.Medio };

    [Fact]
    public void Section_IncludesOnlyPlantsCutByTheLine()
    {
        Species s = Tree(10, 6);
        var plants = new[]
        {
            new SectionPlant(s, "A", new Point2(5, 1), 10, 6),   // copa cortada (1 < 3)
            new SectionPlant(s, "B", new Point2(12, 8), 10, 6),  // longe da linha
        };
        Drawing d = SchematicSection.Build(new Point2(0, 0), new Point2(20, 0), plants, Array.Empty<SectionMassing>(), new ColorSettings());

        Assert.Single(d.Texts, t => t.Text == "A");
        Assert.DoesNotContain(d.Texts, t => t.Text == "B");
        double top = d.Fills.Max(f => f.Outline.Max(p => p.Y));
        Assert.InRange(top, 9.9, 10.01);
    }

    [Fact]
    public void Section_MassingSpansFollowPolygon()
    {
        var square = new Polygon2(new[] { new Point2(2, -1), new Point2(5, -1), new Point2(5, 1), new Point2(2, 1) });
        var spans = SchematicSection.Spans(square, new Point2(0, 0), new Point2(10, 0));
        (double from, double to) = Assert.Single(spans);
        Assert.Equal(2, from, 6);
        Assert.Equal(5, to, 6);
    }

    [Theory]
    [InlineData(GrowthRate.Lento)]
    [InlineData(GrowthRate.Medio)]
    [InlineData(GrowthRate.Rapido)]
    public void Growth_IsMonotonicAndEndsAdult(GrowthRate rate)
    {
        var s = Tree(12, 8);
        s.Growth = rate;
        s.PlantingHeightM = 2;
        double previousCrown = 0, previousHeight = 0;
        foreach (GrowthStage stage in GrowthModel.Stages)
        {
            (double crown, double height) = GrowthModel.Size(s, stage);
            Assert.True(crown >= previousCrown && height >= previousHeight);
            (previousCrown, previousHeight) = (crown, height);
        }

        Assert.Equal(8, previousCrown, 6);
        Assert.Equal(12, previousHeight, 6);
        Assert.Equal(2, GrowthModel.Size(s, GrowthStage.Plantio).HeightM, 6);
    }

    [Fact]
    public void FastSpecies_GrowFasterThanSlow()
    {
        var fast = Tree(10, 8);
        fast.Growth = GrowthRate.Rapido;
        var slow = Tree(10, 8);
        slow.Growth = GrowthRate.Lento;
        Assert.True(GrowthModel.Size(fast, GrowthStage.TresAnos).CrownM > GrowthModel.Size(slow, GrowthStage.TresAnos).CrownM);
    }

    [Fact]
    public void GrowthBoard_DrawsFourPanels()
    {
        Species s = Tree(8, 6);
        var plants = new[] { new GrowthPlant(s, new Point2(0, 0)), new GrowthPlant(s, new Point2(6, 0)) };
        Drawing d = GrowthBoard.Build(null, plants, Array.Empty<GrowthMassing>(), new ColorSettings());
        Assert.Equal(8, d.Fills.Count);
        Assert.Contains(d.Texts, t => t.Text == "MATURIDADE");
        Assert.Contains(d.Texts, t => t.Text == "PLANTIO");
    }
}

public class MemorialTests
{
    [Fact]
    public void Memorial_IsAValidWordDocumentWithPhotosAndSections()
    {
        List<Species> catalog = SeedCatalog.CreateSpecies();
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Viridian.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        string photos = Path.Combine(root!, "src", "Viridian.Addin", "Fotos");
        var species = catalog.Where(s => !string.IsNullOrEmpty(s.PhotoFile)).GroupBy(s => s.Category).Select(g => g.First())
            .Select(s => new MemorialSpecies(s, s.Code, 3, s.SupplyUnit, Path.Combine(photos, s.PhotoFile)))
            .ToList();
        var input = new MemorialInput
        {
            Project = new MemorialProject { Name = "Residência Teste", Number = "PAI-2026-01", Client = "Cliente & Filhos <Ltda>" },
            Species = species,
            MassingAreaM2 = 42.5,
        };

        IReadOnlyList<DocBlock> blocks = MemorialBuilder.Build(input);
        string file = Path.Combine(Path.GetTempPath(), "viridian-memorial-" + Guid.NewGuid().ToString("N") + ".docx");
        try
        {
            DocxWriter.Write(file, blocks, "Memorial");
            using ZipArchive zip = ZipFile.OpenRead(file);
            XDocument document;
            using (Stream s = zip.GetEntry("word/document.xml")!.Open())
            {
                document = XDocument.Load(s); // XML bem formado
            }

            string text = string.Concat(document.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
            Assert.Contains("Memorial descritivo de paisagismo", text);
            Assert.Contains("Cliente & Filhos <Ltda>", text);
            Assert.Contains("Calendário de floração", text);
            Assert.Contains("Créditos das fotografias", text);
            Assert.Contains(species[0].Species.ScientificName, text);

            int images = zip.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
            Assert.True(images >= species.Count, $"{images} imagens");
            int drawings = document.Descendants().Count(e => e.Name.LocalName == "blip");
            Assert.Equal(images, drawings);
            Assert.NotNull(zip.GetEntry("word/styles.xml"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ImageInfo_ReadsJpegSize()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Viridian.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        string any = Directory.GetFiles(Path.Combine(root!, "src", "Viridian.Addin", "Fotos"), "*.jpg").First();
        Assert.True(ImageInfo.TryRead(any, out int w, out int h, out string ext));
        Assert.Equal("jpeg", ext);
        Assert.True(Math.Max(w, h) <= 360 && Math.Min(w, h) > 50);
    }
}

public class CalendarDrawingAndGeometryTests
{
    [Fact]
    public void CalendarDrawing_FillsFloweringMonths()
    {
        var a = new Species { Id = "a", PopularName = "Ipê", Flowering = "ago–set", FlowerColor = "#F2C230" };
        var b = new Species { Id = "b", PopularName = "Grama", Flowering = "" };
        FloweringCalendar cal = FloweringCalendar.Build(new[] { (a, "AR-001", 1.0), (b, "GR-001", 10.0) });
        Drawing d = CalendarDrawing.Build(cal, new ColorSettings(), "Calendário");
        Assert.Equal(2, d.Fills.Count);
        Assert.All(d.Fills, f => Assert.Equal("#F2C230", f.Color.Hex));
        Assert.Contains(d.Texts, t => t.Text.Contains("Grama", StringComparison.Ordinal) && t.Text.StartsWith("Sem época", StringComparison.Ordinal));
    }

    [Fact]
    public void OrientedSize_MeasuresRotatedBed()
    {
        // Canteiro de 2 × 10 m girado 30°.
        double c = Math.Cos(Math.PI / 6), s = Math.Sin(Math.PI / 6);
        Point2 R(double x, double y) => new(x * c - y * s, x * s + y * c);
        var bed = new Polygon2(new[] { R(0, 0), R(10, 0), R(10, 2), R(0, 2) });
        (double w, double l) = bed.OrientedSize();
        Assert.Equal(2, w, 6);
        Assert.Equal(10, l, 6);
    }
}

public class MemorialTextTests
{
    [Theory]
    [InlineData("muda", 3, "mudas")]
    [InlineData("muda", 1, "muda")]
    [InlineData("unidade", 2, "unidades")]
    [InlineData("un", 5, "un")]
    [InlineData("bandejas", 4, "bandejas")]
    public void Units_ArePluralized(string unit, double quantity, string expected) =>
        Assert.Equal(expected, MemorialBuilder.Plural(unit, quantity));
}
