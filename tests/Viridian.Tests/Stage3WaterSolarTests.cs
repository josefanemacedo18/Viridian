using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Solar;
using Viridian.Core.Water;
using Viridian.Library;

namespace Viridian.Tests;

public class SunTests
{
    [Fact]
    public void Noon_AtTropicOfCapricorn_InDecember_IsOverhead()
    {
        SunPosition p = Sun.Position(-23.44, new SolarDate("", 12, 21).DayOfYear, 12);
        Assert.True(p.AltitudeDeg > 89, p.AltitudeDeg.ToString());
    }

    [Fact]
    public void Noon_InSaoPaulo_InJune_IsLowToTheNorth()
    {
        SunPosition p = Sun.Position(-23.5, new SolarDate("", 6, 21).DayOfYear, 12);
        Assert.InRange(p.AltitudeDeg, 42, 44.5);
        Assert.True(p.AzimuthDeg < 1 || p.AzimuthDeg > 359, p.AzimuthDeg.ToString());
        Assert.True(p.Direction.Y > 0); // sol ao norte no inverno do hemisfério sul
    }

    [Fact]
    public void Morning_East_Afternoon_West()
    {
        int day = new SolarDate("", 3, 21).DayOfYear;
        SunPosition morning = Sun.Position(-15, day, 7);
        SunPosition afternoon = Sun.Position(-15, day, 17);
        Assert.InRange(morning.AzimuthDeg, 60, 120);
        Assert.InRange(afternoon.AzimuthDeg, 240, 300);
        Assert.True(morning.Direction.X > 0 && afternoon.Direction.X < 0);
    }

    [Theory]
    [InlineData(3, 21, 11.8, 12.4)]
    [InlineData(6, 21, 10.3, 10.9)]
    [InlineData(12, 21, 13.1, 13.7)]
    public void DayLength_AtSaoPaulo(int month, int day, double min, double max)
    {
        double hours = Sun.Day(-23.5, new SolarDate("", month, day), 10).Sum(x => x.Hours);
        Assert.InRange(hours, min, max);
    }

    [Fact]
    public void KeyDates_FollowHemisphere()
    {
        Assert.Equal(6, Sun.KeyDates(-20)[0].Month);
        Assert.Equal(12, Sun.KeyDates(40)[0].Month);
        Assert.Contains("inverno", Sun.KeyDates(-20)[0].Name, StringComparison.Ordinal);
    }
}

public class ShadeTests
{
    private static readonly SolarSettings Settings = new() { StepMinutes = 15 };

    [Fact]
    public void Crown_BlocksRayThrough_NotBeside()
    {
        var crown = new CrownObstacle("t", new Vec3(0, 0, 6), 4, 2, 0.85);
        Assert.True(crown.Blocks(new Vec3(0, 0, 0.3), new Vec3(0, 0, 1)));
        Assert.False(crown.Blocks(new Vec3(10, 0, 0.3), new Vec3(0, 0, 1)));
        Assert.False(crown.Blocks(new Vec3(0, 0, 0.3), new Vec3(0, 0, -1)));
        Assert.True(crown.Blocks(new Vec3(0, 0, 6), new Vec3(1, 0, 0))); // ponto dentro da copa
    }

    [Fact]
    public void OpenPoint_GetsWholeDay_ShadedPoint_GetsLess()
    {
        var species = new Species { Category = PlantCategory.Arvore, AdultHeightM = 10, CrownDiameterM = 12 };
        CrownObstacle tree = CrownObstacle.For("arvore", species, Vec3.Zero, GrowthStage.Maturidade)!;
        var open = new SolarPoint { Position = new Vec3(40, 40, 0.3), Hours = new double[3] };
        var under = new SolarPoint { Position = new Vec3(0, 0, 0.3), Hours = new double[3] };
        IReadOnlyList<SolarDate> dates = Sun.KeyDates(-23.5);
        SolarStudy.Compute(new[] { open, under }, new[] { tree }, -23.5, dates, Settings, v => v, null);
        Assert.InRange(open.Average, 11.5, 12.5);
        Assert.True(under.Average < open.Average - 2, under.Average.ToString()); // sol baixo passa por baixo da copa alta
        Assert.Equal(LightNeed.SolPleno, SolarClasses.Classify(open.Average, Settings));
    }

