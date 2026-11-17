using System.Text.RegularExpressions;
using Viridian.Core.Colors;

namespace Viridian.Tests;

/// <summary>As cores do tema precisam atender ao contraste mínimo da WCAG 2.1 (nível AA: 4,5:1 para texto).</summary>
public class ThemeContrastTests
{
    private static Dictionary<string, Rgb> ThemeColors()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Viridian.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        string xaml = File.ReadAllText(Path.Combine(root!, "src", "Viridian.UI", "Themes", "Viridian.xaml"));
        var colors = new Dictionary<string, Rgb>();
        foreach (Match m in Regex.Matches(xaml, "<Color x:Key=\"(\\w+)\">(#[0-9A-Fa-f]{6})</Color>"))
        {
            colors[m.Groups[1].Value] = Rgb.Parse(m.Groups[2].Value, Rgb.Black);
        }

        foreach (Match m in Regex.Matches(xaml, "<SolidColorBrush x:Key=\"(\\w+)\" Color=\"([^\"]+)\""))
        {
            string value = m.Groups[2].Value;
            Match reference = Regex.Match(value, "\\{StaticResource (\\w+)\\}");
            colors[m.Groups[1].Value] = reference.Success ? colors[reference.Groups[1].Value] : Rgb.Parse(value, Rgb.Black);
        }

        return colors;
    }

    [Theory]
    [InlineData("VirText", "VirBackground")]
    [InlineData("VirText", "VirSurface")]
    [InlineData("VirMuted", "VirBackground")]
    [InlineData("VirMuted", "VirSurface")]
    [InlineData("VirGreenDark", "VirBackground")]
    [InlineData("VirGreenDark", "VirGreenLight")]
    [InlineData("VirWarning", "VirSurface")]
    [InlineData("VirError", "VirSurface")]
    public void TextColors_MeetAA(string foreground, string background)
    {
        Dictionary<string, Rgb> colors = ThemeColors();
        double ratio = colors[foreground].ContrastWith(colors[background]);
        Assert.True(ratio >= 4.5, $"{foreground} sobre {background}: {ratio:0.00}:1");
    }

    [Fact]
    public void PrimaryButton_WhiteTextMeetsAA()
    {
        Dictionary<string, Rgb> colors = ThemeColors();
        Assert.True(Rgb.White.ContrastWith(colors["VirGreen"]) >= 4.5);
        Assert.True(Rgb.White.ContrastWith(colors["VirGreenDark"]) >= 4.5);
    }
}
