using Viridian.Core.Composition;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Xunit;

namespace Viridian.Tests;

public class CompositionPlannerTests
{
    private static Polygon2 Rect(double w, double h) =>
        new(new[] { new Point2(0, 0), new Point2(w, 0), new Point2(w, h), new Point2(0, h) });

    private static Species Sp(string id, double crown, double spacing, string unit = "muda") =>
        new() { Id = id, PopularName = id, ScientificName = id, CrownDiameterM = crown, RecommendedSpacingM = spacing, SupplyUnit = unit };

    private static (CompositionItem, Species) Item(Species s, CompositionRole role, double share = 0, int count = 0) =>
        (new CompositionItem { SpeciesId = s.Id, Role = role, SharePercent = share, Count = count }, s);

    [Fact]
    public void AgainstWall_BandsGoBackToFrontFromTheClickedSide()
    {
        // Canteiro de 10 × 3 m encostado num muro ao norte (y = 3).
        Polygon2 bed = Rect(10, 3);
        Species tall = Sp("alto", 1.2, 0.8), mid = Sp("medio", 0.6, 0.5), low = Sp("baixo", 0.3, 0.25);
        CompositionPlan plan = CompositionPlanner.Plan(bed,
            new[] { Item(low, CompositionRole.Frente, 30), Item(tall, CompositionRole.Fundo, 40), Item(mid, CompositionRole.Meio, 30) },
            CompositionLayout.Encostado, new Point2(5, 4), seed: 3);

        Assert.Empty(plan.Warnings);
        double MeanY(string id) => plan.Groups.Single(g => g.Species.Id == id).Points.Average(p => p.Y);
        Assert.True(MeanY("alto") > MeanY("medio"));
        Assert.True(MeanY("medio") > MeanY("baixo"));
        Assert.All(plan.Groups, g => Assert.NotEmpty(g.Points));
        Assert.All(plan.Groups.SelectMany(g => g.Points), p => Assert.True(bed.Contains(p)));

        // Quantidades coerentes com a área de cada faixa e o espaçamento.
        PlannedGroup back = plan.Groups.Single(g => g.Species.Id == "alto");
        Assert.Equal(12, back.BandAreaM2, 6);
        double expected = back.BandAreaM2 / (0.8 * 0.8);
        Assert.InRange(back.Points.Count, expected * 0.4, expected * 1.6);
    }

    [Fact]
    public void ClickOnTheOtherSide_FlipsTheBands()
    {
        Polygon2 bed = Rect(10, 3);
        Species tall = Sp("alto", 1.2, 0.8), low = Sp("baixo", 0.3, 0.25);
        var items = new[] { Item(tall, CompositionRole.Fundo, 50), Item(low, CompositionRole.Frente, 50) };
        CompositionPlan south = CompositionPlanner.Plan(bed, items, CompositionLayout.Encostado, new Point2(5, -1));
        Assert.True(south.Groups.Single(g => g.Species.Id == "alto").Points.Average(p => p.Y)
                    < south.Groups.Single(g => g.Species.Id == "baixo").Points.Average(p => p.Y));
    }

    [Fact]
    public void Island_PutsTheBackInTheCentre()
    {
        Polygon2 bed = Rect(8, 8);
        Species tall = Sp("alto", 1.0, 0.7), low = Sp("baixo", 0.3, 0.3);
        CompositionPlan plan = CompositionPlanner.Plan(bed,
            new[] { Item(tall, CompositionRole.Fundo, 40), Item(low, CompositionRole.Frente, 60) }, CompositionLayout.Ilha, null);
        double Edge(string id) => plan.Groups.Single(g => g.Species.Id == id).Points.Average(p => bed.DistanceToBoundary(p));
        Assert.True(Edge("alto") > Edge("baixo") + 1);
    }

    [Fact]
    public void Accents_AreSpreadAndBandsKeepClearOfThem()
    {
        Polygon2 bed = Rect(12, 4);
        Species palm = Sp("palmeira", 3, 3), shrub = Sp("arbusto", 0.6, 0.5);
        CompositionPlan plan = CompositionPlanner.Plan(bed,
            new[] { Item(palm, CompositionRole.Destaque, count: 3), Item(shrub, CompositionRole.Meio, 100) },
            CompositionLayout.Encostado, new Point2(6, 5));
        PlannedGroup accents = plan.Groups.Single(g => g.Species.Id == "palmeira");
        Assert.Equal(3, accents.Points.Count);
        for (int i = 0; i < 3; i++)
        {
            for (int j = i + 1; j < 3; j++)
            {
                Assert.True((accents.Points[i] - accents.Points[j]).Length >= 3 - 1e-9);
            }
        }

        // Nenhum arbusto debaixo do tronco das palmeiras.
        Assert.All(plan.Groups.Single(g => g.Species.Id == "arbusto").Points,
            p => Assert.All(accents.Points, a => Assert.True((p - a).Length > 0.4)));
    }

