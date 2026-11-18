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

    /// <summary>Larguras das colunas no papel (m): código, espécie, quantidade, muda, escavação, especificação.</summary>
    public static readonly double[] Columns = { 0.016, 0.050, 0.020, 0.016, 0.028, 0.150 };

    public static double Width => Columns.Sum();

    /// <param name="textMm">Altura do texto no papel (mm), usada para a altura das linhas e a quebra do texto.</param>
    public static Drawing Build(IReadOnlyList<SpecificationItem> items, SpecificationSettings specs, double textMm, string title = "Quadro de especificação da vegetação")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(specs);
        textMm = Math.Clamp(double.IsFinite(textMm) ? textMm : 2.0, 1.0, 6.0);
        double line = textMm * 1.7 / 1000;
        double charWidth = textMm * 0.6 / 1000;
        int Chars(double columnWidth) => Math.Max(8, (int)Math.Floor((columnWidth - 0.003) / charWidth));

        var g = new Drawing { Title = title };
        double width = Width;
        double y = 0;
        g.Text(new Point2(0, y + line * 1.2), title.ToUpperInvariant(), -1, 1.3);

        // Nota geral sobre toda a largura.
        List<string> general = TextLayout.Wrap(specs.General, Chars(width)).ToList();
        if (general.Count > 0)
        {
            y -= line * 0.6;
            foreach (string text in general)
            {
                g.Text(new Point2(0, y), text, -1, 0.9);
                y -= line * 0.95;
            }

            y -= line * 0.4;
        }

        string[] headers = { "CÓD.", "ESPÉCIE", "QTD.", "MUDA", "ESCAVAÇÃO", "ESPECIFICAÇÃO" };
        double top = y;
        double headerHeight = line * 1.4;
        double x = 0;
        for (int c = 0; c < headers.Length; c++)
        {
            g.Text(new Point2(x + 0.0015, top - headerHeight / 2), headers[c], -1, 0.9);
            x += Columns[c];
        }

        g.Line(new Point2(0, top), new Point2(width, top), heavy: true);
        y = top - headerHeight;
        g.Line(new Point2(0, y), new Point2(width, y), heavy: true);
        var rowLines = new List<double> { top, y };

        foreach (SpecificationItem item in items.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Species.PopularName, StringComparer.OrdinalIgnoreCase))
        {
            Species withCode = item.Species.Clone();
            withCode.Code = item.Code;
            PlantingDimensions d = PlantingDetails.Dimensions(item.Species);
            var cells = new List<string>[]
            {
                TextLayout.Wrap(item.Code, Chars(Columns[0])).ToList(),
                TextLayout.Wrap(item.Species.PopularName, Chars(Columns[1])).Concat(TextLayout.Wrap(item.Species.ScientificName, Chars(Columns[1]))).ToList(),
                TextLayout.Wrap(Quantity(item), Chars(Columns[2])).ToList(),
                TextLayout.Wrap(d.SeedlingHeightM > 0 ? d.SeedlingHeightM.ToString("0.00", Pt) + " m" : "—", Chars(Columns[3])).ToList(),
                TextLayout.Wrap(PlantingDetails.ExcavationText(d), Chars(Columns[4])).ToList(),
                TextLayout.Wrap(SpecificationTemplates.Render(specs, withCode), Chars(Columns[5])).ToList(),
            };
            int lines = Math.Max(1, cells.Max(c => c.Count));
            double rowHeight = lines * line + line * 0.5;
            x = 0;
            for (int c = 0; c < cells.Length; c++)
            {
                double ty = y - line * 0.75;
                for (int k = 0; k < cells[c].Count; k++)
                {
                    // O nome científico (segunda parte da coluna de espécie) vai em tamanho menor.
                    bool scientific = c == 1 && k >= TextLayout.Wrap(item.Species.PopularName, Chars(Columns[1])).Count();
                    g.Text(new Point2(x + 0.0015, ty), cells[c][k], -1, scientific ? 0.85 : 0.9);
                    ty -= line;
                }

                x += Columns[c];
            }

            y -= rowHeight;
            g.Line(new Point2(0, y), new Point2(width, y));
            rowLines.Add(y);
        }

        // Verticais.
        x = 0;
        for (int c = 0; c <= Columns.Length; c++)
        {
            g.Line(new Point2(x, top), new Point2(x, y), heavy: c == 0 || c == Columns.Length);
            if (c < Columns.Length)
            {
                x += Columns[c];
            }
        }

        g.Text(new Point2(0, y - line * 0.9), "Dimensões e especificações de referência: confira com as normas locais e com o responsável técnico.", -1, 0.8);
        return g;
    }

    private static string Quantity(SpecificationItem item)
    {
        string number = item.Quantity.ToString(item.Quantity % 1 == 0 ? "N0" : "N1", Pt);
        return number + " " + Memorial.MemorialBuilder.Plural(item.Unit, item.Quantity);
    }
}
