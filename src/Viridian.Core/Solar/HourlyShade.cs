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

    /// <summary>
    /// Faixas prontas para virar UMA região preenchida do Revit por classe: o Revit recusa a região inteira quando dois
    /// contornos se tocam, e faixas de linhas vizinhas da malha encostam umas nas outras. Faixas da mesma classe
    /// empilhadas com a mesma largura viram um retângulo só, e todas são recuadas por <paramref name="gap"/> de cada
    /// lado, de modo que nenhum contorno da mesma classe toque outro. Faixas menores que o recuo são descartadas.
    /// </summary>
    public static List<(int Class, Bounds2 Box)> RegionStrips(IEnumerable<(int Class, Bounds2 Box)> runs, double gap)
    {
        ArgumentNullException.ThrowIfNull(runs);
        gap = Math.Max(0, gap);
        var result = new List<(int, Bounds2)>();
        foreach (IGrouping<int, (int Class, Bounds2 Box)> group in runs.GroupBy(r => r.Class))
        {
            double eps = 1e-7;
            List<Bounds2> boxes = group.Select(r => r.Box)
                .OrderBy(b => Math.Round(b.MinX / 1e-6)).ThenBy(b => Math.Round(b.MaxX / 1e-6)).ThenBy(b => b.MinY).ToList();
            var merged = new List<Bounds2>();
            foreach (Bounds2 box in boxes)
            {
                if (merged.Count > 0)
                {
                    Bounds2 last = merged[^1];
                    double tol = Math.Max(eps, (box.MaxY - box.MinY) * 1e-6);
                    if (Math.Abs(last.MinX - box.MinX) <= tol && Math.Abs(last.MaxX - box.MaxX) <= tol && Math.Abs(last.MaxY - box.MinY) <= tol)
                    {
                        merged[^1] = new Bounds2(last.MinX, last.MinY, last.MaxX, box.MaxY);
                        continue;
                    }
                }

                merged.Add(box);
            }

            foreach (Bounds2 b in merged)
            {
                if (b.MaxX - b.MinX > 2 * gap + eps && b.MaxY - b.MinY > 2 * gap + eps)
                {
                    result.Add((group.Key, new Bounds2(b.MinX + gap, b.MinY + gap, b.MaxX - gap, b.MaxY - gap)));
                }
            }
        }

        return result;
    }

    /// <summary>Classe de sombra: 0 = sombra (luz abaixo de 35%), 1 = meia-luz (35% a 75%), -1 = sol.</summary>
    public static int ShadeClass(double light) => light < 0.35 ? 0 : light < 0.75 ? 1 : -1;

    public static readonly Rgb ShadeColor = new(0x5A, 0x67, 0x80);

    public static readonly Rgb PartialColor = new(0xA9, 0xB6, 0xC6);

    public static readonly Rgb SunColor = new(0xF5, 0xD0, 0x5C);
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
        g.Text(new Point2(0, 0.026), title.ToUpperInvariant(), -1, 1.45);
        g.Line(new Point2(0, 0.0215), new Point2(4 * PanelWidthM + 3 * GapM, 0.0215), heavy: true);
        g.Text(new Point2(0, 0.017), $"{date.Name}, dia {date.Day} · {site.Describe()}", -1, 0.85);
        g.Text(new Point2(0, 0.011), $"Hora legal, sem horário de verão. Sombras das copas ({stageText})"
                                     + (buildings ? ", das edificações e do terreno." : ".") + " Decíduas mais transparentes nos meses sem folhas.", -1, 0.8);
        Legend(g, new Point2(0, 0.0055));

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

    /// <summary>Chão da região analisada e copas vistas de cima, nos quadros.</summary>
    public static readonly Rgb GroundColor = new(0xEE, 0xF2, 0xE6);

    public static readonly Rgb CrownColor = new(0x9C, 0xC2, 0x7E);

    private static readonly Rgb Ink = new(0x2B, 0x33, 0x40);

    private static void Legend(Drawing g, Point2 at)
    {
        (Rgb Color, string Text)[] items =
        {
            (GroundColor, "chão ao sol"),
            (HourlyShade.PartialColor, "meia-luz (copa rala ou borda)"),
            (HourlyShade.ShadeColor, "sombra"),
            (CrownColor, "copa (vista de cima)"),
            (HourlyShade.SunColor, "sol: de onde vem a luz"),
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
    }

    private static void Panel(Drawing g, ShadeFrame frame, IReadOnlyList<Point2> grid, double cell, Polygon2 region,
        IReadOnlyList<CrownObstacle> crowns, Bounds2 b, double k, Point2 origin, double trueNorthRad)
    {
        Point2 P(Point2 p) => new(origin.X + (p.X - b.MinX) * k, origin.Y - (b.MaxY - p.Y) * k);
        List<Point2> Map(IEnumerable<Point2> pts) => pts.Select(P).ToList();

        // Chão da região (cor clara), sombra na malha por cima e as copas vistas de cima (acima das sombras no chão).
        foreach (IReadOnlyList<Point2> ring in region.Rings.Where(r => Geo.SignedArea(r) > 0))
        {
            g.Fills.Add(new DrawFill(Map(ring), GroundColor));
        }

        foreach ((int cls, Bounds2 box) in HourlyShade.Runs(grid, cell, i => HourlyShade.ShadeClass(frame.Grid[i])))
        {
            var rect = new List<Point2> { new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY) };
            g.Fills.Add(new DrawFill(Map(rect), cls == 0 ? HourlyShade.ShadeColor : HourlyShade.PartialColor));
        }

        foreach (CrownObstacle c in crowns)
        {
            var circle = Enumerable.Range(0, 28).Select(i => new Point2(c.Center.X + c.Radius * Math.Cos(Geo.TwoPi * i / 28), c.Center.Y + c.Radius * Math.Sin(Geo.TwoPi * i / 28))).ToList();
            List<Point2> clipped = Geo.ClipToRect(circle, b);
            if (clipped.Count >= 3)
            {
                g.Fills.Add(new DrawFill(Map(clipped), CrownColor));
                g.Polyline(Map(clipped), true);
            }
        }

        // Contorno da região (forte) e moldura do quadro.
        foreach (IReadOnlyList<Point2> ring in region.Rings)
        {
            g.Polyline(Map(ring), true, heavy: true);
        }

        var frameBox = new List<Point2> { new(b.MinX, b.MinY), new(b.MaxX, b.MinY), new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY) };
        g.Polyline(Map(frameBox), true);

        // Sol: disco com raios junto à moldura, do lado de onde vem a luz, e seta apontando para o centro.
        var dir = new Point2(frame.ToSun.X, frame.ToSun.Y);
        if (dir.Length > 1e-6)
        {
            dir = dir.Normalized();
            Point2 center = new((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            double reach = Math.Min(b.Width, b.Height) * 0.44;
            Point2 from = P(center + dir * reach), to = P(center + dir * (reach * 0.66));
            const double r = 0.0021;
            var sun = Drawing.Ellipse(from, r, r, 16);
            g.Fills.Add(new DrawFill(sun, HourlyShade.SunColor));
            g.Polyline(sun, true);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                var u = new Point2(Math.Cos(a), Math.Sin(a));
                g.Line(from + u * (r * 1.35), from + u * (r * 1.9));
            }

            Point2 start = from + (to - from).Normalized() * (r * 2.1);
            g.Line(start, to, heavy: true);
            Point2 back = (start - to).Normalized() * 0.0013, side = back.Perp() * 0.65;
            g.Fills.Add(new DrawFill(new List<Point2> { to, to + back + side, to + back - side }, Ink));
        }

        // Norte verdadeiro: seta cheia com "N".
        Point2 nOrigin = new(origin.X + b.Width * k - 0.0045, origin.Y - 0.0055);
        Point2 up = new Point2(0, 1).Rotate(trueNorthRad), across = up.Perp();
        Point2 tip = nOrigin + up * 0.0032, tail = nOrigin - up * 0.0022;
        g.Fills.Add(new DrawFill(new List<Point2> { tip, tail + across * 0.0013, nOrigin - up * 0.0010, tail - across * 0.0013 }, Ink));
        g.Text(tip + up * 0.0016, "N", 0, 0.62);

        // Cabeçalho do quadro: hora e barra com a parte da região à sombra.
        double bottom = origin.Y - b.Height * k;
        g.Text(new Point2(origin.X, origin.Y + 0.0045), frame.HourText, -1, 1.15);
        double barX = origin.X + 0.0125, barW = 0.022, barY = origin.Y + 0.0052;
        double shaded = Math.Clamp(frame.ShadedFraction, 0, 1);
        g.Fills.Add(new DrawFill(Rect(barX, barY, barX + barW, barY - 0.0018), HourlyShade.SunColor));
        if (shaded > 0.005)
        {
            g.Fills.Add(new DrawFill(Rect(barX, barY, barX + barW * shaded, barY - 0.0018), HourlyShade.ShadeColor));
        }

        g.Polyline(Rect(barX, barY, barX + barW, barY - 0.0018), true);
        g.Text(new Point2(barX + barW + 0.0015, origin.Y + 0.0035), $"{frame.ShadedFraction.ToString("0%", Pt)} à sombra", -1, 0.7);
        g.Text(new Point2(origin.X, bottom - 0.0035), HourlyShade.SunText(frame.Sun), -1, 0.68);
    }

    private static List<Point2> Rect(double x0, double y0, double x1, double y1) =>
        new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };

    /// <summary>Tabela: cada espécie (ou planta) por horário, com sol ou sombra e o total de horas de sol.</summary>
    private static void Table(Drawing g, IReadOnlyList<ShadeFrame> frames, IReadOnlyList<ShadeRow> rows, Point2 at)
    {
        if (rows.Count == 0)
        {
            g.Text(at, "Não há plantas do catálogo na região.", -1, 0.85);
            return;
        }

        double width = NameColumnM + frames.Count * HourColumnM + HourColumnM * 1.4;
        g.Text(new Point2(at.X, at.Y + 0.003), "SOL NAS PLANTAS DA REGIÃO, HORA A HORA (média dos exemplares de cada espécie)", -1, 0.95);
        for (int f = 0; f < frames.Count; f++)
        {
            g.Text(new Point2(at.X + NameColumnM + (f + 0.5) * HourColumnM, at.Y - RowM * 0.35), frames[f].ClockHour.ToString("00", Pt) + "h", 0, 0.72);
        }

        g.Text(new Point2(at.X + NameColumnM + (frames.Count + 0.7) * HourColumnM, at.Y - RowM * 0.35), "h de sol", 0, 0.7);
        double y = at.Y - RowM;
        double maxHours = Math.Max(1, frames.Count);
        int index = 0;
        foreach (ShadeRow row in rows)
        {
            if (index++ % 2 == 1)
            {
                g.Fills.Add(new DrawFill(Rect(at.X, y, at.X + NameColumnM, y - RowM), new Rgb(0xF1, 0xF3, 0xEE)));
            }

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
            // Horas de sol em barra (proporcional ao intervalo analisado) e número.
            double hx = at.X + NameColumnM + frames.Count * HourColumnM + 0.0008, hw = HourColumnM * 1.4 - 0.0016;
            g.Fills.Add(new DrawFill(Rect(hx, y - RowM * 0.62, hx + hw * Math.Clamp(sunHours / maxHours, 0, 1), y - RowM * 0.92), HourlyShade.SunColor));
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