    [Fact]
    public void TwoAccents_SplitTheBedEvenlyAndPlantsGrowUnderTheCrowns()
    {
        Polygon2 bed = Rect(12, 3.5);
        Species palm = Sp("palmeira", 3.5, 3.5), shrub = Sp("arbusto", 0.6, 0.5);
        CompositionPlan plan = CompositionPlanner.Plan(bed,
            new[] { Item(palm, CompositionRole.Destaque, count: 2), Item(shrub, CompositionRole.Meio, 100) },
            CompositionLayout.Encostado, new Point2(6, 5));
        List<double> xs = plan.Groups.Single(g => g.Species.Id == "palmeira").Points.Select(p => p.X).OrderBy(x => x).ToList();
        Assert.InRange(xs[0], 1.8, 4.2);
        Assert.InRange(xs[1], 7.8, 10.2);

        // Sob a copa (raio 1,75 m) há plantio; só a coroa junto ao tronco fica livre.
        List<Point2> shrubs = plan.Groups.Single(g => g.Species.Id == "arbusto").Points.ToList();
        Point2 trunk = plan.Groups.Single(g => g.Species.Id == "palmeira").Points[0];
        Assert.Contains(shrubs, p => (p - trunk).Length < 1.75);
        Assert.DoesNotContain(shrubs, p => (p - trunk).Length < CompositionPlanner.TrunkClearance(palm));
    }

    [Fact]
    public void BaseAreaSpeciesAndShares_AreHandled()
    {
        Polygon2 bed = Rect(6, 3);
        Species grass = Sp("grama", 0.3, 0.3, "m²"), groundcover = Sp("forracao", 0.3, 0.3, "m²"), shrub = Sp("arbusto", 0.6, 0.5);
        CompositionPlan plan = CompositionPlanner.Plan(bed,
            new[] { Item(groundcover, CompositionRole.Base), Item(grass, CompositionRole.Base), Item(shrub, CompositionRole.Meio, 60), Item(grass, CompositionRole.Frente, 20) },
            CompositionLayout.Encostado, null);
        Assert.Same(groundcover, plan.BaseSpecies);
        Assert.Contains(plan.Warnings, w => w.Contains("Só uma forração", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("ajustadas para 100%", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("vendida por área", StringComparison.Ordinal));
        Assert.NotEmpty(plan.Groups.Single(g => g.Species.Id == "arbusto").Points);
    }

    [Fact]
    public void SameSeed_SamePlan()
    {
        Polygon2 bed = Rect(7, 3);
        Species a = Sp("a", 0.8, 0.6), b = Sp("b", 0.4, 0.35);
        var items = new[] { Item(a, CompositionRole.Fundo, 50), Item(b, CompositionRole.Frente, 50) };
        CompositionPlan p1 = CompositionPlanner.Plan(bed, items, CompositionLayout.Encostado, new Point2(3, 9), 42);
        CompositionPlan p2 = CompositionPlanner.Plan(bed, items, CompositionLayout.Encostado, new Point2(3, 9), 42);
        Assert.Equal(p1.Groups.SelectMany(g => g.Points), p2.Groups.SelectMany(g => g.Points));
    }

    [Fact]
    public void Template_DrawingPlacementAndJson()
    {
        Point2 placed = TemplateDrawing.Place(new Point2(1, 0), new Point2(10, 10), Math.PI / 2);
        Assert.Equal(10, placed.X, 9);
        Assert.Equal(11, placed.Y, 9);

        var template = new BedTemplate
        {
            Name = "Canteiro do muro",
            Items = { new CompositionItem { SpeciesId = "x", Role = CompositionRole.Fundo, SharePercent = 40 } },
            Drawing = new TemplateDrawing
            {
                Outline = { new List<Point2> { new(0, 0), new(2, 0), new(2, 1) } },
                Plants = { new TemplatePlant { SpeciesId = "x", X = 1, Y = 0.5 } },
            },
        };
        Assert.Empty(template.Validate());
        Assert.True(template.IsDrawing);
        BedTemplate back = ViridianJson.Deserialize<BedTemplate>(ViridianJson.Serialize(template))!;
        Assert.Equal("Canteiro do muro", back.Name);
        Assert.Equal(CompositionRole.Fundo, back.Items[0].Role);
        Assert.Equal(1, back.Drawing!.Plants[0].X);
        Assert.StartsWith("desenho", back.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(new BedTemplate().Validate());
    }
}
