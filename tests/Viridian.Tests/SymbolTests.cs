using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

namespace Viridian.Tests;

public class SymbolTests
{
    public static IEnumerable<object[]> StylesAndDiameters()
    {
        foreach (SymbolStyle style in SymbolStyles.All)
        {
            foreach (double diameter in new[] { 0.10, 0.25, 0.6, 1.2, 3.0, 8.0, 20.0, 60.0 })
            {
                yield return new object[] { style, diameter };
            }
        }
    }

    private static IEnumerable<Point2> Sample(SymbolPrimitive primitive)
    {
        switch (primitive)
        {
            case SymbolLine line:
                yield return line.A;
                yield return line.B;
                break;
            case SymbolArc arc:
                Assert.True(ArcInfo.TryFromThreePoints(arc.Start, arc.Mid, arc.End, out ArcInfo info), "arco degenerado");
                for (int i = 0; i <= 12; i++)
                {
                    yield return info.PointAt(i / 12.0);
                }

                break;
        }
    }

    [Theory]
    [MemberData(nameof(StylesAndDiameters))]
    public void SymbolsAreValidAndStayInsideTheCrown(SymbolStyle style, double diameter)
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            PlantSymbol symbol = SymbolGenerator.Generate(style, diameter, seed * 7919, seed % 3);
            Assert.Equal(diameter, symbol.Diameter, 9);
            Assert.NotEmpty(symbol.Primitives);

            double radius = diameter / 2;
            foreach (SymbolPrimitive primitive in symbol.Primitives)
            {
                foreach (Point2 p in Sample(primitive))
                {
                    Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
                    Assert.True(p.Length <= radius * 1.02 + 1e-9, $"{style} Ø{diameter}: ponto fora da copa ({p.Length:0.000} > {radius:0.000})");
                }

                // Comprimento mínimo aceito pelo Revit para curvas (aprox. 0,8 mm), com folga.
                double length = primitive switch
                {
                    SymbolLine l => l.A.DistanceTo(l.B),
                    SymbolArc a => a.Start.DistanceTo(a.End),
                    _ => 0,
                };
                Assert.True(length >= 0.003, $"{style} Ø{diameter}: trecho curto demais ({length * 1000:0.00} mm)");
            }

