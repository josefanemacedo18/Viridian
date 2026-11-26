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

    /// <summary>
    /// Manchas de luz contornadas (sombra realista: edificações projetadas, copas e troncos), do maior para o menor.
    /// Vazio quando o cálculo foi feito só na malha de pontos.
    /// </summary>
    public List<ShadeRing> Rings { get; } = new();

    /// <summary>Parte do chão à sombra calculada no campo fino (NaN quando não há campo).</summary>
    public double FieldShadedFraction { get; set; } = double.NaN;

    /// <summary>Parte do chão à sombra (luz abaixo de 50%): pelo campo fino ou, sem ele, pela malha.</summary>
    public double ShadedFraction => double.IsFinite(FieldShadedFraction) ? FieldShadedFraction
        : Grid.Length == 0 ? 0 : Grid.Count(l => l < 0.5) / (double)Grid.Length;

    public string HourText => $"{ClockHour:00}:00";
}

/// <summary>
/// Dados para as sombras realistas: a região, o plano do jardim (cota do chão, m), a geometria das edificações, a
/// resolução da malha fina e até que distância (m) as edificações são consideradas.
/// </summary>
public sealed record ShadeGeometry(Polygon2 Region, double PlaneZ, ObstacleMesh? Mesh, double Resolution, double Reach = 80);

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
        CancellationToken cancel = default,
        ShadeGeometry? geometry = null)
    {
        var frames = new List<ShadeFrame>();
        IReadOnlyList<List<Vec3>>? casters = geometry?.Mesh is { IsEmpty: false } mesh
            ? mesh.Above(geometry.PlaneZ + ShadowField.MinCasterHeightM, geometry.Region.Bounds.Expand(geometry.Reach))
            : null;
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

            if (geometry is not null)
            {
                LightField field = ShadowField.Compute(geometry.Region, geometry.Resolution, geometry.PlaneZ, toSun, date.Month, crowns, casters);
                frame.Rings.AddRange(ShadowField.Rings(field));
                frame.FieldShadedFraction = field.ShadedFraction();
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
    private const double PanelWidthM = 0.086;
    private const double MaxPanelHeightM = 0.092;
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
        string stageText,
        string northText = "",
        string geometryText = "")
    {
        var g = new Drawing { Title = title };
        int cols = frames.Count is > 12 ? 4 : 3;
        double width = cols * PanelWidthM + (cols - 1) * GapM;

        // Faixa de título e informações do estudo.
        g.Box(0, 0.024, width, 0.034, DrawingStyle.Primary);
        g.Text(new Point2(0.003, 0.029), title.ToUpperInvariant(), TextStyle.Claro, -1, 1.3);
        g.Text(new Point2(width - 0.003, 0.029), $"{date.Name}, dia {date.Day}", TextStyle.Claro, 1, 0.95);
        g.Text(new Point2(0, 0.0195), site.Describe(), TextStyle.Subtitulo, -1, 0.85);
        string north = northText.Length > 0 ? northText : Math.Abs(trueNorthRad) < 1e-4 ? "Norte verdadeiro para o topo da planta" : "Norte verdadeiro conforme a seta";
        g.Text(new Point2(0, 0.0150), $"Hora legal, sem horário de verão. {north}.", TextStyle.Nota, -1, 0.78);
        g.Text(new Point2(0, 0.0110), $"Sombras das copas ({stageText}): luz filtrada pela espessura de folhagem atravessada, troncos opacos"
                                      + (buildings ? "; edificações, muros e coberturas pela geometria real do modelo" + (geometryText.Length > 0 ? " (" + geometryText + ")" : string.Empty) : string.Empty)
                                      + ". Decíduas mais transparentes nos meses sem folhas.", TextStyle.Nota, -1, 0.78);
        Legend(g, new Point2(0, 0.0060));

        if (frames.Count == 0)
        {
            g.Text(new Point2(0, -0.01), "O sol não fica acima do horizonte no intervalo de horas escolhido.", -1, 0.9);
            return g;
        }

        Bounds2 b = region.Bounds.Expand(Math.Max(1, Math.Max(region.Bounds.Width, region.Bounds.Height) * 0.08));
        double k = PanelWidthM / Math.Max(1e-6, b.Width);
        if (b.Height * k > MaxPanelHeightM)
        {
            k = MaxPanelHeightM / b.Height;
        }

        double ph = b.Height * k;
        double top = -0.004;
        for (int f = 0; f < frames.Count; f++)
        {
            double x0 = f % cols * (PanelWidthM + GapM);
            double y0 = top - f / cols * (ph + LabelM * 1.6 + GapM) - LabelM;
            Panel(g, frames[f], grid, cell, region, crowns, b, k, new Point2(x0, y0), trueNorthRad);
        }

        int panelRows = (frames.Count + cols - 1) / cols;
        double tableTop = top - panelRows * (ph + LabelM * 1.6 + GapM) - 0.004;
        Table(g, frames, rows, new Point2(0, tableTop));
        return g;
    }

    /// <summary>Chão da região analisada ao sol, nos quadros.</summary>
    public static readonly Rgb GroundColor = new(0xF4, 0xF1, 0xE2);

    /// <summary>Contorno das copas vistas de cima.</summary>
    public static readonly Rgb CrownColor = new(0x5E, 0x82, 0x4E);

    /// <summary>Edificações, muros e coberturas vistos de cima.</summary>
    public static readonly Rgb BuildingColor = new(0x9A, 0x9E, 0xA4);

    private static readonly Rgb Ink = new(0x2B, 0x33, 0x40);

    private static Rgb ColorOf(ShadeClass c) => c switch
    {
        ShadeClass.Edificacao => BuildingColor,
        ShadeClass.Sombra => HourlyShade.ShadeColor,
        ShadeClass.MeiaLuz => HourlyShade.PartialColor,
        _ => GroundColor,
    };

    private static void Legend(Drawing g, Point2 at)
    {
        (Rgb? Color, string Text)[] items =
        {
            (GroundColor, "chão ao sol"),
            (HourlyShade.PartialColor, "meia-luz (borda da copa, copa rala)"),
            (HourlyShade.ShadeColor, "sombra"),
            (BuildingColor, "edificação, muro, cobertura"),
            (null, "copa e tronco (vistos de cima)"),
            (HourlyShade.SunColor, "sol: de onde vem a luz"),
        };
        double x = at.X;
        foreach ((Rgb? color, string text) in items)
        {
            var box = new List<Point2> { new(x, at.Y), new(x + 0.005, at.Y), new(x + 0.005, at.Y - 0.003), new(x, at.Y - 0.003) };
            if (color is Rgb fill)
            {
                g.Fills.Add(new DrawFill(box, fill));
                g.Strokes(box, true, LinePen.Fina);
            }
            else
            {
                g.Strokes(Drawing.Ellipse(new Point2(x + 0.0025, at.Y - 0.0015), 0.0022, 0.0014, 20), true, LinePen.Media);
                g.Fills.Add(new DrawFill(Drawing.Ellipse(new Point2(x + 0.0025, at.Y - 0.0015), 0.0004, 0.0004, 10), CrownColor));
            }

            g.Text(new Point2(x + 0.0065, at.Y - 0.0016), text, TextStyle.Normal, -1, 0.75);
            x += 0.011 + 0.00145 * text.Length;
        }
    }

    /// <summary>Copa vista de cima: contorno levemente ondulado, como no desenho à mão.</summary>
    private static List<Point2> CrownRing(Point2 center, double radius, string id)
    {
        int seed = 17;
        foreach (char ch in id)
        {
            seed = seed * 31 + ch;
        }

        double phase = (seed & 1023) / 1023.0 * Geo.TwoPi;
        int lobes = 7 + (seed & 3);
        var points = new List<Point2>(40);
        for (int i = 0; i < 40; i++)
        {
            double a = Geo.TwoPi * i / 40;
            double r = radius * (1 + 0.045 * Math.Sin(lobes * a + phase));
            points.Add(new Point2(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a)));
        }

        return points;
    }

    private static void Panel(Drawing g, ShadeFrame frame, IReadOnlyList<Point2> grid, double cell, Polygon2 region,
        IReadOnlyList<CrownObstacle> crowns, Bounds2 b, double k, Point2 origin, double trueNorthRad)
    {
        Point2 P(Point2 p) => new(origin.X + (p.X - b.MinX) * k, origin.Y - (b.MaxY - p.Y) * k);
        List<Point2> Map(IEnumerable<Point2> pts) => pts.Select(P).ToList();

        // Chão da região ao sol e, por cima, as manchas de luz do maior para o menor (cada uma com a cor de dentro).
        foreach (IReadOnlyList<Point2> ring in region.Rings.Where(r => Geo.SignedArea(r) > 0))
        {
            g.Fills.Add(new DrawFill(Map(ring), GroundColor));
        }

        if (frame.Rings.Count > 0)
        {
            foreach (ShadeRing ring in frame.Rings)
            {
                g.Fills.Add(new DrawFill(Map(ring.Points), ColorOf(ring.Fill)));
            }
        }
        else
        {
            foreach ((int cls, Bounds2 box) in HourlyShade.Runs(grid, cell, i => HourlyShade.ShadeClass(frame.Grid[i])))
            {
                var rect = new List<Point2> { new(box.MinX, box.MinY), new(box.MaxX, box.MinY), new(box.MaxX, box.MaxY), new(box.MinX, box.MaxY) };
                g.Fills.Add(new DrawFill(Map(rect), cls == 0 ? HourlyShade.ShadeColor : HourlyShade.PartialColor));
            }
        }

        // Copas vistas de cima: só o contorno (a sombra no chão continua visível) e o tronco.
        foreach (CrownObstacle c in crowns)
        {
            var center = new Point2(c.Center.X, c.Center.Y);
            if (!b.Contains(center))
            {
                continue;
            }

            List<Point2> outline = Geo.ClipToRect(CrownRing(center, c.Radius, c.Id), b);
            if (outline.Count >= 3)
            {
                g.Strokes(Map(outline), true, LinePen.Media);
            }

            double trunk = Math.Max(0.00045, Math.Max(c.TrunkRadius, 0.06) * k);
            g.Fills.Add(new DrawFill(Drawing.Ellipse(P(center), trunk, trunk, 12), CrownColor));
        }

        // Contorno da região (forte) e moldura do quadro.
        foreach (IReadOnlyList<Point2> ring in region.Rings)
        {
            g.Strokes(Map(ring), true, LinePen.Grossa);
        }

        var frameBox = new List<Point2> { new(b.MinX, b.MinY), new(b.MaxX, b.MinY), new(b.MaxX, b.MaxY), new(b.MinX, b.MaxY) };
        g.Strokes(Map(frameBox), true, LinePen.Fina);

        // Sol: disco com raios junto à moldura, do lado de onde vem a luz, e seta apontando para o centro.
        var dir = new Point2(frame.ToSun.X, frame.ToSun.Y);
        if (dir.Length > 1e-6)
        {
            dir = dir.Normalized();
            Point2 center = new((b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            // Até a moldura, na direção do sol.
            double tx = Math.Abs(dir.X) < 1e-9 ? double.MaxValue : (b.Width / 2) / Math.Abs(dir.X);
            double ty = Math.Abs(dir.Y) < 1e-9 ? double.MaxValue : (b.Height / 2) / Math.Abs(dir.Y);
            double reach = Math.Min(tx, ty) * 0.86;
            Point2 from = P(center + dir * reach), to = P(center + dir * (reach * 0.62));
            const double r = 0.0024;
            var sun = Drawing.Ellipse(from, r, r, 18);
            g.Fills.Add(new DrawFill(sun, HourlyShade.SunColor));
            g.Strokes(sun, true, LinePen.Fina);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                var u = new Point2(Math.Cos(a), Math.Sin(a));
                g.Stroke(from + u * (r * 1.35), from + u * (r * 1.9), LinePen.Fina);
            }

            if (from.DistanceTo(to) > r * 2.6)
            {
                g.Arrow(from + (to - from).Normalized() * (r * 2.1), to, LinePen.Media, 0.0016, Ink);
            }
        }

        // Norte verdadeiro: círculo, seta cheia girada e "N".
        Point2 nOrigin = new(origin.X + b.Width * k - 0.0062, origin.Y - 0.0085);
        Point2 up = new Point2(0, 1).Rotate(trueNorthRad), across = up.Perp();
        g.Fills.Add(new DrawFill(Drawing.Ellipse(nOrigin, 0.0038, 0.0038, 28), new Rgb(0xFF, 0xFF, 0xFF)));
        g.Strokes(Drawing.Ellipse(nOrigin, 0.0038, 0.0038, 28), true, LinePen.Fina);
        Point2 tip = nOrigin + up * 0.0033, tail = nOrigin - up * 0.0024;
        g.Fills.Add(new DrawFill(new List<Point2> { tip, tail + across * 0.0015, nOrigin - up * 0.0011, tail - across * 0.0015 }, Ink));
        g.Text(nOrigin + up * 0.0050 + across * 0.0001, "N", TextStyle.Titulo, 0, 0.62);

        // Escala gráfica do quadro (0 – n m).
        double meters = ScaleMeters(b.Width);
        double bx = origin.X + 0.002, by = origin.Y - b.Height * k + 0.0028;
        for (int i = 0; i < 2; i++)
        {
            double xa = bx + i * meters / 2 * k, xb = bx + (i + 1) * meters / 2 * k;
            g.Fills.Add(new DrawFill(Rect(xa, by, xb, by - 0.0010), i == 0 ? Ink : new Rgb(0xFF, 0xFF, 0xFF)));
            g.Strokes(Rect(xa, by, xb, by - 0.0010), true, LinePen.Fina);
        }

        g.Text(new Point2(bx + meters * k + 0.001, by - 0.0005), meters.ToString("0.#", Pt) + " m", TextStyle.Nota, -1, 0.6);

        // Cabeçalho do quadro: hora, barra da parte do chão à sombra e posição do sol.
        double bottom = origin.Y - b.Height * k;
        g.Text(new Point2(origin.X, origin.Y + 0.0046), frame.HourText, TextStyle.Titulo, -1, 1.2);
        double barX = origin.X + 0.0145, barW = 0.026, barY = origin.Y + 0.0054;
        double shaded = Math.Clamp(frame.ShadedFraction, 0, 1);
        g.Fills.Add(new DrawFill(Rect(barX, barY, barX + barW, barY - 0.0019), HourlyShade.SunColor));
        if (shaded > 0.005)
        {
            g.Fills.Add(new DrawFill(Rect(barX, barY, barX + barW * shaded, barY - 0.0019), HourlyShade.ShadeColor));
        }

        g.Strokes(Rect(barX, barY, barX + barW, barY - 0.0019), true, LinePen.Fina);
        g.Text(new Point2(barX + barW + 0.0015, origin.Y + 0.0045), $"{frame.ShadedFraction.ToString("0%", Pt)} do chão à sombra", TextStyle.Normal, -1, 0.7);
        g.Text(new Point2(origin.X, bottom - 0.0037), HourlyShade.SunText(frame.Sun), TextStyle.Nota, -1, 0.7);
    }

    /// <summary>Comprimento redondo da escala gráfica: cerca de um quinto da largura do quadro.</summary>
    private static double ScaleMeters(double widthM)
    {
        double target = widthM / 5;
        foreach (double v in new[] { 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100 })
        {
            if (v >= target)
            {
                return v;
            }
        }

        return 200;
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
