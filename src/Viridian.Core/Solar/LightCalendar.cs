using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;

namespace Viridian.Core.Solar;

/// <summary>Linha do calendário de luz: a região ou uma espécie, com as horas de sol em cada data.</summary>
public sealed record LightCalendarRow(string Label, IReadOnlyList<double> Hours, PlantLight? Light, int Plants);

/// <summary>
/// Calendário de luz para vista de desenho: nascer e pôr do sol, duração do dia e horas de sol direto da região e
/// de cada espécie em cada mês (ou data), com as células na cor da faixa de luz e a marcação dos meses em que a
/// espécie não recebe a luz que pede. Medidas em metros no papel (desenhar em escala 1:1).
/// </summary>
public static class LightCalendar
{
    public const double NameColumnM = 0.080;
    public const double DateColumnM = 0.0125;
    public const double SummaryColumnM = 0.016;
    public const double RowM = 0.0062;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static Drawing Build(string title, string period, SolarSite site, IReadOnlyList<SolarDate> dates, IReadOnlyList<LightCalendarRow> rows, SolarSettings s, bool buildings)
    {
        var g = new Drawing { Title = title };
        double width = NameColumnM + dates.Count * DateColumnM + SummaryColumnM;
        double y = 0;
        g.Text(new Point2(0, y + RowM * 3.6), title.ToUpperInvariant(), -1, 1.3);
        g.Text(new Point2(0, y + RowM * 2.4), site.Describe(), -1, 0.85);
        g.Text(new Point2(0, y + RowM * 1.4), period + ". Horas de sol direto num dia típico; horários em hora legal, sem horário de verão.", -1, 0.8);

        // Cabeçalho.
        for (int d = 0; d < dates.Count; d++)
        {
            g.Text(new Point2(NameColumnM + (d + 0.5) * DateColumnM, y - RowM * 0.3), dates[d].Short, 0, 0.85);
        }

        g.Text(new Point2(NameColumnM + dates.Count * DateColumnM + SummaryColumnM / 2, y - RowM * 0.3), "atende", 0, 0.8);
        int line = 1;

        // Sol do dia.
        void InfoRow(string label, Func<SolarDate, string> value)
        {
            double yy = y - line * RowM;
            g.Text(new Point2(0.0015, yy - RowM * 0.32), label, -1, 0.85);
            for (int d = 0; d < dates.Count; d++)
            {
                g.Text(new Point2(NameColumnM + (d + 0.5) * DateColumnM, yy - RowM * 0.32), value(dates[d]), 0, 0.72);
            }

            line++;
        }

        InfoRow("Nascer do sol", d => Sun.SunriseSunset(site.LatitudeDeg, d.DayOfYear) is (double rise, _) ? site.ClockText(rise, d.DayOfYear) : "—");
        InfoRow("Pôr do sol", d => Sun.SunriseSunset(site.LatitudeDeg, d.DayOfYear) is (_, double set) ? site.ClockText(set, d.DayOfYear) : "—");
        InfoRow("Duração do dia (h)", d => Sun.SunriseSunset(site.LatitudeDeg, d.DayOfYear) is (double r, double t) ? (t - r).ToString("0.0", Pt) : "—");
        int infoLines = line;

        foreach (LightCalendarRow row in rows)
        {
            double yy = y - line * RowM;
            for (int d = 0; d < dates.Count && d < row.Hours.Count; d++)
            {
                double x = NameColumnM + d * DateColumnM;
                var cell = new List<Point2>
                {
                    new(x + 0.0005, yy - 0.0005), new(x + DateColumnM - 0.0005, yy - 0.0005),
                    new(x + DateColumnM - 0.0005, yy - RowM + 0.0005), new(x + 0.0005, yy - RowM + 0.0005),
                };
                g.Fills.Add(new DrawFill(cell, Rgb.Parse(SolarClasses.ColorOf(row.Hours[d], s), Rgb.White)));
                string mark = row.Light?.Fits[d] switch
                {
                    LightFit.NaoAtende => " ×",
                    LightFit.Limite => " ~",
                    _ => string.Empty,
                };
                g.Text(new Point2(x + DateColumnM / 2, yy - RowM * 0.32), row.Hours[d].ToString("0.0", Pt) + mark, 0, 0.72);
            }

            g.Text(new Point2(0.0015, yy - RowM * 0.32), Fit(row.Label, 46), -1, 0.82);
            string summary = row.Light is PlantLight light ? $"{light.Meeting}/{light.Fits.Count}" : "—";
            g.Text(new Point2(NameColumnM + dates.Count * DateColumnM + SummaryColumnM / 2, yy - RowM * 0.32), summary, 0, 0.8);
            line++;
        }

        // Grade.
        double bottom = y - line * RowM;
        for (int i = 0; i <= line; i++)
        {
            double yy = y - i * RowM;
            g.Line(new Point2(0, yy), new Point2(width, yy), heavy: i <= 1 || i == infoLines);
        }

        g.Line(new Point2(0, y), new Point2(0, bottom), heavy: true);
        for (int d = 0; d <= dates.Count; d++)
        {
            double x = NameColumnM + d * DateColumnM;
            g.Line(new Point2(x, y), new Point2(x, bottom), heavy: d == 0 || d == dates.Count);
        }

        g.Line(new Point2(width, y), new Point2(width, bottom), heavy: true);

        // Legenda das faixas e notas.
        double ly = bottom - RowM * 1.2;
        double lx = 0;
        foreach ((double _, double _, string color, string label) in SolarClasses.Bins(s))
        {
            var box = new List<Point2> { new(lx, ly), new(lx + 0.006, ly), new(lx + 0.006, ly - 0.004), new(lx, ly - 0.004) };
            g.Fills.Add(new DrawFill(box, Rgb.Parse(color, Rgb.White)));
            g.Polyline(box, true);
            g.Text(new Point2(lx + 0.008, ly - 0.0028), label, -1, 0.75);
            lx += 0.008 + 0.0016 * label.Length + 0.006;
        }

        ly -= RowM * 1.3;
        g.Text(new Point2(0, ly), $"× não atende à luz que a espécie pede; ~ no limite (até {SolarClasses.Hours(s.ToleranceHours)} h fora da faixa). "
                                   + "\"Atende\": meses (ou datas) dentro da faixa ou no limite.", -1, 0.75);
        g.Text(new Point2(0, ly - RowM * 0.9), "Sombra das copas adultas" + (buildings ? ", das edificações e do terreno (também de vínculos)." : ".")
                                              + " Valores de referência para o projeto; confira em campo.", -1, 0.75);
        return g;
    }

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