    [Fact]
    public void ShadowFallsOnTheSouthInSouthernWinter()
    {
        // No inverno do hemisfério sul o sol fica ao norte: a sombra da árvore cai ao sul.
        var species = new Species { Category = PlantCategory.Arvore, AdultHeightM = 8, CrownDiameterM = 6 };
        CrownObstacle tree = CrownObstacle.For("a", species, Vec3.Zero, GrowthStage.Maturidade)!;
        var south = new SolarPoint { Position = new Vec3(0, -5, 0.3), Hours = new double[1] };
        var north = new SolarPoint { Position = new Vec3(0, 5, 0.3), Hours = new double[1] };
        SolarStudy.Compute(new[] { south, north }, new[] { tree }, -23.5, new[] { Sun.KeyDates(-23.5)[0] }, Settings, v => v, null);
        Assert.True(south.Hours[0] < north.Hours[0] - 1, $"{south.Hours[0]} x {north.Hours[0]}");
    }

    [Fact]
    public void Buildings_BlockEverything_WhenTheyAlwaysHit()
    {
        var p = new SolarPoint { Position = Vec3.Zero, Hours = new double[1] };
        SolarStudy.Compute(new[] { p }, Array.Empty<CrownObstacle>(), -10, new[] { Sun.KeyDates(-10)[1] }, Settings, v => v, (_, _) => true);
        Assert.Equal(0, p.Hours[0], 6);
    }

    [Fact]
    public void ModelRotation_IsApplied()
    {
        // Girar 180° troca norte e sul: o ponto ao norte passa a ficar na sombra.
        var species = new Species { Category = PlantCategory.Arvore, AdultHeightM = 8, CrownDiameterM = 6 };
        CrownObstacle tree = CrownObstacle.For("a", species, Vec3.Zero, GrowthStage.Maturidade)!;
        var north = new SolarPoint { Position = new Vec3(0, 5, 0.3), Hours = new double[1] };
        SolarStudy.Compute(new[] { north }, new[] { tree }, -23.5, new[] { Sun.KeyDates(-23.5)[0] }, Settings, v => v.RotateZ(Math.PI), null);
        var reference = new SolarPoint { Position = new Vec3(0, 5, 0.3), Hours = new double[1] };
        SolarStudy.Compute(new[] { reference }, new[] { tree }, -23.5, new[] { Sun.KeyDates(-23.5)[0] }, Settings, v => v, null);
        Assert.True(north.Hours[0] < reference.Hours[0] - 1);
    }

    [Fact]
    public void Grid_RespectsMaximum()
    {
        var square = new Polygon2(new[] { new Point2(0, 0), new Point2(100, 0), new Point2(100, 100), new Point2(0, 100) });
        (List<Point2> points, double cell) = SolarStudy.Grid(square, 1, 500);
        Assert.InRange(points.Count, 300, 500);
        Assert.True(cell > 1);
        Assert.All(points, p => Assert.True(square.Contains(p)));
        var tiny = new Polygon2(new[] { new Point2(0, 0), new Point2(0.2, 0), new Point2(0.2, 0.2), new Point2(0, 0.2) });
        Assert.Single(SolarStudy.Grid(tiny, 1, 500).Points);
    }

