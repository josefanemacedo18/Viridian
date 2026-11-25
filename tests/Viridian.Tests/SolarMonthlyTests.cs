using Viridian.Core.Model;
using Viridian.Core.Solar;

namespace Viridian.Tests;

public class SolarMonthlyTests
{
    private static readonly SolarSettings Settings = new();

    [Fact]
    public void MonthlyDates_AreTwelveTypicalDays()
    {
        IReadOnlyList<SolarDate> dates = Sun.MonthlyDates();
        Assert.Equal(12, dates.Count);
        Assert.Equal(Enumerable.Range(1, 12), dates.Select(d => d.Month));
        Assert.Equal("jun", dates[5].Short);
        Assert.Equal("inverno", Sun.KeyDates(-23)[0].Short);
    }

    [Fact]
    public void DayLength_SaoPaulo_IsLongerInDecember()
    {
        (double r6, double s6) = Sun.SunriseSunset(-23.55, new SolarDate("", 6, 21).DayOfYear)!.Value;
        (double r12, double s12) = Sun.SunriseSunset(-23.55, new SolarDate("", 12, 21).DayOfYear)!.Value;
        Assert.InRange(s6 - r6, 10.4, 10.9);   // ~10h35 no inverno
        Assert.InRange(s12 - r12, 13.3, 13.7); // ~13h30 no verão
    }

    [Fact]
    public void ClockTime_UsesLongitudeTimeZoneAndEquationOfTime()
    {
        // São Paulo (−46,63°, UTC−3): meio-dia solar perto de 12h06 em meados de abril (equação do tempo ~0).
        var sp = new SolarSite(-23.55, -46.63, -3, "São Paulo", SiteSource.Projeto);
        int april15 = new SolarDate("", 4, 15).DayOfYear;
        Assert.Contains(sp.ClockText(12, april15), new[] { "12:06", "12:07" });

        // Início de novembro: equação do tempo ~ +16 min, meio-dia solar ~11h50.
        int nov3 = new SolarDate("", 11, 3).DayOfYear;
        Assert.InRange(Sun.EquationOfTimeMinutes(nov3), 15, 17.5);
        Assert.Equal("11:50", sp.ClockText(12, nov3));

        // Sunrise in São Paulo on 21/12 around 5h15 (legal time, no daylight saving).
        int dec21 = new SolarDate("", 12, 21).DayOfYear;
        string sunrise = sp.ClockText(Sun.SunriseSunset(-23.55, dec21)!.Value.Sunrise, dec21);
        Assert.InRange(TimeSpan.Parse(sunrise, System.Globalization.CultureInfo.InvariantCulture).TotalMinutes, 5 * 60 + 5, 5 * 60 + 25);
    }

