using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Library;

namespace Viridian.Tests;

public class CardTests
{
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();
    private static readonly List<Plant3DProfile> Profiles = Plant3DProfileDefaults.Create();
    private static readonly Dictionary<string, PlantCards> Cache = new();

    private static PlantCards Cards(string name)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out PlantCards? cards))
            {
                Species s = Catalog.First(x => x.ScientificName == name);
                cards = PlantCardsPlanner.Build(s, 0, Profiles, new ColorSettings(), "#6B4F3A");
                Cache[name] = cards;
            }

            return cards;
        }
    }

    public static IEnumerable<object[]> PilotNames() => PlantCardsPlanner.Pilot.Select(n => new object[] { n });

    [Fact]
    public void Pilot_IsInTheCatalog_AndOnlyPilotSpeciesAreSelected()
    {
        foreach (string name in PlantCardsPlanner.Pilot)
        {
            Assert.Contains(Catalog, s => s.ScientificName == name);
        }

        Assert.Equal(8, Catalog.Count(PlantCardsPlanner.IsPilot));
    }

    [Theory]
    [MemberData(nameof(PilotNames))]
    public void EveryStage_HasBothPlanes_WithOutline_AndSafeEdges(string name)
    {
        PlantCards cards = Cards(name);
        Assert.Contains(cards.Sets, s => s.Code == PlantCardsPlanner.DefaultStage);
        Assert.True(cards.Sets.Select(s => s.Code).SequenceEqual(cards.Sets.Select(s => s.Code).OrderBy(c => c)));
        foreach (CardSet set in cards.Sets)
        {
            foreach (CardRegions plane in new[] { set.PlaneA, set.PlaneB })
            {
                Assert.NotEmpty(plane.Regions);
                Assert.Contains(plane.Regions, r => r.ColorIndex == CardArt.Black);
                foreach (ColorRegion region in plane.Regions)
                {
                    foreach (List<Point2> loop in region.Loops)
                    {
                        Assert.True(loop.Count >= 3, $"{name} {set.Label}: laço com {loop.Count} pontos");
                        for (int i = 0; i < loop.Count; i++)
                        {
                            Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                            double length = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
                            Assert.True(length >= 0.002, $"{name} {set.Label}: aresta de {length * 1000:0.##} mm");
                        }
                    }
                }
            }
        }

        // Peso da família: limite de arestas somando todas as fases e os dois planos.
        Assert.True(cards.Edges < 40000, $"{name}: {cards.Edges} arestas");
    }

    [Fact]
    public void DeciduousSpecies_GetALeaflessStage_OthersDoNot()
    {
        Assert.Contains(Cards("Handroanthus chrysotrichus").Sets, s => s.Code == 5);
        Assert.DoesNotContain(Cards("Syagrus romanzoffiana").Sets, s => s.Code == 5);
        Assert.DoesNotContain(Cards("Agave attenuata").Sets, s => s.Code == 5);
    }

    [Fact]
    public void SmallerStages_AreShorter_AndLighter()
    {
        PlantCards ipe = Cards("Handroanthus chrysotrichus");
        CardSet plantio = ipe.Sets.Single(s => s.Code == 1), adulta = ipe.Sets.Single(s => s.Code == 4);
        double Top(CardRegions r) => r.Regions.SelectMany(x => x.Loops).SelectMany(l => l).Max(p => p.Y);
        Assert.True(Top(plantio.PlaneA) < Top(adulta.PlaneA) * 0.7);
        Assert.True(plantio.PlaneA.Edges < adulta.PlaneA.Edges);
    }

    [Fact]
    public void Regions_DoNotOverlap_BeyondAThinBandAtTheBorders()
    {
        // Pontos ao acaso: cada ponto está dentro de uma região de cor, no máximo (regra par-ímpar). A simplificação dos
        // contornos pode deixar uma sobreposição mínima, só numa faixa de cerca de uma célula junto às divisas.
        foreach (string name in new[] { "Handroanthus chrysotrichus", "Bismarckia nobilis", "Agave attenuata" })
        {
            CardRegions plane = Cards(name).Sets.Single(s => s.Code == 4).PlaneA;
            List<Point2> all = plane.Regions.SelectMany(r => r.Loops).SelectMany(l => l).ToList();
            double minX = all.Min(p => p.X), maxX = all.Max(p => p.X), minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
            var random = new DeterministicRandom(7);
            int covered = 0;
            for (int i = 0; i < 1500; i++)
            {
                var p = new Point2(random.Range(minX, maxX), random.Range(minY, maxY));
                int inside = plane.Regions.Count(r => r.Loops.Count(l => Inside(l, p)) % 2 == 1);
                if (inside > 1)
                {
                    double distance = plane.Regions.SelectMany(r => r.Loops).Min(l => DistanceToLoop(l, p));
                    Assert.True(distance <= plane.Cell * 1.5, $"{name}: sobreposição a {distance * 1000:0.#} mm da divisa (célula {plane.Cell * 1000:0.#} mm)");
                }

                covered += Math.Min(inside, 1);
            }

            Assert.True(covered > 100, $"{name}: quase nada coberto ({covered})");
        }
    }

    [Fact]
    public void Loops_NeverCrossOrTouch_WithinAColor()
    {
        foreach (string name in new[] { "Bismarckia nobilis", "Dracaena marginata" })
        {
            foreach (ColorRegion region in Cards(name).Sets.Single(s => s.Code == 4).PlaneB.Regions)
            {
                var segments = new List<(int Loop, int Index, int Count, Point2 A, Point2 B)>();
                for (int l = 0; l < region.Loops.Count; l++)
                {
                    List<Point2> loop = region.Loops[l];
                    for (int i = 0; i < loop.Count; i++)
                    {
                        segments.Add((l, i, loop.Count, loop[i], loop[(i + 1) % loop.Count]));
                    }
                }

                for (int i = 0; i < segments.Count; i++)
                {
                    for (int j = i + 1; j < segments.Count; j++)
                    {
                        var s1 = segments[i];
                        var s2 = segments[j];
                        if (s1.Loop == s2.Loop && (Math.Abs(s1.Index - s2.Index) == 1 || Math.Abs(s1.Index - s2.Index) == s1.Count - 1))
                        {
                            continue;
                        }

                        Assert.False(Cross(s1.A, s1.B, s2.A, s2.B), $"{name}: contornos se cruzam na cor {region.Color.Hex}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Hash_IsStable_AndFollowsTheSpeciesData()
    {
        Species ipe = Catalog.First(x => x.ScientificName == "Handroanthus chrysotrichus");
        string a = PlantCardsPlanner.Build(ipe, 0, Profiles, new ColorSettings(), "#6B4F3A").Hash;
        Assert.Equal(Cards("Handroanthus chrysotrichus").Hash, a);
        Assert.NotEqual(a, PlantCardsPlanner.Build(ipe, 1, Profiles, new ColorSettings(), "#6B4F3A").Hash);
        Species changed = ViridianJsonClone(ipe);
        changed.FlowerColor = "#FF00FF";
        Assert.NotEqual(a, PlantCardsPlanner.Build(changed, 0, Profiles, new ColorSettings(), "#6B4F3A").Hash);
    }

    [Fact]
    public void Svg_Preview_ShowsEveryRegion()
    {
        CardSet set = Cards("Agave attenuata").Sets.Single(s => s.Code == 4);
        string face = CardSvg.Face(set.PlaneA, set.ArtA.Size);
        Assert.Equal(set.PlaneA.Regions.Count, face.Split("<path").Length - 1);
        string corner = CardSvg.Corner(set.PlaneA, set.PlaneB, set.ArtA.Size);
        Assert.Contains("clip-path", corner);
    }

    private static Species ViridianJsonClone(Species s) =>
        Viridian.Core.Config.ViridianJson.Deserialize<Species>(Viridian.Core.Config.ViridianJson.Serialize(s))!;

    private static double DistanceToLoop(List<Point2> loop, Point2 p)
    {
        double best = double.MaxValue;
        for (int i = 0; i < loop.Count; i++)
        {
            Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double t = len2 < 1e-18 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
            best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
        }

        return best;
    }

    private static bool Inside(List<Point2> polygon, Point2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y)
                && p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool Cross(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        const double eps = 1e-12;
        double O(Point2 p, Point2 q, Point2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        bool On(Point2 p, Point2 q, Point2 r) => Math.Min(p.X, q.X) - eps <= r.X && r.X <= Math.Max(p.X, q.X) + eps && Math.Min(p.Y, q.Y) - eps <= r.Y && r.Y <= Math.Max(p.Y, q.Y) + eps;
        double o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
        if (((o1 > eps && o2 < -eps) || (o1 < -eps && o2 > eps)) && ((o3 > eps && o4 < -eps) || (o3 < -eps && o4 > eps)))
        {
            return true;
        }

        return (Math.Abs(o1) <= eps && On(a, b, c)) || (Math.Abs(o2) <= eps && On(a, b, d)) || (Math.Abs(o3) <= eps && On(c, d, a)) || (Math.Abs(o4) <= eps && On(c, d, b));
    }
}
