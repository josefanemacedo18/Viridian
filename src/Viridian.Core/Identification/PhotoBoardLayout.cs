using Viridian.Core.Geometry;

namespace Viridian.Core.Identification;

/// <summary>Uma espécie a ilustrar: chave e pontos onde ela aparece na planta (exemplares ou maciços).</summary>
public sealed record PhotoBoardItem(string Key, IReadOnlyList<Point2> Targets);

/// <summary>Cartão posicionado: retângulo (foto e texto), ponto de saída da linha e ponto da planta indicado.</summary>
public sealed record PhotoCard(string Key, Bounds2 Box, Point2 Anchor, Point2 Target);

/// <summary>
/// Diagramação do quadro de fotos ao redor da planta, como nas pranchas de paisagismo ilustradas: um cartão por
/// espécie nas margens do desenho, ligado por linha a um exemplar da espécie. As posições são escolhidas para
/// encurtar as linhas e evitar que elas se cruzem.
/// </summary>
public static class PhotoBoardLayout
{
    public static IReadOnlyList<PhotoCard> Arrange(Bounds2 drawing, IReadOnlyList<PhotoBoardItem> items, double cardWidth, double cardHeight, double gap)
    {
        List<PhotoBoardItem> valid = items.Where(i => i.Targets.Count > 0).ToList();
        if (valid.Count == 0 || !(cardWidth > 0) || !(cardHeight > 0))
        {
            return Array.Empty<PhotoCard>();
        }

        gap = Math.Max(0, gap);
        List<Bounds2> slots = new();
        for (int ring = 0; slots.Count < valid.Count && ring < 6; ring++)
        {
            slots.AddRange(Ring(drawing, cardWidth, cardHeight, gap, ring));
        }

        Point2 center = drawing.Center;

        // Representante inicial de cada espécie: o ponto mais afastado do centro (mais perto das margens).
        var representative = valid.ToDictionary(i => i.Key, i => i.Targets.OrderByDescending(t => t.DistanceTo(center)).First());

        // Atribuição gulosa pelos pares mais próximos.
        var pairs = new List<(int Item, int Slot, double Distance)>();
        for (int i = 0; i < valid.Count; i++)
        {
            for (int s = 0; s < slots.Count; s++)
            {
                pairs.Add((i, s, AnchorOf(slots[s], drawing).DistanceTo(representative[valid[i].Key])));
            }
        }

        var slotOf = new int[valid.Count];
        Array.Fill(slotOf, -1);
        var usedSlots = new HashSet<int>();
        foreach ((int item, int slot, _) in pairs.OrderBy(p => p.Distance))
        {
            if (slotOf[item] < 0 && !usedSlots.Contains(slot))
            {
                slotOf[item] = slot;
                usedSlots.Add(slot);
            }
        }

        // Para cada cartão, o exemplar da espécie mais próximo do cartão.
        Point2 Target(int i) => valid[i].Targets.OrderBy(t => t.DistanceTo(AnchorOf(slots[slotOf[i]], drawing))).First();

        // Desfaz cruzamentos trocando cartões entre espécies (2-opt), enquanto houver melhora.
        for (int pass = 0; pass < 20; pass++)
        {
            bool improved = false;
            for (int a = 0; a < valid.Count; a++)
            {
                for (int b = a + 1; b < valid.Count; b++)
                {
                    Point2 aAnchor = AnchorOf(slots[slotOf[a]], drawing);
                    Point2 bAnchor = AnchorOf(slots[slotOf[b]], drawing);
                    Point2 aTarget = Target(a);
                    Point2 bTarget = Target(b);
                    double before = aAnchor.DistanceTo(aTarget) + bAnchor.DistanceTo(bTarget);
                    bool crossing = Geo.SegmentsCross(aAnchor, aTarget, bAnchor, bTarget);
                    (slotOf[a], slotOf[b]) = (slotOf[b], slotOf[a]);
                    double after = AnchorOf(slots[slotOf[a]], drawing).DistanceTo(Target(a)) + AnchorOf(slots[slotOf[b]], drawing).DistanceTo(Target(b));
                    bool crossingAfter = Geo.SegmentsCross(AnchorOf(slots[slotOf[a]], drawing), Target(a), AnchorOf(slots[slotOf[b]], drawing), Target(b));
                    if ((crossing && !crossingAfter) || (crossing == crossingAfter && after < before - 1e-9))
                    {
                        improved = true;
                    }
                    else
                    {
                        (slotOf[a], slotOf[b]) = (slotOf[b], slotOf[a]);
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }

        var cards = new List<PhotoCard>(valid.Count);
        for (int i = 0; i < valid.Count; i++)
        {
            Bounds2 box = slots[slotOf[i]];
            cards.Add(new PhotoCard(valid[i].Key, box, AnchorOf(box, drawing), Target(i)));
        }

        return cards;
    }

    /// <summary>Posições disponíveis em um anel ao redor do desenho (0 = mais próximo).</summary>
    private static IEnumerable<Bounds2> Ring(Bounds2 d, double w, double h, double gap, int ring)
    {
        double offsetX = gap + ring * (w + gap);
        double offsetY = gap + ring * (h + gap);
        double left = d.MinX - offsetX - w;
        double right = d.MaxX + offsetX;
        double bottom = d.MinY - offsetY - h;
        double top = d.MaxY + offsetY;

        // Colunas laterais (de cima para baixo) e faixas superior e inferior (da esquerda para a direita).
        int rows = Math.Max(1, (int)Math.Floor((top + h - bottom + gap) / (h + gap)));
        for (int r = 0; r < rows; r++)
        {
            double y = top + h - (r + 1) * (h + gap) + gap;
            yield return new Bounds2(left, y, left + w, y + h);
            yield return new Bounds2(right, y, right + w, y + h);
        }

        int columns = (int)Math.Floor((right - (left + w) - gap) / (w + gap));
        double start = left + w + gap + ((right - (left + w) - gap) - columns * (w + gap)) / 2;
        for (int c = 0; c < columns; c++)
        {
            double x = start + c * (w + gap);
            yield return new Bounds2(x, top, x + w, top + h);
            yield return new Bounds2(x, bottom, x + w, bottom + h);
        }
    }

    /// <summary>Ponto de saída da linha: o meio do lado do cartão voltado para o desenho.</summary>
    public static Point2 AnchorOf(Bounds2 card, Bounds2 drawing)
    {
        if (card.MaxX <= drawing.MinX)
        {
            return new Point2(card.MaxX, card.Center.Y);
        }

        if (card.MinX >= drawing.MaxX)
        {
            return new Point2(card.MinX, card.Center.Y);
        }

        return card.MinY >= drawing.MaxY ? new Point2(card.Center.X, card.MinY) : new Point2(card.Center.X, card.MaxY);
    }
}
