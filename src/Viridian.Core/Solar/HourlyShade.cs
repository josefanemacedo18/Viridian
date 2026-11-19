using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model3D;

namespace Viridian.Core.Solar;

/// <summary>Sombras num horário de uma data: luz em cada ponto da malha e em cada planta e o contorno da sombra de cada copa.</summary>
public sealed class ShadeFrame
{
    public SolarDate Date { get; init; } = new(string.Empty, 1, 1);

    /// <summary>Hora legal (do relógio, sem horário de verão).</summary>
    public int ClockHour { get; init; }

    public SunPosition Sun { get; init; }

    /// <summary>Direção para o sol nas coordenadas do modelo.</summary>
    public Vec3 ToSun { get; init; }

    /// <summary>Fração de luz direta (0 a 1) em cada ponto da malha.</summary>
    public double[] Grid { get; init; } = Array.Empty<double>();

    /// <summary>Fração de luz direta em cada planta analisada.</summary>
    public double[] Plants { get; init; } = Array.Empty<double>();

    /// <summary>Sombra de cada copa no chão (contorno em metros) e a opacidade da copa no mês.</summary>
    public List<(string Id, IReadOnlyList<Point2> Outline, double Opacity)> Shadows { get; } = new();

    /// <summary>Parte da malha à sombra (luz abaixo de 50%).</summary>
    public double ShadedFraction => Grid.Length == 0 ? 0 : Grid.Count(l => l < 0.5) / (double)Grid.Length;

    public string HourText => $"{ClockHour:00}:00";
}

