using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Export;
using Viridian.Core.Model;
using Viridian.Core.Sheets;

namespace Viridian.Tests;

public class SheetLayoutTests
{
    // Folha A1 deitada.
    private static readonly PaperRect A1 = new(0, 0, 841, 594);

    [Fact]
    public void UsableArea_FollowsNbrMargins()
    {
        PaperRect usable = SheetLayout.UsableArea(A1);
        Assert.Equal(25, usable.MinX);
        Assert.Equal(10, usable.MinY);
        Assert.Equal(831, usable.MaxX);
        Assert.Equal(584, usable.MaxY);
    }

    [Fact]
    public void Layout_PlanLeftLegendAndScheduleOnRightColumn()
    {
        var input = new SheetLayoutInput
        {
            Usable = SheetLayout.UsableArea(A1),
            PlanWidthMm = 500, PlanHeightMm = 400,
            LegendWidthMm = 190, LegendHeightMm = 200,
            ScheduleWidthMm = 170, ScheduleHeightMm = 120,
        };

        SheetLayoutResult result = SheetLayout.Compute(input);

        Assert.True(result.PlanFits);
        Assert.True(result.ColumnFits);
        Assert.Empty(result.Warnings);
        PaperRect legend = result.Legend!.Value;
        PaperRect schedule = result.Schedule!.Value;
        Assert.Equal(input.Usable.MaxY, legend.MaxY, 6);           // legenda no alto
        Assert.Equal(input.Usable.MaxX - 190, legend.MinX, 6);     // coluna encostada à direita
        Assert.Equal(legend.MinY - 8, schedule.MaxY, 6);           // tabela logo abaixo
        Assert.False(result.Plan.Overlaps(legend));
        Assert.False(result.Plan.Overlaps(schedule));
        Assert.True(input.Usable.Contains(result.Plan));
        Assert.True(schedule.MinY >= input.Usable.MinY + input.StampReserveMm); // carimbo livre
    }

    [Fact]
    public void Layout_WarnsWhenPlanOrColumnDoNotFit()
    {
        var input = new SheetLayoutInput
        {
            Usable = SheetLayout.UsableArea(A1),
            PlanWidthMm = 900, PlanHeightMm = 300,
            LegendWidthMm = 190, LegendHeightMm = 450,
            ScheduleWidthMm = 170, ScheduleHeightMm = 120,
        };

        SheetLayoutResult result = SheetLayout.Compute(input);

        Assert.False(result.PlanFits);
        Assert.False(result.ColumnFits);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Layout_WithoutSideColumnUsesWholeSheet()
    {
        var input = new SheetLayoutInput { Usable = SheetLayout.UsableArea(A1), PlanWidthMm = 780, PlanHeightMm = 500 };
        SheetLayoutResult result = SheetLayout.Compute(input);
        Assert.True(result.PlanFits);
        Assert.Null(result.Legend);
        Assert.Equal(input.Usable.CenterX, result.Plan.CenterX, 6);
    }

    [Theory]
    [InlineData(40, 30, 600, 500, 100, 100)]   // já cabe em 1:100
    [InlineData(80, 50, 600, 500, 100, 150)]   // 1:100 = 800 mm → precisa de 1:150
    [InlineData(80, 50, 600, 500, 0, 150)]
    [InlineData(5000, 5000, 600, 500, 100, 0)] // nem 1:5000 serve
    public void SuggestScale_PicksNextStandardScaleThatFits(double w, double h, double aw, double ah, int current, int expected) =>
        Assert.Equal(expected, SheetLayout.SuggestScale(w, h, aw, ah, current));

    [Fact]
    public void Numbering_SkipsUsedNumbers()
    {
        Assert.Equal("PAI-01", SheetNumbering.Next(Array.Empty<string>()));
        Assert.Equal("PAI-03", SheetNumbering.Next(new[] { "PAI-01", "pai-02", "ARQ-01" }));
        Assert.Equal("PSG-001", SheetNumbering.Next(new[] { "A01" }, "PSG-", 3));
    }
}

public class DxfTests
{
    private static List<(int Code, string Value)> Pairs(string text)
    {
        string[] lines = text.Split("\r\n");
        var pairs = new List<(int, string)>();
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            pairs.Add((int.Parse(lines[i].Trim(), System.Globalization.CultureInfo.InvariantCulture), lines[i + 1]));
        }

