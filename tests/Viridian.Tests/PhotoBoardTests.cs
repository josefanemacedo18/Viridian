using Viridian.Core.Geometry;
using Viridian.Core.Identification;

namespace Viridian.Tests;

public class PhotoBoardTests
{
    private static readonly Bounds2 Drawing = new(0, 0, 40, 20);

    [Fact]
    public void EveryItemGetsCardOutsideDrawingWithoutOverlap()
    {
        var items = Enumerable.Range(0, 14)
            .Select(i => new PhotoBoardItem("sp" + i, new[] { new Point2(2 + i * 2.6, 3 + i % 5 * 3) }))
            .ToList();

        IReadOnlyList<PhotoCard> cards = PhotoBoardLayout.Arrange(Drawing, items, 6, 5, 1);

        Assert.Equal(14, cards.Count);
        Assert.Equal(14, cards.Select(c => c.Key).Distinct().Count());
        foreach (PhotoCard card in cards)
        {
            bool outside = card.Box.MaxX <= Drawing.MinX || card.Box.MinX >= Drawing.MaxX || card.Box.MaxY <= Drawing.MinY || card.Box.MinY >= Drawing.MaxY;
            Assert.True(outside, card.Key);
        }

        for (int i = 0; i < cards.Count; i++)
        {
            for (int j = i + 1; j < cards.Count; j++)
            {
                Bounds2 a = cards[i].Box;
                Bounds2 b = cards[j].Box;
                Assert.False(a.MinX < b.MaxX - 1e-9 && a.MaxX > b.MinX + 1e-9 && a.MinY < b.MaxY - 1e-9 && a.MaxY > b.MinY + 1e-9, $"{cards[i].Key} x {cards[j].Key}");
            }
        }
    }

    [Fact]
    public void LeadersDoNotCross_ForPlantsSpreadAroundTheDrawing()
    {
        var items = new[]
        {
            new PhotoBoardItem("oeste", new[] { new Point2(1, 10) }),
            new PhotoBoardItem("leste", new[] { new Point2(39, 10) }),
            new PhotoBoardItem("norte", new[] { new Point2(20, 19) }),
            new PhotoBoardItem("sul", new[] { new Point2(20, 1) }),
        };

        IReadOnlyList<PhotoCard> cards = PhotoBoardLayout.Arrange(Drawing, items, 6, 5, 1);

        for (int i = 0; i < cards.Count; i++)
        {
            for (int j = i + 1; j < cards.Count; j++)
            {
                Assert.False(Geo.SegmentsCross(cards[i].Anchor, cards[i].Target, cards[j].Anchor, cards[j].Target));
            }
        }

        Assert.True(cards.Single(c => c.Key == "oeste").Box.MaxX <= 0);
        Assert.True(cards.Single(c => c.Key == "leste").Box.MinX >= 40);
    }

    [Fact]
    public void TargetIsTheSpeciesPlantNearestToTheCard()
    {
        var items = new[] { new PhotoBoardItem("a", new[] { new Point2(35, 10), new Point2(2, 10), new Point2(20, 10) }) };
        PhotoCard card = Assert.Single(PhotoBoardLayout.Arrange(Drawing, items, 6, 5, 1));
        Point2 nearest = items[0].Targets.OrderBy(t => t.DistanceTo(card.Anchor)).First();
        Assert.Equal(nearest, card.Target);
    }

    [Fact]
    public void ManySpecies_UseOuterRings()
    {
        var items = Enumerable.Range(0, 60).Select(i => new PhotoBoardItem("sp" + i, new[] { new Point2(i % 40, i % 20) })).ToList();
        Assert.Equal(60, PhotoBoardLayout.Arrange(Drawing, items, 6, 5, 1).Count);
    }
}