/// <summary>Cálculo das sombras de hora em hora (hora legal) a partir das coordenadas da obra.</summary>
public static class HourlyShade
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Horas cheias entre <paramref name="from"/> e <paramref name="to"/> com o sol a mais de 2° do horizonte.</summary>
    public static List<int> Hours(SolarSite site, SolarDate date, int from, int to)
    {
        var hours = new List<int>();
        for (int h = Math.Clamp(from, 0, 23); h <= Math.Clamp(to, 0, 23); h++)
        {
            if (SunAt(site, date, h).AltitudeDeg > 2)
            {
                hours.Add(h);
            }
        }

        return hours;
    }

    /// <summary>Posição do sol na hora legal informada.</summary>
    public static SunPosition SunAt(SolarSite site, SolarDate date, double clockHour) =>
        Sun.Position(site.LatitudeDeg, date.DayOfYear, Sun.SolarHour(clockHour, site.LongitudeDeg, site.TimeZoneHours, date.DayOfYear));

    /// <summary>Direção de onde vem o sol, em pontos cardeais (N, NE, L, SE, S, SO, O, NO).</summary>
    public static string Cardinal(double azimuthDeg)
    {
        string[] names = { "N", "NE", "L", "SE", "S", "SO", "O", "NO" };
        int index = (int)Math.Round((((azimuthDeg % 360) + 360) % 360) / 45.0) % 8;
        return names[index];
    }

    public static string SunText(SunPosition sun) =>
        $"sol a {sun.AltitudeDeg.ToString("0", Pt)}° de altura, vindo de {Cardinal(sun.AzimuthDeg)} ({sun.AzimuthDeg.ToString("0", Pt)}°)";

    /// <summary>Sombras de hora em hora numa data, na malha e nas plantas.</summary>
    public static List<ShadeFrame> Compute(
        IReadOnlyList<Vec3> grid,
        IReadOnlyList<(Vec3 Position, string IgnoreId)> plants,
        IReadOnlyList<CrownObstacle> crowns,
        SolarSite site,
        SolarDate date,
        int fromHour,
        int toHour,
        Func<Vec3, Vec3> toModel,
        Func<Vec3, Vec3, bool>? blockedByBuildings,
        CancellationToken cancel = default)
    {
        var frames = new List<ShadeFrame>();
        foreach (int hour in Hours(site, date, fromHour, toHour))
        {
            cancel.ThrowIfCancellationRequested();
            SunPosition sun = SunAt(site, date, hour);
            Vec3 toSun = toModel(sun.Direction).Normalized();
            var frame = new ShadeFrame
            {
                Date = date,
                ClockHour = hour,
                Sun = sun,
                ToSun = toSun,
                Grid = grid.Select(p => SolarStudy.Light(p, string.Empty, toSun, date.Month, crowns, blockedByBuildings)).ToArray(),
                Plants = plants.Select(p => SolarStudy.Light(p.Position, p.IgnoreId, toSun, date.Month, crowns, blockedByBuildings)).ToArray(),
            };
            foreach (CrownObstacle c in crowns)
            {
                IReadOnlyList<Point2> outline = c.ShadowOnGround(toSun);
                if (outline.Count >= 3)
                {
                    frame.Shadows.Add((c.Id, outline, c.OpacityIn(date.Month)));
                }
            }

            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>
    /// Junta pontos vizinhos da malha (mesma linha, mesma classe) em faixas retangulares: muito menos elementos no Revit.
    /// <paramref name="classOf"/> devolve a classe de cada ponto (negativa = não desenhar).
    /// </summary>
    public static List<(int Class, Bounds2 Box)> Runs(IReadOnlyList<Point2> points, double cell, Func<int, int> classOf)
    {
        var result = new List<(int, Bounds2)>();
        double half = cell / 2;
        foreach (IGrouping<long, int> row in Enumerable.Range(0, points.Count).GroupBy(i => (long)Math.Round(points[i].Y / cell * 1000)))
        {
            List<int> ordered = row.OrderBy(i => points[i].X).ToList();
            int start = 0;
            for (int k = 1; k <= ordered.Count; k++)
            {
                bool breaks = k == ordered.Count
                              || classOf(ordered[k]) != classOf(ordered[start])
                              || points[ordered[k]].X - points[ordered[k - 1]].X > cell * 1.5;
                if (!breaks)
                {
                    continue;
                }

                int cls = classOf(ordered[start]);
                if (cls >= 0)
                {
                    Point2 a = points[ordered[start]], b = points[ordered[k - 1]];
                    result.Add((cls, new Bounds2(a.X - half, a.Y - half, b.X + half, b.Y + half)));
                }

                start = k;
            }
        }

        return result;
    }

    /// <summary>Classe de sombra: 0 = sombra (luz abaixo de 35%), 1 = meia-luz (35% a 75%), -1 = sol.</summary>
    public static int ShadeClass(double light) => light < 0.35 ? 0 : light < 0.75 ? 1 : -1;

    public static readonly Rgb ShadeColor = new(0x6E, 0x79, 0x8A);

    public static readonly Rgb PartialColor = new(0xB8, 0xC0, 0xCB);

    public static readonly Rgb SunColor = new(0xF6, 0xD8, 0x5A);
}

/// <summary>Planta analisada no quadro de sombras: rótulo e índices nas listas de plantas dos quadros.</summary>
public sealed record ShadeRow(string Label, IReadOnlyList<int> Plants);

/// <summary>
/// Quadro de sombras de uma data: uma pequena planta por horário (sombra da malha, contorno das copas e das suas
/// sombras, direção do sol) e, abaixo, a tabela de sol e sombra de cada espécie em cada horário. Medidas em metros no
/// papel (desenhar em escala 1:1).
/// </summary>
public static class ShadeSheet
{
    private const double PanelWidthM = 0.062;
    private const double MaxPanelHeightM = 0.075;
    private const double GapM = 0.010;
    private const double LabelM = 0.012;
    private const double RowM = 0.0055;
    private const double NameColumnM = 0.070;
    private const double HourColumnM = 0.0105;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static Drawing Build(
        string title,
        SolarSite site,
        SolarDate date,
        IReadOnlyList<ShadeFrame> frames,
        IReadOnlyList<Point2> grid,
        double cell,
        Polygon2 region,
        IReadOnlyList<CrownObstacle> crowns,
        IReadOnlyList<ShadeRow> rows,
        double trueNorthRad,
        bool buildings,
        string stageText)
    {
        var g = new Drawing { Title = title };
        g.Text(new Point2(0, 0.024), title.ToUpperInvariant(), -1, 1.3);
        g.Text(new Point2(0, 0.017), $"{date.Name}, dia {date.Day} · {site.Describe()}", -1, 0.85);
        g.Text(new Point2(0, 0.011), $"Hora legal, sem horário de verão. Sombras das copas ({stageText})"
                                     + (buildings ? ", das edificações e do terreno." : ".") + " Decíduas mais transparentes nos meses sem folhas.", -1, 0.8);
        Legend(g, new Point2(0, 0.005));

        if (frames.Count == 0)
        {
            g.Text(new Point2(0, -0.01), "O sol não fica acima do horizonte no intervalo de horas escolhido.", -1, 0.9);
            return g;
        }

        Bounds2 b = region.Bounds.Expand(Math.Max(2, Math.Max(region.Bounds.Width, region.Bounds.Height) * 0.12));
        double k = PanelWidthM / Math.Max(1e-6, b.Width);
        if (b.Height * k > MaxPanelHeightM)
        {
            k = MaxPanelHeightM / b.Height;
        }

        double pw = b.Width * k, ph = b.Height * k;
        int cols = frames.Count > 12 ? 5 : 4;
        double top = -0.006;
        for (int f = 0; f < frames.Count; f++)
        {
            double x0 = f % cols * (pw + GapM);
            double y0 = top - f / cols * (ph + LabelM + GapM) - LabelM;
            Panel(g, frames[f], grid, cell, region, crowns, b, k, new Point2(x0, y0), trueNorthRad);
        }

        int panelRows = (frames.Count + cols - 1) / cols;
        double tableTop = top - panelRows * (ph + LabelM + GapM) - 0.004;
        Table(g, frames, rows, new Point2(0, tableTop));
        return g;
    }

    private static void Legend(Drawing g, Point2 at)
    {
        (Rgb Color, string Text)[] items =
        {
            (HourlyShade.ShadeColor, "sombra"),
            (HourlyShade.PartialColor, "meia-luz (copa rala ou borda)"),
            (HourlyShade.SunColor, "sol (na tabela)"),
        };
        double x = at.X;
        foreach ((Rgb color, string text) in items)
        {
            var box = new List<Point2> { new(x, at.Y), new(x + 0.005, at.Y), new(x + 0.005, at.Y - 0.003), new(x, at.Y - 0.003) };
            g.Fills.Add(new DrawFill(box, color));
            g.Polyline(box, true);
            g.Text(new Point2(x + 0.0065, at.Y - 0.0024), text, -1, 0.75);
            x += 0.010 + 0.0015 * text.Length;
        }

        g.Text(new Point2(x, at.Y - 0.0024), "Contorno tracejado: sombra de cada copa no chão. Círculo: copa.", -1, 0.75);
    }

    private static void Panel(Drawing g, ShadeFrame frame, IReadOnlyList<Point2> grid, double cell, Polygon2 region,
        IReadOnlyList<CrownObstacle> crowns, Bounds2 b, double k, Point2 origin, double trueNorthRad)
    {
        Point2 P(Point2 p) => new(origin.X + (p.X - b.MinX) * k, origin.Y - (b.MaxY - p.Y) * k);
        List<Point2> Map(IEnumerable<Point2> pts) => pts.Select(P).ToList();

        // Sombra na malha (faixas da mesma classe).
        foreach ((int cls, Bounds2 box) in HourlyShade.Runs(grid, cell, i => HourlyShade.ShadeClass(frame.Grid[i])))
        {
            var rect = new List<Point2> { new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY) };
            g.Fills.Add(new DrawFill(Map(rect), cls == 0 ? HourlyShade.ShadeColor : HourlyShade.PartialColor));
        }

        // Contorno da região e moldura.
        foreach (IReadOnlyList<Point2> ring in region.Rings)
        {
            g.Polyline(Map(ring), true, heavy: true);
        }

        var frameBox = new List<Point2> { new(b.MinX, b.MinY), new(b.MaxX, b.MinY), new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY) };
        g.Polyline(Map(frameBox), true);

        // Sombras das copas (tracejadas) e copas (círculos), recortadas na moldura.
        foreach ((string _, IReadOnlyList<Point2> outline, double _) in frame.Shadows)
        {
            List<Point2> clipped = Geo.ClipToRect(outline, b);
            if (clipped.Count >= 3)
            {
                g.Polyline(Map(clipped), true, dashed: true);
            }
        }

        foreach (CrownObstacle c in crowns)
        {
            var circle = Enumerable.Range(0, 20).Select(i => new Point2(c.Center.X + c.Radius * Math.Cos(Geo.TwoPi * i / 20), c.Center.Y + c.Radius * Math.Sin(Geo.TwoPi * i / 20))).ToList();
            List<Point2> clipped = Geo.ClipToRect(circle, b);
            if (clipped.Count >= 3)
            {
                g.Polyline(Map(clipped), true);
            }
        }

        // Sol: seta da borda da moldura para o centro, do lado de onde vem a luz.
        var dir = new Point2(frame.ToSun.X, frame.ToSun.Y);
        if (dir.Length > 1e-6)
        {
            dir = dir.Normalized();
            Point2 center = new((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            double reach = Math.Min(b.Width, b.Height) * 0.46;
            Point2 from = P(center + dir * reach), to = P(center + dir * (reach * 0.72));
            var sun = Drawing.Ellipse(from, 0.0018, 0.0018, 12);
            g.Fills.Add(new DrawFill(sun, HourlyShade.SunColor));
            g.Polyline(sun, true);
            g.Line(from, to);
            Point2 back = (from - to).Normalized() * 0.0012, side = back.Perp() * 0.7;
            g.Line(to, to + back + side);
            g.Line(to, to + back - side);
        }

        // Norte verdadeiro.
        Point2 nOrigin = new(origin.X + b.Width * k - 0.004, origin.Y - 0.003);
        Point2 north = new Point2(0, 1).Rotate(trueNorthRad) * 0.0035;
        g.Line(nOrigin - north * 0.4, nOrigin + north);
        g.Text(nOrigin + north * 1.4, "N", 0, 0.6);

        double bottom = origin.Y - b.Height * k;
        g.Text(new Point2(origin.X, origin.Y + 0.0045), frame.HourText, -1, 1.05);
        g.Text(new Point2(origin.X + 0.012, origin.Y + 0.0045), $"{frame.ShadedFraction.ToString("0%", Pt)} da região à sombra", -1, 0.7);
        g.Text(new Point2(origin.X, bottom - 0.0035), HourlyShade.SunText(frame.Sun), -1, 0.68);
    }

    /// <summary>Tabela: cada espécie (ou planta) por horário, com sol ou sombra e o total de horas de sol.</summary>
    private static void Table(Drawing g, IReadOnlyList<ShadeFrame> frames, IReadOnlyList<ShadeRow> rows, Point2 at)
    {
        if (rows.Count == 0)
        {
            g.Text(at, "Não há plantas do catálogo na região.", -1, 0.85);
            return;
        }

        double width = NameColumnM + frames.Count * HourColumnM + HourColumnM * 1.4;
        g.Text(new Point2(at.X, at.Y + 0.003), "Sol nas plantas da região, hora a hora (média dos exemplares de cada espécie)", -1, 0.9);
        for (int f = 0; f < frames.Count; f++)
        {
            g.Text(new Point2(at.X + NameColumnM + (f + 0.5) * HourColumnM, at.Y - RowM * 0.35), frames[f].ClockHour.ToString("00", Pt) + "h", 0, 0.72);
        }

        g.Text(new Point2(at.X + NameColumnM + (frames.Count + 0.7) * HourColumnM, at.Y - RowM * 0.35), "h de sol", 0, 0.7);
        double y = at.Y - RowM;
        foreach (ShadeRow row in rows)
        {
            double sunHours = 0;
            for (int f = 0; f < frames.Count; f++)
            {
                double light = row.Plants.Count == 0 ? 0 : row.Plants.Average(i => frames[f].Plants[i]);
                sunHours += light;
                int cls = HourlyShade.ShadeClass(light);
                Rgb color = cls == 0 ? HourlyShade.ShadeColor : cls == 1 ? HourlyShade.PartialColor : HourlyShade.SunColor;
                double x = at.X + NameColumnM + f * HourColumnM;
                g.Fills.Add(new DrawFill(new List<Point2>
                {
                    new(x + 0.0005, y - 0.0005), new(x + HourColumnM - 0.0005, y - 0.0005),
                    new(x + HourColumnM - 0.0005, y - RowM + 0.0005), new(x + 0.0005, y - RowM + 0.0005),
                }, color));
            }

            string label = row.Label.Length > 44 ? row.Label[..43] + "…" : row.Label;
            g.Text(new Point2(at.X + 0.0015, y - RowM * 0.3), label, -1, 0.78);
            g.Text(new Point2(at.X + NameColumnM + (frames.Count + 0.7) * HourColumnM, y - RowM * 0.3), sunHours.ToString("0.#", Pt), 0, 0.78);
            y -= RowM;
        }

        for (int i = 0; i <= rows.Count + 1; i++)
        {
            double yy = at.Y - i * RowM;
            g.Line(new Point2(at.X, yy), new Point2(at.X + width, yy), heavy: i <= 1);
        }

        double bottom = at.Y - (rows.Count + 1) * RowM;
        g.Line(new Point2(at.X, at.Y), new Point2(at.X, bottom), heavy: true);
        for (int f = 0; f <= frames.Count; f++)
        {
            double x = at.X + NameColumnM + f * HourColumnM;
            g.Line(new Point2(x, at.Y), new Point2(x, bottom), heavy: f == 0 || f == frames.Count);
        }

        g.Line(new Point2(at.X + width, at.Y), new Point2(at.X + width, bottom), heavy: true);
    }
}