        return pairs;
    }

    [Fact]
    public void SymbolLibrary_HasValidStructureAndOneBlockPerSpecies()
    {
        List<Species> species = Viridian.Library.SeedCatalog.CreateSpecies().Take(12).ToList();
        var settings = new ViridianSettings();
        settings.Normalize();

        string text = SymbolDxf.Build(species.Select(s => (s, s.Code)).ToList(), settings, includeFill: true).Build();
        List<(int Code, string Value)> pairs = Pairs(text);

        Assert.Equal((0, "EOF"), pairs[^1]);
        Assert.Equal(4, pairs.Count(p => p == (0, "SECTION")));
        Assert.Equal(4, pairs.Count(p => p == (0, "ENDSEC")));
        Assert.Contains((1, "AC1009"), pairs);
        Assert.Equal(12, pairs.Count(p => p == (0, "BLOCK")));
        Assert.Equal(12, pairs.Count(p => p == (0, "ENDBLK")));
        Assert.Equal(12, pairs.Count(p => p == (0, "INSERT")));
        Assert.Contains(pairs, p => p == (0, "SOLID"));
        Assert.Contains(pairs, p => p == (0, "ARC") || p == (0, "LINE"));
        Assert.Contains((2, "VIR_" + species[0].Code.ToUpperInvariant()), pairs);

        // Todo valor numérico de coordenada usa ponto decimal.
        Assert.All(pairs.Where(p => p.Code is >= 10 and <= 59), p =>
            Assert.True(double.TryParse(p.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _), p.Value));
    }

    [Fact]
    public void WithoutFill_HasOnlyLines()
    {
        Species s = Viridian.Library.SeedCatalog.CreateSpecies().First();
        string text = SymbolDxf.Build(new[] { (s, s.Code) }, new ViridianSettings(), includeFill: false).Build();
        Assert.DoesNotContain("SOLID", text);
    }

    [Fact]
    public void BlockNames_AreSafeAndUnique()
    {
        var species = new Species { ScientificName = "Ipê × híbrido" };
        Assert.Equal("VIR_IPE___HIBRIDO", SymbolDxf.BlockName(string.Empty, species));
        Assert.Equal("VIR_AR-001", SymbolDxf.BlockName("ar-001", species));
    }

    [Fact]
    public void SaveUsesWindows1252ForAccents()
    {
        var dxf = new DxfWriter();
        dxf.AddEntity(w => w.Text("0", new Viridian.Core.Geometry.Point2(0, 0), 1, "Ipê-roxo"));
        string file = Path.Combine(Path.GetTempPath(), "viridian-" + Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            dxf.Save(file);
            byte[] bytes = File.ReadAllBytes(file);
            Assert.Contains((byte)0xEA, bytes); // ê em Windows-1252
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(255, 0, 0, 1)]
    [InlineData(0, 0, 255, 5)]
    public void NearestAci_FindsPrimaryColors(byte r, byte g, byte b, int expected) =>
        Assert.Equal(expected, DxfWriter.NearestAci(new Rgb(r, g, b)));

    [Fact]
    public void NearestAci_KeepsGreensGreen()
    {
        Rgb aci = DxfWriter.AciColor(DxfWriter.NearestAci(new Rgb(0x4E, 0x8A, 0x3E)));
        Assert.True(aci.G > aci.R && aci.G > aci.B, aci.Hex);
        Assert.Equal(new Rgb(255, 127, 127).Hex, DxfWriter.AciColor(11).Hex);
    }
}
