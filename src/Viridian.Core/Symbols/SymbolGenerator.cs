using Viridian.Core.Geometry;

namespace Viridian.Core.Symbols;

/// <summary>
/// Gerador procedural dos símbolos 2D de vegetação em planta.
/// Cada símbolo é desenhado em dimensão real (metros), centrado no ponto de inserção, e
/// organizado em camadas para que o detalhamento acompanhe a escala da vista.
/// </summary>
public static class SymbolGenerator
{
    /// <summary>Versão do algoritmo. Alterá-la invalida o cache de famílias geradas.</summary>
    public const int Version = 2;

    public const double MinDiameter = 0.10;
    public const double MaxDiameter = 60.0;

    private const SymbolTier Minimal = SymbolTier.MinimalOutline;
    private const SymbolTier Center = SymbolTier.Center;
    private const SymbolTier Outline = SymbolTier.Outline;
    private const SymbolTier Essential = SymbolTier.Essential;
    private const SymbolTier Principal = SymbolTier.Principal;
    private const SymbolTier Detail = SymbolTier.Detail;

    /// <summary>Gera o símbolo de um estilo para o diâmetro de copa informado.</summary>
    /// <param name="style">Estilo gráfico.</param>
    /// <param name="diameter">Diâmetro real da copa em metros.</param>
    /// <param name="seed">Semente da espécie: mantém o desenho estável entre projetos.</param>
    /// <param name="variant">Índice da variante gráfica (0, 1, 2...).</param>
    public static PlantSymbol Generate(SymbolStyle style, double diameter, int seed, int variant = 0)
    {
        double d = double.IsFinite(diameter) ? Math.Clamp(diameter, MinDiameter, MaxDiameter) : 1.0;
        double r = d / 2;
        var rng = new DeterministicRandom(DeterministicRandom.Combine(seed, variant, (int)style, 7919));
        var b = new SymbolBuilder(Math.Max(0.004, r * 0.004));
        int level = r < 0.20 ? 0 : r < 0.75 ? 1 : 2;

        b.Circle(Minimal, Point2.Zero, r);

        switch (style)
        {
            case SymbolStyle.ArvoreArredondada:
                RoundedTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvoreEspalhada:
                SpreadingTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvoreColunar:
                ColumnarTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvorePiramidal:
                PyramidalTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvoreIrregular:
                IrregularTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvoreRadial:
                RadialTree(b, rng, r, level);
                break;
            case SymbolStyle.ArvoreFolhagem:
                FoliageTree(b, rng, r, level);
                break;
            case SymbolStyle.Palmeira:
                PinnatePalm(b, rng, r, level);
                break;
            case SymbolStyle.PalmeiraLeque:
                FanPalm(b, rng, r, level);
                break;
            case SymbolStyle.ArbustoNuvem:
                CloudShrub(b, rng, r, level);
                break;
            case SymbolStyle.ArbustoPontiagudo:
                SpikyShrub(b, rng, r, level);
                break;
            case SymbolStyle.Herbacea:
                Herbaceous(b, rng, r, level);
                break;
            case SymbolStyle.Forracao:
                Groundcover(b, rng, r, level);
                break;
            case SymbolStyle.Gramado:
                Grass(b, rng, r, level);
                break;
            case SymbolStyle.Trepadeira:
                Climber(b, rng, r, level);
                break;
            case SymbolStyle.Aquatica:
                Aquatic(b, rng, r, level);
                break;
            default:
                b.Circle(Outline, Point2.Zero, r);
                CrossMark(b, r);
                break;
        }

        IReadOnlyList<Point2> fill = BuildFill(style, b.Items, r);
        var flowerRng = new DeterministicRandom(DeterministicRandom.Combine(seed, variant, 104729));
        IReadOnlyList<FlowerSpot> flowers = BuildFlowers(style, fill, r, flowerRng);
        return new PlantSymbol(style, d, b.Items, fill, flowers);
    }

    // ------------------------------------------------------------------ preenchimento e floração