    [Theory]
    [InlineData("-22.9068, -43.1729", -22.9068, -43.1729)]
    [InlineData("-22,9068, -43,1729", -22.9068, -43.1729)]
    [InlineData("-22,9068;-43,1729", -22.9068, -43.1729)]
    [InlineData("−15.7939 −47.8828", -15.7939, -47.8828)]
    [InlineData("22°54'24.5\"S 43°10'22.4\"W", -22.906805, -43.172888)]
    [InlineData("3°43'6\"S 38°32'34\"O", -3.718333, -38.542777)]
    [InlineData("22.9068° S, 43.1729° W", -22.9068, -43.1729)]
    public void Coordinates_AreParsed(string text, double lat, double lon)
    {
        Assert.True(SolarSite.TryParseCoordinates(text, out double a, out double b));
        Assert.Equal(lat, a, 4);
        Assert.Equal(lon, b, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Rio de Janeiro")]
    [InlineData("-22.9")]
    [InlineData("95, 10")]
    public void Coordinates_InvalidAreRejected(string text) =>
        Assert.False(SolarSite.TryParseCoordinates(text, out _, out _));

    [Fact]
    public void TimeZone_Suggestion_AndText()
    {
        Assert.Equal(-3, SolarSite.SuggestTimeZone(-46.6));
        Assert.Equal(-4, SolarSite.SuggestTimeZone(-60.0));
        Assert.Equal("UTC−3", SolarSite.Utc(-3));
        Assert.Equal("UTC+5:30", SolarSite.Utc(5.5));
        string text = new SolarSite(-22.9, -43.2, -3, "Rio", SiteSource.Digitado).Describe();
        Assert.Contains("Rio", text, StringComparison.Ordinal);
        Assert.Contains("coordenadas digitadas", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PlantLight_Verdict_MajorityAndMonthsText()
    {
        IReadOnlyList<SolarDate> dates = Sun.MonthlyDates();
        double[] winterShade = { 7, 7, 6.5, 5.2, 2, 1.5, 1.5, 2.5, 6, 7, 7, 7 }; // abr no limite; mai–ago com pouca luz
        PlantLight light = PlantLight.Evaluate(LightNeed.SolPleno, winterShade, dates, Settings)!;
        Assert.Equal(4, light.Failing);
        Assert.Equal(LightFit.Limite, light.Verdict); // falha na minoria dos meses
        Assert.Equal("mai–ago", light.FailingText());
        string text = light.Describe(Settings);
        Assert.Contains("sol pleno (6 h ou mais)", text, StringComparison.Ordinal);
        Assert.Contains("não atende em 4 de 12 meses: mai–ago", text, StringComparison.Ordinal);

        PlantLight dark = PlantLight.Evaluate(LightNeed.SolPleno, Enumerable.Repeat(1.0, 12).ToList(), dates, Settings)!;
        Assert.Equal(LightFit.NaoAtende, dark.Verdict);
        Assert.Null(PlantLight.Evaluate(LightNeed.NaoInformada, winterShade, dates, Settings));
    }

    [Fact]
    public void Suggestions_SameCategory_CompatibleLight_NoInvasive()
    {
        var current = new Species { Id = "x", Category = PlantCategory.Arbusto, Light = LightNeed.SolPleno, AdultHeightM = 1.5, CrownDiameterM = 1 };
        var catalog = new[]
        {
            new Species { Id = "a", PopularName = "A", Category = PlantCategory.Arbusto, Light = LightNeed.Sombra, AdultHeightM = 1.4, CrownDiameterM = 1 },
            new Species { Id = "b", PopularName = "B", Category = PlantCategory.Arbusto, Light = LightNeed.Sombra, AdultHeightM = 6, CrownDiameterM = 4 },
            new Species { Id = "c", PopularName = "C", Category = PlantCategory.Arbusto, Light = LightNeed.Sombra, AdultHeightM = 1.5, CrownDiameterM = 1, Origin = PlantOrigin.ExoticaInvasora },
            new Species { Id = "d", PopularName = "D", Category = PlantCategory.Herbacea, Light = LightNeed.Sombra, AdultHeightM = 1.5, CrownDiameterM = 1 },
            new Species { Id = "e", PopularName = "E", Category = PlantCategory.Arbusto, Light = LightNeed.SolPleno, AdultHeightM = 1.5, CrownDiameterM = 1 },
        };
        List<Species> result = SolarSuggestions.For(current, 1.0, catalog, Settings);
        Assert.Equal(new[] { "a", "b" }, result.Select(s => s.Id));
    }

    [Fact]
    public void SettingsClone_AndNormalize_NewFields()
    {
        var s = new SolarSettings { PartShadeHours = 9, ShadeHours = 3, ToleranceHours = 10, LongitudeDeg = double.NaN, TimeZoneHours = 40, MapMonths = new() { 0, 6, 6, 13, 12 } };
        s.Normalize();
        Assert.Equal(3, s.PartShadeHours);
        Assert.Equal(3, s.ToleranceHours);
        Assert.Equal(-46.63, s.LongitudeDeg);
        Assert.Equal(14, s.TimeZoneHours);
        Assert.Equal(new[] { 6, 12 }, s.MapMonths);
        SolarSettings copy = s.Clone();
        copy.MapMonths.Add(1);
        Assert.Equal(2, s.MapMonths.Count);
    }

    [Fact]
    public void Calendar_DrawsRowsAndLegend()
    {
        IReadOnlyList<SolarDate> dates = Sun.MonthlyDates();
        var rows = new List<LightCalendarRow>
        {
            new("Região", Enumerable.Repeat(5.0, 12).ToList(), null, 0),
            new("AR-1 Ipê", Enumerable.Repeat(2.0, 12).ToList(), PlantLight.Evaluate(LightNeed.SolPleno, Enumerable.Repeat(2.0, 12).ToList(), dates, Settings), 2),
        };
        var site = new SolarSite(-23.55, -46.63, -3, "São Paulo", SiteSource.Projeto);
        Viridian.Core.Detailing.Drawing d = LightCalendar.Build("Calendário de luz", "Mês a mês", site, dates, rows, Settings, true);
        Assert.Equal(24 + 5 + 2 + 2, d.Fills.Count); // 2 linhas × 12 meses + 5 faixas da legenda + faixas do cabeçalho + 2 zebras
        Assert.Contains(d.Texts, t => t.Text == "2,0 ×");
        Assert.Contains(d.Texts, t => t.Text == "0/12");
        Assert.Contains(d.Texts, t => t.Text.Contains("São Paulo", StringComparison.Ordinal));
    }
}