    [Fact]
    public void Bands_And_Fit()
    {
        var s = new SolarSettings();
        Assert.Equal(SolarBand.Sombra, SolarClasses.Band(0.5, s));
        Assert.Equal(SolarBand.SombraParcial, SolarClasses.Band(2, s));
        Assert.Equal(SolarBand.MeiaSombra, SolarClasses.Band(4.5, s));
        Assert.Equal(SolarBand.SolPleno, SolarClasses.Band(7, s));
        Assert.Equal(SolarBand.SolPlenoForte, SolarClasses.Band(9, s));
        Assert.Equal(0, SolarClasses.BinOf(0.2, s));
        Assert.Equal(4, SolarClasses.BinOf(12, s));
        Assert.Equal(LightFit.Atende, SolarClasses.Fit(LightNeed.SolPleno, 7, s));
        Assert.Equal(LightFit.Limite, SolarClasses.Fit(LightNeed.SolPleno, 5.2, s));
        Assert.Equal(LightFit.NaoAtende, SolarClasses.Fit(LightNeed.SolPleno, 2, s));
        Assert.Equal(LightFit.NaoAtende, SolarClasses.Fit(LightNeed.Sombra, 9, s));
        Assert.Equal(LightFit.Atende, SolarClasses.Fit(LightNeed.MeiaSombra, 4, s));
        Assert.Equal(LightFit.Limite, SolarClasses.Fit(LightNeed.MeiaSombra, 6.8, s));
        Assert.Null(SolarClasses.Fit(LightNeed.NaoInformada, 9, s));
        Assert.Equal(5, SolarClasses.Bins(s).Count);
        Assert.Contains("6 h ou mais", SolarClasses.NeedText(LightNeed.SolPleno, s), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_Normalize()
    {
        var s = new SolarSettings { LatitudeDeg = double.NaN, StepMinutes = 0, MaxPoints = 1, ShadeHours = 20, FullSunHours = 6 };
        s.Normalize();
        Assert.Equal(-23.55, s.LatitudeDeg);
        Assert.Equal(30, s.StepMinutes);
        Assert.Equal(50, s.MaxPoints);
        Assert.Equal(6, s.ShadeHours);
    }
}

public class RainGardenTests
{
    [Fact]
    public void Sizing_ByVolume()
    {
        var s = new RainGardenSettings();
        RainGardenResult r = RainGardenCalculator.Calculate(100, 0, s);
        Assert.Equal(2.25, r.RunoffVolumeM3, 6);
        Assert.Equal(0.345, r.StorageDepthM, 6);
        Assert.Equal(2.25 / 0.345, r.RequiredAreaM2, 6);
        Assert.Equal(0.85, r.ExcavationDepthM, 6);
        Assert.Null(r.DrainHours);
        Assert.True(r.NeedsUnderdrain);
        Assert.True(r.AreaSufficient);
    }

    [Fact]
    public void SmallArea_IsFlagged_AndInfiltrationGivesDrainTime()
    {
        var s = new RainGardenSettings { InfiltrationMmH = 25 };
        RainGardenResult r = RainGardenCalculator.Calculate(100, 3, s);
        Assert.False(r.AreaSufficient);
        Assert.Contains(r.Warnings, w => w.Contains("menos que", StringComparison.Ordinal));
        Assert.Equal(13.8, r.DrainHours!.Value, 6);
        Assert.False(r.NeedsUnderdrain);
        Assert.Contains(RainGardenCalculator.Report(100, r, s), l => l.Contains("2,25 m³", StringComparison.Ordinal));

        var slow = new RainGardenSettings { InfiltrationMmH = 2 };
        Assert.True(RainGardenCalculator.Calculate(100, 10, slow).NeedsUnderdrain);
    }

    [Fact]
    public void InvalidSettings_AreNormalized()
    {
        var s = new RainGardenSettings { RainMm = double.NaN, RunoffCoefficient = 5, SubstratePorosity = -1 };
        RainGardenResult r = RainGardenCalculator.Calculate(double.NaN, -5, s);
        Assert.Equal(0, r.RunoffVolumeM3);
        Assert.Contains(r.Warnings, w => w.Contains("área de contribuição", StringComparison.Ordinal));
    }

    [Fact]
    public void Detail_HasLayersOverflowAndUnderdrain()
    {
        var s = new RainGardenSettings();
        Drawing d = RainGardenCalculator.Detail(s, RainGardenCalculator.Calculate(80, 0, s));
        Assert.Contains(d.Texts, t => t.Text.Contains("EXTRAVASOR", StringComparison.Ordinal));
        Assert.Contains(d.Texts, t => t.Text.Contains("DRENO DE FUNDO", StringComparison.Ordinal));
        Assert.Contains(d.Texts, t => t.Text.Contains("BRITA", StringComparison.Ordinal));
        Assert.True(d.Fills.Count >= 4);
    }

    [Fact]
    public void Waterlogging_EstimateAndSuggestions()
    {
        Assert.Equal(WaterloggingTolerance.Alta, new Species { Category = PlantCategory.Aquatica }.EffectiveWaterlogging);
        Assert.Equal(WaterloggingTolerance.Baixa, new Species { Category = PlantCategory.Arbusto, Water = WaterNeed.Baixa }.EffectiveWaterlogging);
        var set = new Species { Category = PlantCategory.Arbusto, Water = WaterNeed.Baixa, Waterlogging = WaterloggingTolerance.Alta };
        Assert.Equal(WaterloggingTolerance.Alta, set.EffectiveWaterlogging);
        Assert.False(set.WaterloggingEstimated);

        (List<Species> bottom, List<Species> slope, List<Species> edge) = RainGardenCalculator.SuggestPlants(SeedCatalog.CreateSpecies());
        Assert.NotEmpty(bottom);
        Assert.NotEmpty(slope);
        Assert.NotEmpty(edge);
        Assert.All(bottom, s => Assert.Equal(WaterloggingTolerance.Alta, s.EffectiveWaterlogging));
        Assert.All(bottom.Concat(slope), s => Assert.NotEqual(PlantOrigin.ExoticaInvasora, s.Origin));
    }

    [Fact]
    public void Species_WaterloggingSurvivesJson()
    {
        var s = new Species { PopularName = "Teste", Waterlogging = WaterloggingTolerance.Media };
        Species back = Viridian.Core.Config.ViridianJson.Deserialize<Species>(Viridian.Core.Config.ViridianJson.Serialize(s))!;
        Assert.Equal(WaterloggingTolerance.Media, back.Waterlogging);
    }
}