    /// <summary>
    /// Contorno da área colorida: amostragem radial do contorno da copa. Como todos os contornos são
    /// radiais em relação ao centro, o anel resultante nunca cruza a si mesmo.
    /// </summary>
    private static IReadOnlyList<Point2> BuildFill(SymbolStyle style, IReadOnlyList<SymbolPrimitive> items, double r)
    {
        int count = Math.Clamp((int)Math.Ceiling(Geo.TwoPi * r / 0.012), 36, 240);
        var ring = new List<Point2>(count);
        double minSpacing = Math.Max(0.003, r * 0.004);

        if (style is SymbolStyle.Herbacea or SymbolStyle.Gramado or SymbolStyle.Simples)
        {
            for (int k = 0; k < count; k++)
            {
                ring.Add(Point2.Polar(r * 0.94, Geo.TwoPi * k / count));
            }

            return ring;
        }

        var segments = new List<(Point2 A, Point2 B)>();
        foreach (SymbolPrimitive primitive in items)
        {
            if (primitive.Tier != SymbolTier.Outline)
            {
                continue;
            }

            switch (primitive)
            {
                case SymbolLine line:
                    segments.Add((line.A, line.B));
                    break;
                case SymbolArc arc when ArcInfo.TryFromThreePoints(arc.Start, arc.Mid, arc.End, out ArcInfo info):
                {
                    const int parts = 10;
                    Point2 previous = info.PointAt(0);
                    for (int i = 1; i <= parts; i++)
                    {
                        Point2 next = info.PointAt(i / (double)parts);
                        segments.Add((previous, next));
                        previous = next;
                    }

                    break;
                }

                case SymbolArc arc:
                    segments.Add((arc.Start, arc.End));
                    break;
            }
        }

        double floor = r * 0.10;
        for (int k = 0; k < count; k++)
        {
            double angle = Geo.TwoPi * k / count;
            var dir = new Point2(Math.Cos(angle), Math.Sin(angle));
            double best = 0;
            foreach ((Point2 a, Point2 b) in segments)
            {
                if (RaySegment(dir, a, b, out double t) && t > best)
                {
                    best = t;
                }
            }

            double radius = Math.Clamp(best > 0 ? best * 0.995 : floor, floor, r * 0.995);
            Point2 p = Point2.Polar(radius, angle);
            if (ring.Count == 0 || ring[^1].DistanceTo(p) >= minSpacing)
            {
                ring.Add(p);
            }
        }

        while (ring.Count > 3 && ring[0].DistanceTo(ring[^1]) < minSpacing)
        {
            ring.RemoveAt(ring.Count - 1);
        }

        return ring;
    }

    /// <summary>Interseção do raio que parte da origem com um segmento; t é a distância ao longo do raio.</summary>
    private static bool RaySegment(Point2 dir, Point2 a, Point2 b, out double t)
    {
        t = 0;
        Point2 e = b - a;
        double denominator = dir.Cross(e);
        if (Math.Abs(denominator) < 1e-12)
        {
            return false;
        }

        t = a.Cross(e) / denominator;
        double u = a.Cross(dir) / denominator;
        return t > 0 && u >= -1e-9 && u <= 1 + 1e-9;
    }

    /// <summary>Manchas de floração distribuídas na copa, sem sobreposição.</summary>
    private static IReadOnlyList<FlowerSpot> BuildFlowers(SymbolStyle style, IReadOnlyList<Point2> fill, double r, DeterministicRandom rng)
    {
        if (fill.Count < 3)
        {
            return Array.Empty<FlowerSpot>();
        }

        bool tree = style is >= SymbolStyle.ArvoreArredondada and <= SymbolStyle.ArvoreFolhagem;
        int wanted = tree ? Math.Clamp((int)Math.Round(r * 2.4), 5, 18) : Math.Clamp((int)Math.Round(r * 9), 3, 9);
        double spotRadius = tree ? Math.Clamp(r * 0.075, 0.03, 0.55) : Math.Clamp(r * 0.13, 0.012, 0.25);
        if (spotRadius < 0.012)
        {
            return Array.Empty<FlowerSpot>();
        }

        var polygon = new Polygon2(fill);
        var spots = new List<FlowerSpot>();
        for (int attempt = 0; attempt < wanted * 30 && spots.Count < wanted; attempt++)
        {
            Point2 c = Point2.Polar(r * Math.Sqrt(rng.Range(0.05, 0.75)), rng.Range(0, Geo.TwoPi));
            double radius = Math.Max(0.012, spotRadius * rng.Range(0.8, 1.15));
            if (!polygon.Contains(c) || polygon.DistanceToBoundary(c) < radius * 1.25 || c.Length + radius > r * 0.98)
            {
                continue;
            }

            if (spots.Any(s => s.Center.DistanceTo(c) < (s.Radius + radius) * 1.35))
            {
                continue;
            }

            spots.Add(new FlowerSpot(c, radius));
        }

        return spots;
    }

    // ------------------------------------------------------------------ marcas de centro

    private static double TrunkRadius(double r) => Math.Clamp(r * 0.045, 0.025, 0.22);

    private static double TrunkMark(SymbolBuilder b, double r)
    {
        double t = TrunkRadius(r);
        if (t > r * 0.3)
        {
            CrossMark(b, r);
            return Math.Min(t, r * 0.1);
        }

        b.Circle(Center, Point2.Zero, t);
        return t;
    }

    private static void CrossMark(SymbolBuilder b, double r)
    {
        double a = Math.Clamp(r * 0.09, 0.012, 0.12);
        b.Line(Center, new Point2(-a, 0), new Point2(a, 0));
        b.Line(Center, new Point2(0, -a), new Point2(0, a));
    }

    // ------------------------------------------------------------------ utilitários de desenho

