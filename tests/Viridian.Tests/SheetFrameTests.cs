using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Sheets;
using Xunit;

namespace Viridian.Tests;

public class SheetFrameTests
{
    private static SheetFrameInfo Info() => new()
    {
        Project = "Residência Teste", ProjectNumber = "P-01", Client = "Cliente", Address = "Rua A, 1", Author = "Autora",
        SheetName = "Planta de paisagismo", SheetNumber = "PSG-03", Date = "26/11/2026", Scale = 100, NorthDeg = 30,
        Notes = SheetFrame.DefaultNotes,
    };

    [Fact]
    public void Frame_StaysInsideTheA1SheetAndCarriesTheStampData()
    {
        Drawing d = SheetFrame.Build(Info());
        Bounds2 e = d.Extent();
        Assert.InRange(e.MinX, -1e-9, 0.001);
        Assert.InRange(e.MaxX, 0.840, 0.8411);
        Assert.InRange(e.MaxY, 0.593, 0.5941);
        Assert.Contains(d.Texts, t => t.Text == "PSG-03");
        Assert.Contains(d.Texts, t => t.Text == "Residência Teste");
        Assert.Contains(d.Texts, t => t.Text == "1:100");
        Assert.Contains(d.Texts, t => t.Text == "N");
        Assert.Contains(d.Texts, t => t.Text.StartsWith("1. Medidas em metros", StringComparison.Ordinal));
        Assert.Contains(d.Texts, t => t.Text == "4 m");                   // escala gráfica 0–4 m em 1:100
        Assert.All(d.Texts, t => Assert.True(t.At.X >= 0 && t.At.X <= 0.841 && t.At.Y >= 0 && t.At.Y <= 0.594, t.Text));
    }

    [Fact]
    public void Frame_LongTextsAreShortened()
    {
        SheetFrameInfo info = Info();
        info.Address = new string('x', 200);
        Drawing d = SheetFrame.Build(info);
        Assert.Contains(d.Texts, t => t.Text.EndsWith("…", StringComparison.Ordinal) && t.Text.Length <= 60);
    }

    [Theory]
    [InlineData(20, 0.5)]
    [InlineData(100, 1)]
    [InlineData(250, 2.5)]
    [InlineData(500, 5)]
    [InlineData(2000, 20)]
    public void ScaleBar_UnitGivesAtLeast40mm(int scale, double unit)
    {
        Assert.Equal(unit, SheetFrame.ScaleUnit(scale));
        Assert.True(unit * 4 * 1000 / scale >= 40);
    }

    [Fact]
    public void Layout_ReservesTheDrawnColumnAndItsTopBand()
    {
        var input = new SheetLayoutInput
        {
            Usable = SheetFrame.Usable,
            PlanWidthMm = 400, PlanHeightMm = 300,
            LegendWidthMm = 120, LegendHeightMm = 80,
            MinColumnWidthMm = SheetFrame.ColumnMm - 10,
            ColumnTopInsetMm = 30,
            StampReserveMm = SheetFrame.StampBlockMm,
        };
        SheetLayoutResult r = SheetLayout.Compute(input);
        Assert.Equal(SheetFrame.ColumnMm - 10, r.ColumnWidthMm);
        Assert.True(r.PlanArea.MaxX <= SheetFrame.Usable.MaxX - (SheetFrame.ColumnMm - 10));
        Assert.Equal(SheetFrame.Usable.MaxY - 30, r.Legend!.Value.MaxY, 6);
        Assert.True(r.PlanFits);
        Assert.True(r.ColumnFits);
    }
}
