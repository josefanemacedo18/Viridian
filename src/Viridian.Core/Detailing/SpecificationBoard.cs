using System.Globalization;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Linha do quadro de especificação: a espécie com o código e a quantidade do projeto.</summary>
public sealed record SpecificationItem(Species Species, string Code, double Quantity, string Unit);

/// <summary>
/// Quadro de especificação para a prancha: uma linha por espécie com código, nomes, quantidade, muda, escavação e o
/// texto da nota de especificação (os mesmos modelos do detalhe de plantio e do memorial). Medidas em metros no papel
/// (vista em escala 1:1).
/// </summary>
public static class SpecificationBoard
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Larguras das colunas no papel (m): símbolo, código, espécie, quantidade, muda, escavação, especificação.</summary>
    public static readonly double[] Columns = { 0.011, 0.016, 0.050, 0.020, 0.016, 0.028, 0.150 };

    public static double Width => Columns.Sum();

    /// <param name="textMm">Altura do texto no papel (mm), usada para a altura das linhas e a quebra do texto.</param>
    /// <param name="colors">Cores para a miniatura de cada espécie; nulo = cores padrão.</param>
    public static Drawing Build(IReadOnlyList<SpecificationItem> items, SpecificationSettings specs, double textMm, string title = "Quadro de especificação da vegetação",
        Colors.ColorSettings? colors = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(specs);
        colors ??= new Colors.ColorSettings();
        textMm = Math.Clamp(double.IsFinite(textMm) ? textMm : 2.0, 1.0, 6.0);
        double mm = 0.001;
        double line = textMm * 1.7 / 1000;
        double charWidth = textMm * 0.6 / 1000;
        int Chars(double columnWidth) => Math.Max(8, (int)Math.Floor((columnWidth - 0.003) / charWidth));

        var g = new Drawing { Title = title };
        double width = Width;

        // Faixa de título com o resumo.
        double band = textMm * 4.2 * mm;
        g.Box(0, 0, width, band, DrawingStyle.Primary);
        g.Text(new Point2(0.004, band / 2), title.ToUpperInvariant(), TextStyle.Claro, -1, 1.35);
        g.Text(new Point2(width - 0.004, band / 2), Summary(items), TextStyle.Claro, 1, 0.95);
        double y = -line * 0.4;

        // Nota geral numa faixa clara sobre toda a largura.
        List<string> general = TextLayout.Wrap(specs.General, Chars(width - 0.004)).ToList();
        if (general.Count > 0)
        {
            double noteHeight = general.Count * line * 0.95 + line * 0.7;
            g.Box(0, y - noteHeight, width, y, DrawingStyle.Tint);
            double ny = y - line * 0.75;
            foreach (string text in general)
            {
                g.Text(new Point2(0.003, ny), text, TextStyle.Normal, -1, 0.9);
                ny -= line * 0.95;
            }

            y -= noteHeight + line * 0.4;
        }

        string[] headers = { string.Empty, "CÓD.", "ESPÉCIE", "QTD.", "MUDA", "ESCAVAÇÃO", "ESPECIFICAÇÃO" };
        double top = y;
        double headerHeight = line * 1.5;
        g.Box(0, top - headerHeight, width, top, DrawingStyle.Secondary);
        double x = 0;
        for (int c = 0; c < headers.Length; c++)
        {
            g.Text(new Point2(x + 0.0015, top - headerHeight / 2), headers[c], TextStyle.Claro, -1, 0.9);
            x += Columns[c];
        }

        y = top - headerHeight;
        int row = 0;
        foreach (SpecificationItem item in items.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Species.PopularName, StringComparer.OrdinalIgnoreCase))
        {
            Species withCode = item.Species.Clone();
            withCode.Code = item.Code;
            PlantingDimensions d = PlantingDetails.Dimensions(item.Species);
            List<string> popular = TextLayout.Wrap(item.Species.PopularName, Chars(Columns[2])).ToList();
            List<string> scientific = TextLayout.Wrap(item.Species.ScientificName, Chars(Columns[2])).ToList();
            var cells = new List<string>[]
            {
                new(),
                TextLayout.Wrap(item.Code, Chars(Columns[1])).ToList(),
                popular.Concat(scientific).ToList(),
                TextLayout.Wrap(Quantity(item), Chars(Columns[3])).ToList(),
                TextLayout.Wrap(d.SeedlingHeightM > 0 ? d.SeedlingHeightM.ToString("0.00", Pt) + " m" : "—", Chars(Columns[4])).ToList(),
                TextLayout.Wrap(PlantingDetails.ExcavationText(d), Chars(Columns[5])).ToList(),
                TextLayout.Wrap(SpecificationTemplates.Render(specs, withCode), Chars(Columns[6])).ToList(),
            };
            int lines = Math.Max(2, cells.Max(c => c.Count));
            double rowHeight = lines * line + line * 0.5;
            if (row % 2 == 1)
            {
                g.Box(0, y - rowHeight, width, y, DrawingStyle.Paper);
            }

            // Miniatura: a copa da espécie na cor da folhagem, com a flor no centro.
            double r = Math.Min(Columns[0] * 0.36, rowHeight * 0.36);
            var center = new Point2(Columns[0] / 2, y - Math.Min(rowHeight / 2, line * 1.3));
            g.Fill(Mini(item.Species, center, r), Colors.VegetationPalette.Foliage(item.Species, colors));
            g.Strokes(Mini(item.Species, center, r), true, LinePen.Fina);
            if (Colors.VegetationPalette.Flower(item.Species, colors) is Colors.Rgb flower)
            {
                g.Fill(Drawing.Ellipse(center, r * 0.32, r * 0.32, 16), flower);
            }

            x = Columns[0];
            for (int c = 1; c < cells.Length; c++)
            {
                double ty = y - line * 0.75;
                for (int k = 0; k < cells[c].Count; k++)
                {
                    TextStyle style = c switch
                    {
                        1 => TextStyle.Subtitulo,
                        2 when k < popular.Count => TextStyle.Subtitulo,
                        2 => TextStyle.Cientifico,
                        _ => TextStyle.Normal,
                    };
                    g.Text(new Point2(x + 0.0015, ty), cells[c][k], style, -1, c == 2 && k >= popular.Count ? 0.85 : 0.9);
                    ty -= line;
                }

                x += Columns[c];
            }

            y -= rowHeight;
            g.Stroke(new Point2(0, y), new Point2(width, y), LinePen.Textura);
            row++;
        }

        // Totais por unidade de fornecimento.
        double totalHeight = line * 1.5;
        g.Box(0, y - totalHeight, width, y, DrawingStyle.Tint);
        g.Text(new Point2(Columns[0] + 0.0015, y - totalHeight / 2), "TOTAL", TextStyle.Subtitulo, -1, 0.9);
        g.Text(new Point2(Columns.Take(3).Sum() + 0.0015, y - totalHeight / 2), Totals(items), TextStyle.Subtitulo, -1, 0.9);
        y -= totalHeight;

        // Moldura e divisões das colunas.
        g.Strokes(new List<Point2> { new(0, top), new(width, top), new(width, y), new(0, y) }, true, LinePen.Grossa);
        x = 0;
        for (int c = 0; c < Columns.Length - 1; c++)
        {
            x += Columns[c];
            g.Stroke(new Point2(x, top), new Point2(x, y + totalHeight), LinePen.Textura);
        }

        g.Text(new Point2(0, y - line * 0.9), "Dimensões e especificações de referência: confira com as normas locais e com o responsável técnico.", TextStyle.Nota, -1, 0.8);
        return g;
    }

    private static List<Point2> Mini(Species species, Point2 center, double radius)
    {
        IReadOnlyList<Point2> fill = Symbols.SymbolGenerator.Generate(species.SymbolStyle, 1.0, species.EffectiveSymbolSeed).FillOutline;
        return fill.Count < 3 ? Drawing.Ellipse(center, radius, radius, 24) : fill.Select(p => center + p * (radius * 2)).ToList();
    }

    /// <summary>Resumo para a faixa de título, por exemplo "6 espécies".</summary>
    private static string Summary(IReadOnlyList<SpecificationItem> items)
    {
        int species = items.Select(i => i.Species.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return species == 1 ? "1 espécie" : species + " espécies";
    }

    /// <summary>Totais por unidade de fornecimento, por exemplo "31 mudas · 120 m²".</summary>
    public static string Totals(IReadOnlyList<SpecificationItem> items)
    {
        if (items.Count == 0)
        {
            return "—";
        }

        return string.Join(" · ", items
            .GroupBy(i => (i.Unit ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                double sum = g.Sum(i => i.Quantity);
                return sum.ToString(sum % 1 == 0 ? "N0" : "N1", Pt) + " " + Memorial.MemorialBuilder.Plural(g.Key, sum);
            }));
    }

    private static string Quantity(SpecificationItem item)
    {
        string number = item.Quantity.ToString(item.Quantity % 1 == 0 ? "N0" : "N1", Pt);
        return number + " " + Memorial.MemorialBuilder.Plural(item.Unit, item.Quantity);
    }
}