    /// <summary>Contorno em gomos (arcos convexos), o traço clássico de copa em desenho de paisagismo.</summary>
    private static void Scallops(
        SymbolBuilder b,
        SymbolTier tier,
        DeterministicRandom rng,
        double r,
        int lobes,
        double baseFrac,
        double angleJitter,
        double radiusJitter,
        Func<double, double>? radiusScale = null,
        bool irregularWidths = false,
        double coverage = 1.0)
    {
        lobes = Math.Max(3, lobes);
        double phase = rng.Range(0, Geo.TwoPi);
        var bounds = new double[lobes + 1];

        if (irregularWidths)
        {
            var weights = new double[lobes];
            double total = 0;
            for (int i = 0; i < lobes; i++)
            {
                weights[i] = rng.Range(0.6, 1.6);
                total += weights[i];
            }

            double acc = 0;
            for (int i = 0; i < lobes; i++)
            {
                bounds[i] = phase + Geo.TwoPi * acc / total;
                acc += weights[i];
            }
        }
        else
        {
            double step = Geo.TwoPi / lobes;
            for (int i = 0; i < lobes; i++)
            {
                bounds[i] = phase + i * step + rng.Range(-angleJitter, angleJitter) * step;
            }
        }

        bounds[lobes] = bounds[0] + Geo.TwoPi;

        Func<double, double> scale = radiusScale ?? (_ => 1.0);
        var baseRadius = new double[lobes + 1];
        for (int i = 0; i < lobes; i++)
        {
            baseRadius[i] = r * (baseFrac + rng.Range(-radiusJitter, radiusJitter)) * scale(bounds[i]);
        }

        baseRadius[lobes] = baseRadius[0];

        for (int i = 0; i < lobes; i++)
        {
            double a0 = bounds[i];
            double a1 = bounds[i + 1];
            double r0 = baseRadius[i];
            double r1 = baseRadius[i + 1];
            if (coverage < 1.0)
            {
                // Gomos abertos: cada arco ocupa só parte do setor, deixando respiros.
                double gap = (a1 - a0) * (1 - coverage) / 2;
                a0 += gap;
                a1 -= gap;
            }

            double am = (a0 + a1) / 2;
            double rm = r * (1.0 - rng.Range(0, radiusJitter)) * scale(am);
            if (coverage < 1.0)
            {
                rm = Math.Max(rm, Math.Max(r0, r1) * 1.04);
            }

            b.Arc(tier, Point2.Polar(r0, a0), Point2.Polar(rm, am), Point2.Polar(r1, a1));
        }
    }

    /// <summary>Galho curvo que parte de um ponto em certa direção. Retorna a curva para encadear ramificações.</summary>
    private static Curve3 Branch(SymbolBuilder b, SymbolTier tier, Point2 from, double angle, double length, double bend, double maxRadius)
    {
        Point2 to = ClampInside(from, from + Point2.Polar(length, angle), maxRadius);
        Point2 dir = (to - from).Normalized();
        double actual = from.DistanceTo(to);
        Point2 mid = Point2.Lerp(from, to, 0.5) + dir.Perp() * (bend * actual);
        mid = ClampInside(from, mid, maxRadius);
        b.Arc(tier, from, mid, to);
        return new Curve3(from, mid, to);
    }

