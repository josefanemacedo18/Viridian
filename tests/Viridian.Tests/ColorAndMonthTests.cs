using Viridian.Core.Colors;
using Viridian.Core.Model;

namespace Viridian.Tests;

public class ColorTests
{
    [Fact]
    public void HexRoundTripAndMixing()
    {
        Assert.True(Rgb.TryParse("#4e8a3e", out Rgb c));
        Assert.Equal("#4E8A3E", c.Hex);
        Assert.False(Rgb.TryParse("verde", out _));
        Assert.Equal(Rgb.White, c.Tint(1));
        Assert.Equal(Rgb.Black, c.Shade(1));
        Assert.Equal(c, c.Mix(Rgb.White, 0));
        Assert.Equal(c, c.Mix(Rgb.White, double.NaN));
    }

    [Fact]
    public void HslRoundTripIsStable()
    {
        foreach (string hex in new[] { "#4E8A3E", "#F2C318", "#B05AA5", "#3F9A8C", "#000000", "#FFFFFF", "#808080" })
        {
            Rgb color = Rgb.Parse(hex, Rgb.Black);
            (double h, double s, double l) = color.ToHsl();
            Rgb back = Rgb.FromHsl(h, s, l);
            Assert.InRange(Math.Abs(back.R - color.R), 0, 1);
            Assert.InRange(Math.Abs(back.G - color.G), 0, 1);
            Assert.InRange(Math.Abs(back.B - color.B), 0, 1);
        }
    }

    [Fact]
    public void ContrastFollowsWcag()
    {
        Assert.Equal(21, Rgb.Black.ContrastWith(Rgb.White), 1);
        Assert.Equal(1, Rgb.White.ContrastWith(Rgb.White), 6);
    }

    [Fact]
    public void SpeciesColorsAreStableAndInTheCategoryFamily()
    {
        var settings = new ColorSettings();
        settings.Normalize();
        var species = new Species { Category = PlantCategory.Arvore, ScientificName = "Handroanthus chrysotrichus" };
        Rgb a = VegetationPalette.Foliage(species, settings);
        Assert.Equal(a, VegetationPalette.Foliage(species, settings));

        (double baseHue, _, _) = VegetationPalette.CategoryBase(PlantCategory.Arvore, settings).ToHsl();
        (double hue, _, double lightness) = a.ToHsl();
        Assert.InRange(Math.Abs(hue - baseHue), 0, 10);
        Assert.InRange(lightness, 0.22, 0.62);

        species.FoliageColor = "#8E2F4E";
        Assert.Equal("#8E2F4E", VegetationPalette.Foliage(species, settings).Hex);

        Assert.Null(VegetationPalette.Flower(species, settings));
        species.FlowerColor = "#F2C318";
        Assert.Equal("#F2C318", VegetationPalette.Flower(species, settings)!.Value.Hex);
        settings.ShowFlowers = false;
        Assert.Null(VegetationPalette.Flower(species, settings));

        Rgb fill = VegetationPalette.CrownFill(species, settings);
        Assert.True(fill.Luminance > VegetationPalette.Foliage(species, settings).Luminance);
        Assert.True(VegetationPalette.MassingPattern(species, settings).Luminance < VegetationPalette.Foliage(species, settings).Luminance);
    }

    [Fact]
    public void ColorSettingsNormalizeRepairsValues()
    {
        var settings = new ColorSettings { CrownTint = 5, MassingBackgroundTint = double.NaN, CategoryColors = new() { [PlantCategory.Arvore] = "xx" } };
        settings.Normalize();
        Assert.Equal(0.85, settings.CrownTint);
        Assert.Equal(0.55, settings.MassingBackgroundTint);
        Assert.Equal(Labels.Categories.Count, settings.CategoryColors.Count);
        Assert.Equal(VegetationPalette.DefaultCategoryColors()[PlantCategory.Arvore], settings.CategoryColors[PlantCategory.Arvore]);
    }
}

public class MonthTests
{
    [Theory]
    [InlineData("ago–set", "ago–set", 2)]
    [InlineData("jan–abr e jun–ago", "jan–abr e jun–ago", 7)]
    [InlineData("nov–fev", "nov–fev", 4)]
    [InlineData("ano todo", "ano todo", 12)]
    [InlineData("Setembro a Dezembro", "set–dez", 4)]
    [InlineData("maio", "mai", 1)]
    [InlineData("jul–set e nov–dez", "jul–set e nov–dez", 5)]
    [InlineData("", "", 0)]
    [InlineData("primavera", "", 0)]
    public void FloweringTextsAreParsed(string text, string expected, int count)
    {
        MonthSet set = MonthSet.Parse(text);
        Assert.Equal(count, set.Count);
        Assert.Equal(expected, set.ToString());
    }

    [Fact]
    public void ShiftWrapsAroundTheYear()
    {
        MonthSet set = MonthSet.Parse("nov–dez").Shift(2);
        Assert.Equal("jan–fev", set.ToString());
        Assert.True(MonthSet.Range(3, 3).Contains(3));
        Assert.True(MonthSet.Range(13, 2).IsEmpty);
    }
}
