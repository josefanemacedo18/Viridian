using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>
/// Quadro do calendário de floração para vista de desenho: uma linha por espécie e uma coluna por mês, com as
/// células preenchidas na cor da flor. Medidas em metros no papel (desenhar em escala 1:1).
/// </summary>
public static class CalendarDrawing
{
    public const double NameColumnM = 0.075;
    public const double MonthColumnM = 0.009;
    public const double RowM = 0.006;

    public static Drawing Build(FloweringCalendar calendar, ColorSettings colors, string title)
    {
        var g = new Drawing { Title = title };
        List<CalendarRow> rows = calendar.Rows.Where(r => !r.Unknown).ToList();
        double width = NameColumnM + 12 * MonthColumnM;
        double top = 0;

        g.Text(new Point2(0, top + RowM * 1.6), title.ToUpperInvariant(), -1, 1.3);
        for (int m = 0; m < 12; m++)
        {
            g.Text(new Point2(NameColumnM + (m + 0.5) * MonthColumnM, top - RowM * 0.25), MonthSet.Abbreviations[m], 0, 0.85);
        }

        double y = top - RowM;
        foreach (CalendarRow row in rows)
        {
            Rgb color = Rgb.TryParse(row.Species.FlowerColor, out Rgb flower) ? flower : VegetationPalette.CrownFill(row.Species, colors);
            for (int m = 1; m <= 12; m++)
            {
                if (row.Months.Contains(m))
                {
                    double x = NameColumnM + (m - 1) * MonthColumnM;
                    var cell = new List<Point2>
                    {
                        new(x + 0.0006, y - 0.0006), new(x + MonthColumnM - 0.0006, y - 0.0006),
                        new(x + MonthColumnM - 0.0006, y - RowM + 0.0006), new(x + 0.0006, y - RowM + 0.0006),
                    };
                    g.Fills.Add(new DrawFill(cell, color));
                    if (color.Luminance > 0.75)
                    {
                        g.Polyline(cell, true); // flores brancas e muito claras: contorno para não sumirem
                    }
                }
            }

            g.Text(new Point2(0.0015, y - RowM * 0.3), $"{row.Code} {row.Species.PopularName}", -1, 0.9);
            y -= RowM;
        }

        // Grade.
        for (int i = 0; i <= rows.Count + 1; i++)
        {
            double yy = top - i * RowM;
            g.Line(new Point2(0, yy), new Point2(width, yy), heavy: i <= 1);
        }

        double bottom = top - (rows.Count + 1) * RowM;
        g.Line(new Point2(0, top), new Point2(0, bottom));
        for (int m = 0; m <= 12; m++)
        {
            double x = NameColumnM + m * MonthColumnM;
            g.Line(new Point2(x, top), new Point2(x, bottom), heavy: m == 0 || m == 12);
        }

        // Resumo de meses sem floração vistosa.
        MonthSet gaps = calendar.ShowyGaps;
        string note = !rows.Any(r => r.Showy)
            ? "Composição sem floração vistosa."
            : gaps.IsEmpty ? "Floração vistosa em todos os meses do ano." : $"Sem floração vistosa em: {gaps}.";
        g.Text(new Point2(0, bottom - RowM * 0.8), note, -1, 0.9);
        List<CalendarRow> unknown = calendar.Unknown.ToList();
        if (unknown.Count > 0)
        {
            g.Text(new Point2(0, bottom - RowM * 1.8), "Sem época cadastrada: " + string.Join(", ", unknown.Select(u => u.Species.PopularName)), -1, 0.8);
        }

        return g;
    }
}
