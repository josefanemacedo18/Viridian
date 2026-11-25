using Viridian.Core.Geometry;

namespace Viridian.Core.Detailing;

/// <summary>Linha da legenda de vegetação.</summary>
/// <param name="Group">Grupo (categoria) da linha: cada grupo ganha um subtítulo.</param>
public sealed record LegendRow(string Code, string PopularName, string ScientificName, string Quantity, string Group);

/// <summary>Onde o símbolo (ou a hachura) e a foto de cada linha devem ser colocados (mesmas unidades do desenho).</summary>
public sealed record LegendSlot(int Row, Bounds2 Symbol, Bounds2? Photo);

/// <summary>Resultado da diagramação da legenda.</summary>
public sealed class LegendLayoutResult
{
    public Drawing Drawing { get; } = new() { Title = "Legenda de vegetação" };

    public List<LegendSlot> Slots { get; } = new();
}

/// <summary>
/// Diagramação da legenda de vegetação: faixa de título, cabeçalho, grupos por categoria com subtítulo, linhas
/// alternadas, nome popular em negrito e científico em itálico. Os símbolos, hachuras e fotos são colocados depois,
/// nas posições devolvidas (são elementos nativos do Revit).
/// </summary>
public static class LegendLayout
{
    /// <param name="unit">Metros do desenho por milímetro de papel (escala / 1000).</param>
    public static LegendLayoutResult Build(IReadOnlyList<LegendRow> rows, bool withPhotos, string title, double unit)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var result = new LegendLayoutResult();
        Drawing g = result.Drawing;
        double U(double mm) => mm * unit;
        double rowHeight = withPhotos ? 26 : 13;
        double[] columns = withPhotos
            ? new double[] { 0, 24, 60, 82, 132, 192, 230 }
            : new double[] { 0, 20, 42, 92, 152, 190 };
        string[] headers = withPhotos
            ? new[] { "SÍMBOLO", "FOTO", "CÓDIGO", "NOME POPULAR", "NOME CIENTÍFICO", "QUANTIDADE" }
            : new[] { "SÍMBOLO", "CÓDIGO", "NOME POPULAR", "NOME CIENTÍFICO", "QUANTIDADE" };
        int text = withPhotos ? 2 : 1;
        double width = columns[^1];

        // Faixa de título.
        g.Box(U(0), U(0), U(width), U(11), DrawingStyle.Primary);
        g.Text(new Point2(U(3), U(5.5)), title.ToUpperInvariant(), TextStyle.Claro, -1, 1.25);
        g.Text(new Point2(U(width - 3), U(5.5)), rows.Count == 1 ? "1 item" : rows.Count + " itens", TextStyle.Claro, 1, 0.9);

        // Cabeçalho.
        double y = -1.5;
        g.Box(U(0), U(y - 7), U(width), U(y), DrawingStyle.Secondary);
        for (int c = 0; c < headers.Length; c++)
        {
            g.Text(new Point2(U(columns[c] + 1.5), U(y - 3.5)), headers[c], TextStyle.Claro, -1, 0.85);
        }

        double top = y;
        y -= 7;
        string? group = null;
        int zebra = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            LegendRow row = rows[i];
            if (!string.Equals(row.Group, group, StringComparison.Ordinal))
            {
                group = row.Group;
                zebra = 0;
                if (group.Length > 0)
                {
                    g.Box(U(0), U(y - 6), U(width), U(y), DrawingStyle.Tint);
                    g.Text(new Point2(U(1.5), U(y - 3)), group.ToUpperInvariant(), TextStyle.Titulo, -1, 0.9);
                    y -= 6;
                }
            }

            double rowTop = y, rowBottom = y - rowHeight;
            if (zebra++ % 2 == 1)
            {
                g.Box(U(0), U(rowBottom), U(width), U(rowTop), DrawingStyle.Paper);
            }

            double cy = (rowTop + rowBottom) / 2;
            var symbol = new Bounds2(U(columns[0] + 2.5), U(rowBottom + 2), U(columns[1] - 2.5), U(rowTop - 2));
            Bounds2? photo = withPhotos ? new Bounds2(U(columns[1] + 1.5), U(rowBottom + 1.5), U(columns[2] - 1.5), U(rowTop - 1.5)) : null;
            result.Slots.Add(new LegendSlot(i, symbol, photo));

            g.Text(new Point2(U(columns[text] + 1.5), U(cy)), row.Code, TextStyle.Subtitulo, -1, 1.0);
            g.Text(new Point2(U(columns[text + 1] + 1.5), U(cy)), row.PopularName, TextStyle.Subtitulo, -1, 1.0);
            g.Text(new Point2(U(columns[text + 2] + 1.5), U(cy)), row.ScientificName, TextStyle.Cientifico, -1, 1.0);
            g.Text(new Point2(U(columns[text + 3] + 1.5), U(cy)), row.Quantity, TextStyle.Normal, -1, 1.0);
            y = rowBottom;
            g.Stroke(new Point2(U(0), U(y)), new Point2(U(width), U(y)), LinePen.Textura);
        }

        // Moldura e divisões das colunas.
        g.Strokes(new List<Point2> { new(U(0), U(top)), new(U(width), U(top)), new(U(width), U(y)), new(U(0), U(y)) }, true, LinePen.Grossa);
        for (int c = 1; c < columns.Length - 1; c++)
        {
            g.Stroke(new Point2(U(columns[c]), U(top)), new Point2(U(columns[c]), U(y)), LinePen.Textura);
        }

        return result;
    }
}