    /// <summary>Encurta o segmento from→to para que o ponto final não ultrapasse o raio máximo.</summary>
    private static Point2 ClampInside(Point2 from, Point2 to, double maxRadius)
    {
        if (to.Length <= maxRadius)
        {
            return to;
        }

        if (from.Length >= maxRadius)
        {
            return from.Normalized() * maxRadius;
        }

        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            double t = (lo + hi) / 2;
            if (Point2.Lerp(from, to, t).Length <= maxRadius)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }
        }

        return Point2.Lerp(from, to, lo);
    }

    /// <summary>Ângulos distribuídos ao redor do círculo com variação controlada.</summary>
    private static double[] SpreadAngles(DeterministicRandom rng, int count, double jitter)
    {
        var result = new double[count];
        double step = Geo.TwoPi / count;
        double phase = rng.Range(0, Geo.TwoPi);
        for (int i = 0; i < count; i++)
        {
            result[i] = phase + i * step + rng.Range(-jitter, jitter) * step;
        }

        return result;
    }

    /// <summary>Pequeno arco em "c", usado como textura de folhagem.</summary>
    private static void LeafCurl(SymbolBuilder b, SymbolTier tier, DeterministicRandom rng, Point2 center, double radius)
    {
        double start = rng.Range(0, Geo.TwoPi);
        double span = rng.Range(2.4, 3.6);
        b.ArcByAngles(tier, center, radius, start, start + span);
    }

    /// <summary>Folha amendoada formada por dois arcos entre a base e a ponta.</summary>
    private static void Almond(SymbolBuilder b, SymbolTier tier, Point2 basePoint, Point2 tip, double widthRatio)
    {
        Point2 dir = (tip - basePoint).Normalized();
        double length = basePoint.DistanceTo(tip);
        Point2 middle = Point2.Lerp(basePoint, tip, 0.5);
        Point2 offset = dir.Perp() * (length * widthRatio);
        b.Arc(tier, basePoint, middle + offset, tip);
        b.Arc(tier, basePoint, middle - offset, tip);
    }

    // ------------------------------------------------------------------ árvores

    private static void RoundedTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 2 ? rng.NextInt(15, 19) : level == 1 ? 12 : 8;
        Scallops(b, Outline, rng, r, lobes, 0.915, 0.18, 0.02);
        double trunk = TrunkMark(b, r);
        ForkingBranches(b, rng, r, trunk, level == 0 ? 4 : rng.NextInt(5, 7), _ => 1.0, 0.0);
    }

    /// <summary>Estrutura de galhos em bifurcação, comum a várias copas.</summary>
    private static void ForkingBranches(
        SymbolBuilder b,
        DeterministicRandom rng,
        double r,
        double trunk,
        int mainCount,
        Func<double, double> reach,
        double swirl)
    {
        double[] angles = SpreadAngles(rng, mainCount, 0.20);
        foreach (double a in angles)
        {
            double k = reach(a);
            double limit = r * 0.90 * k;
            Point2 start = Point2.Polar(trunk, a);
            double mainBend = swirl != 0 ? swirl * rng.Range(0.08, 0.16) : rng.Range(-0.10, 0.10);
            Curve3 main = Branch(b, Essential, start, a, r * k * rng.Range(0.44, 0.56), mainBend, limit);
            Point2 p1 = main.PointAt(1);
            double heading = main.TangentAngleAt(1);

            foreach (int side in new[] { -1, 1 })
            {
                double a2 = heading + side * rng.Range(0.30, 0.52);
                Curve3 secondary = Branch(b, Principal, p1, a2, r * k * rng.Range(0.24, 0.33), side * rng.Range(0.02, 0.12), limit);
                Point2 p2 = secondary.PointAt(1);
                double heading2 = secondary.TangentAngleAt(1);
                foreach (int twigSide in new[] { -1, 1 })
                {
                    double a3 = heading2 + twigSide * rng.Range(0.28, 0.50);
                    Branch(b, Detail, p2, a3, r * k * rng.Range(0.11, 0.17), twigSide * 0.08, r * 0.93 * k);
                }
            }

            // Raminho lateral no meio do galho principal.
            int lateral = rng.Chance(0.5) ? 1 : -1;
            Point2 origin = main.PointAt(0.55);
            Branch(b, Detail, origin, main.TangentAngleAt(0.55) + lateral * rng.Range(0.6, 0.9), r * k * rng.Range(0.15, 0.22), lateral * 0.06, limit);
        }
    }

    private static void SpreadingTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 2 ? rng.NextInt(10, 14) : 8;
        Scallops(b, Outline, rng, r, lobes, 0.87, 0.28, 0.07);
        double trunk = TrunkMark(b, r);
        int mainCount = level == 0 ? 3 : rng.NextInt(4, 6);
        double swirl = rng.Chance(0.5) ? 1 : -1;
        double limit = r * 0.84;
        double[] angles = SpreadAngles(rng, mainCount, 0.18);
        foreach (double a in angles)
        {
            Point2 start = Point2.Polar(trunk, a);
            Curve3 main = Branch(b, Essential, start, a, r * rng.Range(0.60, 0.72), swirl * rng.Range(0.10, 0.18), limit);

            // Ramos laterais alternados ao longo do galho principal.
            var stations = new (double T, int Side)[] { (0.40, 1), (0.66, -1) };
            foreach ((double t, int side) in stations)
            {
                Point2 origin = main.PointAt(t);
                double heading = main.TangentAngleAt(t) + side * rng.Range(0.55, 0.85);
                Curve3 lateral = Branch(b, Principal, origin, heading, r * rng.Range(0.20, 0.30), side * 0.10, limit);
                Point2 end = lateral.PointAt(1);
                double endHeading = lateral.TangentAngleAt(1);
                Branch(b, Detail, end, endHeading + 0.45, r * rng.Range(0.08, 0.12), 0.06, r * 0.88);
                Branch(b, Detail, end, endHeading - 0.45, r * rng.Range(0.08, 0.12), -0.06, r * 0.88);
            }

            // Bifurcação na ponta.
            Point2 tip = main.PointAt(1);
            double tipHeading = main.TangentAngleAt(1);
            foreach (int side in new[] { -1, 1 })
            {
                Curve3 fork = Branch(b, Principal, tip, tipHeading + side * rng.Range(0.32, 0.50), r * rng.Range(0.13, 0.19), side * 0.08, limit);
                Branch(b, Detail, fork.PointAt(1), fork.TangentAngleAt(1) + side * 0.40, r * rng.Range(0.06, 0.09), 0, r * 0.88);
            }
        }
    }

    private static void ColumnarTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 2 ? rng.NextInt(22, 27) : level == 1 ? 16 : 10;
        Scallops(b, Outline, rng, r, lobes, 0.94, 0.12, 0.012);
        TrunkMark(b, r);

        // Anel interno de gomos: massa densa da copa.
        Scallops(b, Essential, rng, r * 0.62, level == 0 ? 6 : rng.NextInt(10, 13), 0.90, 0.15, 0.02);

        int ticks = level == 0 ? 6 : rng.NextInt(10, 13);
        foreach (double a in SpreadAngles(rng, ticks, 0.25))
        {
            b.Line(Principal, Point2.Polar(r * rng.Range(0.66, 0.70), a), Point2.Polar(r * rng.Range(0.82, 0.88), a));
        }

        Scallops(b, Detail, rng, r * 0.30, level == 0 ? 4 : 6, 0.86, 0.15, 0.02);
        foreach (double a in SpreadAngles(rng, level == 0 ? 4 : 8, 0.25))
        {
            b.Line(Detail, Point2.Polar(r * 0.36, a), Point2.Polar(r * rng.Range(0.48, 0.55), a));
        }
    }

    private static void PyramidalTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int teeth = level == 2 ? rng.NextInt(12, 15) * 2 : level == 1 ? 20 : 12;
        double phase = rng.Range(0, Geo.TwoPi);
        double step = Geo.TwoPi / teeth;
        var ring = new List<Point2>(teeth);
        var tipAngles = new List<(double Angle, double Radius)>();
        for (int i = 0; i < teeth; i++)
        {
            double a = phase + i * step;
            bool isTip = i % 2 == 0;
            double radius = isTip ? r * (i == 0 ? 1.0 : 1.0 - rng.Range(0, 0.035)) : r * rng.Range(0.78, 0.85);
            ring.Add(Point2.Polar(radius, a));
            if (isTip)
            {
                tipAngles.Add((a, radius));
            }
        }

        b.Polyline(Outline, ring, closed: true);
        double trunk = TrunkMark(b, r);

        for (int i = 0; i < tipAngles.Count; i++)
        {
            (double a, double radius) = tipAngles[i];
            if (i % 2 == 0)
            {
                b.Line(Essential, Point2.Polar(trunk, a), Point2.Polar(radius * 0.97, a));
            }
            else
            {
                b.Line(Detail, Point2.Polar(r * 0.30, a), Point2.Polar(radius * 0.97, a));
            }
        }

        ZigzagRing(b, Principal, r, 0.56, 0.45, Math.Max(6, teeth / 2), phase + step / 2);
        ZigzagRing(b, Detail, r, 0.28, 0.20, Math.Max(5, teeth / 3), phase);
    }

    private static void ZigzagRing(SymbolBuilder b, SymbolTier tier, double r, double outer, double inner, int teeth, double phase)
    {
        int count = teeth * 2;
        var points = new List<Point2>(count);
        for (int i = 0; i < count; i++)
        {
            double a = phase + Geo.TwoPi * i / count;
            points.Add(Point2.Polar(r * (i % 2 == 0 ? outer : inner), a));
        }

        b.Polyline(tier, points, closed: true);
    }

    private static void IrregularTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        double phi1 = rng.Range(0, Geo.TwoPi);
        double phi2 = rng.Range(0, Geo.TwoPi);
        double Raw(double a) => 0.5 + 0.30 * Math.Sin(a + phi1) + 0.20 * Math.Sin(2 * a + phi2);

        double max = 0;
        for (int i = 0; i < 360; i++)
        {
            max = Math.Max(max, Raw(Geo.DegToRad(i)));
        }

        double Shape(double a) => 0.72 + 0.28 * Math.Clamp(Raw(a) / max, 0, 1);

        int lobes = level == 2 ? rng.NextInt(11, 15) : 8;
        Scallops(b, Outline, rng, r, lobes, 0.90, 0, 0.02, Shape, irregularWidths: true);
        double trunk = TrunkMark(b, r);
        ForkingBranches(b, rng, r, trunk, level == 0 ? 3 : 5, Shape, 0.0);
    }

    private static void RadialTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 2 ? rng.NextInt(20, 25) : level == 1 ? 14 : 8;
        Scallops(b, Outline, rng, r, lobes, 0.955, 0.10, 0.01);
        double trunk = TrunkMark(b, r);

        int spokes = level == 2 ? rng.NextInt(18, 23) : level == 1 ? 12 : 6;
        double[] angles = SpreadAngles(rng, spokes, 0.28);
        var reach = new double[spokes];
        for (int i = 0; i < spokes; i++)
        {
            reach[i] = r * rng.Range(0.84, 0.93);
            SymbolTier tier = (i % 3) switch { 0 => Essential, 1 => Principal, _ => Detail };
            b.Line(tier, Point2.Polar(trunk, angles[i]), Point2.Polar(reach[i], angles[i]));
        }

        // Travessas entre raios vizinhos: aspecto de galharia vista de cima.
        for (int i = 0; i < spokes; i++)
        {
            if (!rng.Chance(0.6))
            {
                continue;
            }

            int j = (i + 1) % spokes;
            double radius = r * rng.Range(0.35, 0.78);
            b.Line(Detail, Point2.Polar(Math.Min(radius, reach[i]), angles[i]), Point2.Polar(Math.Min(radius * rng.Range(0.88, 1.12), reach[j]), angles[j]));
        }
    }

    private static void FoliageTree(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 2 ? rng.NextInt(16, 21) : level == 1 ? 12 : 8;
        Scallops(b, Outline, rng, r, lobes, 0.90, 0.2, 0.025);
        TrunkMark(b, r);

        Scallops(b, Essential, rng, r * 0.72, level == 0 ? 5 : rng.NextInt(9, 12), 0.90, 0.2, 0.03, coverage: 0.72);
        Scallops(b, Principal, rng, r * 0.46, level == 0 ? 4 : rng.NextInt(6, 8), 0.88, 0.2, 0.03, coverage: 0.70);
        Scallops(b, Detail, rng, r * 0.22, 4, 0.85, 0.2, 0.03, coverage: 0.70);

        int curls = level == 2 ? 16 : level == 1 ? 8 : 3;
        for (int i = 0; i < curls; i++)
        {
            Point2 center = Point2.Polar(r * rng.Range(0.30, 0.84), rng.Range(0, Geo.TwoPi));
            LeafCurl(b, Detail, rng, center, r * rng.Range(0.045, 0.075));
        }
    }

    // ------------------------------------------------------------------ palmeiras

    private static void PinnatePalm(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int fronds = level == 2 ? rng.NextInt(9, 13) : level == 1 ? 8 : 6;
        double stipe = Math.Clamp(r * 0.07, 0.02, 0.25);
        if (stipe > r * 0.3)
        {
            CrossMark(b, r);
            stipe = r * 0.08;
        }
        else
        {
            b.Circle(Center, Point2.Zero, stipe);
        }

        double[] angles = SpreadAngles(rng, fronds, 0.16);
        for (int i = 0; i < fronds; i++)
        {
            double a = angles[i];
            double length = i == 0 ? r : r * (i % 2 == 0 ? rng.Range(0.93, 1.0) : rng.Range(0.80, 0.92));
            Point2 basePoint = Point2.Polar(stipe, a);
            Point2 tip = Point2.Polar(length, a + rng.Range(-0.10, 0.10));
            Point2 dir = (tip - basePoint).Normalized();
            double span = basePoint.DistanceTo(tip);
            Point2 rachisMid = Point2.Lerp(basePoint, tip, 0.5) + dir.Perp() * (span * rng.Range(-0.04, 0.04));

            b.Arc(Essential, basePoint, rachisMid, tip);
            var rachis = new Curve3(basePoint, rachisMid, tip);

            double halfWidth = span * 0.115;
            b.Arc(Outline, basePoint, rachisMid + dir.Perp() * halfWidth, tip);
            b.Arc(Outline, basePoint, rachisMid - dir.Perp() * halfWidth, tip);

            // Folíolos: poucos no perfil intermediário, completos no detalhado.
            double[] stations = level == 0 ? new[] { 0.5 } : new[] { 0.28, 0.44, 0.60, 0.75, 0.88 };
            for (int s = 0; s < stations.Length; s++)
            {
                double t = stations[s];
                Point2 p = rachis.PointAt(t);
                double heading = rachis.TangentAngleAt(t);
                double reach = halfWidth * 4 * t * (1 - t) * 1.1;
                SymbolTier tier = (s == 1 || s == 3) ? Principal : Detail;
                foreach (int side in new[] { -1, 1 })
                {
                    b.Line(tier, p, p + Point2.Polar(reach, heading + side * 0.95));
                }
            }
        }
    }

    private static void FanPalm(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int leaves = level == 2 ? rng.NextInt(7, 10) : level == 1 ? 6 : 5;
        double stipe = Math.Clamp(r * 0.07, 0.02, 0.25);
        if (stipe > r * 0.3)
        {
            CrossMark(b, r);
            stipe = r * 0.08;
        }
        else
        {
            b.Circle(Center, Point2.Zero, stipe);
        }

        double[] angles = SpreadAngles(rng, leaves, 0.14);
        for (int i = 0; i < leaves; i++)
        {
            double a = angles[i];
            double petiole = r * rng.Range(0.28, 0.34);
            Point2 hub = Point2.Polar(petiole, a);
            double fan = (r - petiole) * (i == 0 ? 1.0 : rng.Range(0.88, 1.0));
            double half = Math.Clamp(Math.PI / leaves * 1.3, 0.45, 0.80);

            b.Line(Essential, Point2.Polar(stipe, a), hub);

            // Borda serrilhada do leque: pontas no raio cheio, reentrâncias mais curtas.
            int teeth = level == 0 ? 3 : 6;
            int count = teeth * 2 + 1;
            var rim = new List<Point2>(count);
            for (int k = 0; k < count; k++)
            {
                double ang = a - half + 2 * half * k / (count - 1);
                double radius = fan * (k % 2 == 0 ? 1.0 : 0.84);
                rim.Add(hub + Point2.Polar(radius, ang));
            }

            b.Line(Outline, hub, rim[0]);
            b.Polyline(Outline, rim);
            b.Line(Outline, rim[^1], hub);

            // Nervuras: a central no perfil intermediário, as demais no detalhado.
            for (int k = 2; k < count - 1; k += 2)
            {
                SymbolTier tier = k == teeth ? Principal : Detail;
                b.Line(tier, hub, Point2.Lerp(hub, rim[k], 0.93));
            }
        }
    }

    // ------------------------------------------------------------------ arbustos

    private static void CloudShrub(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 0 ? 6 : rng.NextInt(8, 11);
        Scallops(b, Outline, rng, r, lobes, 0.83, 0.22, 0.03);
        CrossMark(b, r);

        Scallops(b, Principal, rng, r * 0.56, level == 0 ? 3 : rng.NextInt(4, 6), 0.84, 0.2, 0.03, coverage: 0.62);

        int curls = level == 0 ? 2 : rng.NextInt(6, 9);
        for (int i = 0; i < curls; i++)
        {
            Point2 center = Point2.Polar(r * rng.Range(0.28, 0.78), rng.Range(0, Geo.TwoPi));
            LeafCurl(b, Detail, rng, center, r * rng.Range(0.05, 0.08));
        }
    }

    private static void SpikyShrub(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int spikes = level == 0 ? 8 : rng.NextInt(12, 17);
        double phase = rng.Range(0, Geo.TwoPi);
        int count = spikes * 2;
        var ring = new List<Point2>(count);
        var tips = new List<Point2>(spikes);
        for (int i = 0; i < count; i++)
        {
            double a = phase + Geo.TwoPi * i / count;
            bool isTip = i % 2 == 0;
            double radius = isTip ? r * (i == 0 ? 1.0 : 1.0 - rng.Range(0, 0.06)) : r * rng.Range(0.58, 0.70);
            Point2 p = Point2.Polar(radius, a);
            ring.Add(p);
            if (isTip)
            {
                tips.Add(p);
            }
        }

        b.Polyline(Outline, ring, closed: true);
        CrossMark(b, r);

        for (int i = 0; i < tips.Count; i++)
        {
            Point2 dir = tips[i].Normalized();
            if (i % 2 == 0)
            {
                b.Line(Principal, dir * (r * 0.14), tips[i] * 0.96);
            }
            else
            {
                b.Line(Detail, dir * (r * 0.26), tips[i] * 0.96);
            }
        }

        ZigzagRing(b, Detail, r, 0.30, 0.19, Math.Max(4, spikes / 2), phase);
    }

    // ------------------------------------------------------------------ herbáceas, forrações e gramados

    private static void DashedCircle(SymbolBuilder b, SymbolTier tier, DeterministicRandom rng, double r, int dashes, double coverage)
    {
        double step = Geo.TwoPi / dashes;
        double phase = rng.Range(0, step);
        for (int i = 0; i < dashes; i++)
        {
            double a0 = phase + i * step;
            b.ArcByAngles(tier, Point2.Zero, r, a0, a0 + step * coverage);
        }
    }

    private static void Herbaceous(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        DashedCircle(b, Outline, rng, r, level == 0 ? 5 : rng.NextInt(6, 9), 0.68);
        CrossMark(b, r * 0.7);

        int blades = level == 0 ? 6 : rng.NextInt(14, 19);
        double[] angles = SpreadAngles(rng, blades, 0.30);
        double start = Math.Max(r * 0.07, 0.008);
        for (int i = 0; i < blades; i++)
        {
            int side = i % 2 == 0 ? 1 : -1;
            double length = r * rng.Range(0.62, 0.95);
            SymbolTier tier = (i % 3) switch { 0 => Essential, 1 => Principal, _ => Detail };
            Point2 from = Point2.Polar(start, angles[i]);
            Point2 to = Point2.Polar(length, angles[i] + side * rng.Range(0.10, 0.28));
            Point2 dir = (to - from).Normalized();
            Point2 mid = Point2.Lerp(from, to, 0.5) + dir.Perp() * (side * 0.10 * from.DistanceTo(to));
            b.Arc(tier, from, mid, to);
        }
    }

    private static void Groundcover(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 0 ? 6 : rng.NextInt(9, 13);
        Scallops(b, Outline, rng, r, lobes, 0.88, 0.30, 0.04);
        CrossMark(b, r * 0.7);

        double phase = rng.Range(0, Geo.TwoPi);
        for (int i = 0; i < 3; i++)
        {
            double a = phase + Geo.TwoPi * i / 3;
            Point2 center = Point2.Polar(r * 0.46, a);
            b.ArcByAngles(Principal, center, r * 0.16, a + Math.PI * 0.5, a + Math.PI * 1.5);
        }

        int strokes = level == 0 ? 3 : rng.NextInt(6, 10);
        for (int i = 0; i < strokes; i++)
        {
            Point2 p = Point2.Polar(r * rng.Range(0.20, 0.74), rng.Range(0, Geo.TwoPi));
            double a = rng.Range(0, Geo.TwoPi);
            b.Line(Detail, p, p + Point2.Polar(r * rng.Range(0.08, 0.12), a));
        }
    }

    private static void Grass(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        DashedCircle(b, Outline, rng, r, level == 0 ? 4 : 5, 0.55);
        CrossMark(b, r * 0.6);

        int tufts = level == 0 ? 3 : 9;
        double height = r * 0.22;
        for (int i = 0; i < tufts; i++)
        {
            double ringRadius = i < 3 ? r * 0.34 : r * 0.66;
            int inRing = i < 3 ? 3 : tufts - 3;
            int index = i < 3 ? i : i - 3;
            double a = Geo.TwoPi * index / inRing + (i < 3 ? 0.5 : 0.2) + rng.Range(-0.15, 0.15);
            Point2 q = Point2.Polar(ringRadius, a) - new Point2(0, height * 0.4);
            SymbolTier tier = i < 3 ? Essential : i < 6 ? Principal : Detail;
            b.Line(tier, q, q + new Point2(0, height));
            b.Line(tier, q, q + new Point2(-height * 0.5, height * 0.8));
            b.Line(tier, q, q + new Point2(height * 0.5, height * 0.8));
        }
    }

    // ------------------------------------------------------------------ trepadeiras e aquáticas

    private static void Climber(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        int lobes = level == 0 ? 6 : rng.NextInt(8, 11);
        Scallops(b, Outline, rng, r, lobes, 0.86, 0.25, 0.03);
        CrossMark(b, r * 0.6);

        int direction = rng.Chance(0.5) ? 1 : -1;
        double phase = rng.Range(0, Geo.TwoPi);
        double turns = level == 0 ? 1.0 : 1.5;
        double thetaMax = turns * Geo.TwoPi;
        Point2 Spiral(double theta) => Point2.Polar(r * (0.10 + 0.66 * theta / thetaMax), phase + direction * theta);

        int quarters = (int)Math.Round(turns * 4);
        for (int i = 0; i < quarters; i++)
        {
            double t0 = thetaMax * i / quarters;
            double t1 = thetaMax * (i + 1) / quarters;
            b.Arc(Essential, Spiral(t0), Spiral((t0 + t1) / 2), Spiral(t1));
        }

        int leaves = level == 0 ? 3 : 8;
        for (int i = 0; i < leaves; i++)
        {
            double theta = thetaMax * (0.18 + 0.80 * i / Math.Max(1, leaves - 1));
            Point2 p = Spiral(theta);
            int side = i % 2 == 0 ? 1 : -1;
            double heading = p.Angle + side * rng.Range(0.5, 0.9);
            Point2 tip = ClampInside(p, p + Point2.Polar(r * 0.17, heading), r * 0.86);
            Almond(b, i % 2 == 0 ? Principal : Detail, p, tip, 0.28);
        }

        int tendrils = level == 0 ? 0 : 3;
        for (int i = 0; i < tendrils; i++)
        {
            Point2 p = Spiral(thetaMax * (0.35 + 0.25 * i));
            Point2 center = ClampInside(Point2.Zero, p + Point2.Polar(r * 0.07, p.Angle), r * 0.80);
            LeafCurl(b, Detail, rng, center, r * 0.045);
        }
    }

    private static void Aquatic(SymbolBuilder b, DeterministicRandom rng, double r, int level)
    {
        double notch = rng.Range(0, Geo.TwoPi);
        const double half = 0.16;
        b.ArcByAngles(Outline, Point2.Zero, r, notch + half, notch - half + Geo.TwoPi);
        Point2 inner = Point2.Polar(r * 0.10, notch);
        b.Line(Outline, Point2.Polar(r, notch + half), inner);
        b.Line(Outline, Point2.Polar(r, notch - half), inner);
        CrossMark(b, r * 0.5);

        int veins = level == 0 ? 5 : 11;
        double from = notch + half + 0.28;
        double to = notch - half + Geo.TwoPi - 0.28;
        for (int i = 0; i < veins; i++)
        {
            double a = from + (to - from) * i / (veins - 1);
            SymbolTier tier = (i % 3) switch { 0 => Essential, 1 => Principal, _ => Detail };
            b.Line(tier, Point2.Polar(r * 0.10, a), Point2.Polar(r * rng.Range(0.80, 0.88), a));
        }

        if (level > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                double a = from + (to - from) * (0.15 + 0.30 * i);
                b.ArcByAngles(Detail, Point2.Zero, r * 0.50, a, a + 0.55);
            }
        }
    }
}