            // O contorno em dimensão real existe em todos os perfis.
            Assert.Contains(symbol.Primitives, p => p.Tier == SymbolTier.MinimalOutline);
            Assert.Contains(symbol.Primitives, p => p.Tier == SymbolTier.Outline);
            Assert.Contains(symbol.Primitives, p => p.Tier == SymbolTier.Center);
            Assert.True(symbol.Primitives.Count <= 260, $"{style}: {symbol.Primitives.Count} primitivas");
        }
    }

    [Theory]
    [MemberData(nameof(StylesAndDiameters))]
    public void DetailDecreasesWithScale(SymbolStyle style, double diameter)
    {
        PlantSymbol symbol = SymbolGenerator.Generate(style, diameter, 99);
        int detailed = symbol.Count(DetailProfile.Detalhado);
        int intermediate = symbol.Count(DetailProfile.Intermediario);
        int simplified = symbol.Count(DetailProfile.Simplificado);
        int minimal = symbol.Count(DetailProfile.Minimo);
        Assert.True(detailed >= intermediate);
        Assert.True(intermediate >= simplified);
        Assert.True(minimal <= 8);
        Assert.True(minimal >= 2);
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        PlantSymbol a = SymbolGenerator.Generate(SymbolStyle.ArvoreEspalhada, 9, 123, 1);
        PlantSymbol b = SymbolGenerator.Generate(SymbolStyle.ArvoreEspalhada, 9, 123, 1);
        Assert.Equal(a.Primitives, b.Primitives);

        PlantSymbol other = SymbolGenerator.Generate(SymbolStyle.ArvoreEspalhada, 9, 123, 2);
        Assert.NotEqual(a.Primitives, other.Primitives);
    }

    [Fact]
    public void InvalidDiametersAreClamped()
    {
        Assert.Equal(SymbolGenerator.MinDiameter, SymbolGenerator.Generate(SymbolStyle.Simples, 0, 1).Diameter);
        Assert.Equal(SymbolGenerator.MaxDiameter, SymbolGenerator.Generate(SymbolStyle.Simples, 1e9, 1).Diameter);
        Assert.Equal(1.0, SymbolGenerator.Generate(SymbolStyle.Simples, double.NaN, 1).Diameter);
    }

    [Fact]
    public void EveryCategoryHasASuggestedStyle()
    {
        foreach (PlantCategory category in Labels.Categories)
        {
            foreach (CrownForm form in Enum.GetValues<CrownForm>())
            {
                SymbolStyle style = SymbolStyles.Suggest(category, form);
                Assert.Contains(style, SymbolStyles.All);
                Assert.Equal(style, SymbolStyles.ForCategory(category).First(s => s == style));
            }

            Assert.Equal(SymbolStyles.All.Count, SymbolStyles.ForCategory(category).Count);
        }
    }

    [Fact]
    public void TierVisibilityFollowsTheScaleSpecification()
    {
        Assert.True(TierVisibility.IsVisible(SymbolTier.MinimalOutline, DetailProfile.Minimo));
        Assert.False(TierVisibility.IsVisible(SymbolTier.Outline, DetailProfile.Minimo));
        Assert.True(TierVisibility.IsVisible(SymbolTier.Center, DetailProfile.Minimo));
        Assert.True(TierVisibility.IsVisible(SymbolTier.Essential, DetailProfile.Simplificado));
        Assert.False(TierVisibility.IsVisible(SymbolTier.Principal, DetailProfile.Simplificado));
        Assert.True(TierVisibility.IsVisible(SymbolTier.Principal, DetailProfile.Intermediario));
        Assert.False(TierVisibility.IsVisible(SymbolTier.Detail, DetailProfile.Intermediario));
        Assert.True(TierVisibility.IsVisible(SymbolTier.Detail, DetailProfile.Detalhado));
        Assert.Equal(TierVisibility.AllTiers.Count, TierVisibility.AllTiers.Select(TierVisibility.SubcategoryName).Distinct().Count());
    }

    [Fact]
    public void ScaleProfilesResolveByDenominator()
    {
        var set = new ScaleProfileSet();
        Assert.Equal(DetailProfile.Detalhado, set.Resolve(20));
        Assert.Equal(DetailProfile.Detalhado, set.Resolve(50));
        Assert.Equal(DetailProfile.Intermediario, set.Resolve(75));
        Assert.Equal(DetailProfile.Intermediario, set.Resolve(100));
        Assert.Equal(DetailProfile.Simplificado, set.Resolve(125));
        Assert.Equal(DetailProfile.Simplificado, set.Resolve(250));
        Assert.Equal(DetailProfile.Minimo, set.Resolve(300));
        Assert.Equal(DetailProfile.Minimo, set.Resolve(1000));
        Assert.Equal(DetailProfile.Detalhado, set.Resolve(0));
    }

    [Fact]
    public void ScaleProfilesNormalizeRepairsBadConfiguration()
    {
        var set = new ScaleProfileSet
        {
            Ranges = new List<ScaleProfileRange>
            {
                new() { Profile = DetailProfile.Simplificado, MaxDenominator = 10 },
                new() { Profile = DetailProfile.Detalhado, MaxDenominator = 100 },
                new() { Profile = DetailProfile.Detalhado, MaxDenominator = 5 },
                new() { Profile = DetailProfile.Minimo, MaxDenominator = 500 },
            },
        };
        set.Normalize();
        Assert.Equal(2, set.Ranges.Count);
        Assert.True(set.Ranges[0].MaxDenominator < set.Ranges[1].MaxDenominator);
        Assert.Equal(DetailProfile.Detalhado, set.Resolve(100));

        var empty = new ScaleProfileSet { Ranges = new List<ScaleProfileRange>() };
        empty.Normalize();
        Assert.Equal(3, empty.Ranges.Count);
    }
}
