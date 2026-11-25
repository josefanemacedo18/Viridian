using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>Papel de uma espécie na composição do canteiro.</summary>
public enum CompositionRole
{
    /// <summary>Faixa do fundo (junto ao muro, ou o centro de um canteiro em ilha): plantas mais altas.</summary>
    Fundo = 0,

    /// <summary>Faixa intermediária.</summary>
    Meio = 1,

    /// <summary>Faixa da frente (borda visível do canteiro): plantas mais baixas.</summary>
    Frente = 2,

    /// <summary>Exemplares de destaque em quantidade definida, espalhados (árvores, palmeiras, esculturais).</summary>
    Destaque = 3,

    /// <summary>Forração de base sob todo o canteiro, como maciço em hachura.</summary>
    Base = 4,
}

/// <summary>Como as faixas se organizam no canteiro.</summary>
public enum CompositionLayout
{
    /// <summary>Canteiro encostado: fundo junto a um lado (indicado na planta), frente do lado oposto.</summary>
    Encostado = 0,

    /// <summary>Canteiro em ilha: fundo no centro, frente acompanhando todo o contorno.</summary>
    Ilha = 1,
}

/// <summary>Uma espécie da receita do canteiro.</summary>
public sealed class CompositionItem
{
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Nome científico e popular gravados para reencontrar a espécie em outro catálogo.</summary>
    public string ScientificName { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public CompositionRole Role { get; set; } = CompositionRole.Meio;

    /// <summary>Parte da área do canteiro (%) ocupada pela faixa (Fundo, Meio e Frente).</summary>
    public double SharePercent { get; set; } = 33;

    /// <summary>Quantidade de exemplares (Destaque).</summary>
    public int Count { get; set; } = 1;

    /// <summary>Espaçamento entre plantas (m). Zero = o recomendado na ficha da espécie.</summary>
    public double SpacingM { get; set; }

    public bool IsBand => Role is CompositionRole.Fundo or CompositionRole.Meio or CompositionRole.Frente;

    public CompositionItem Clone() => (CompositionItem)MemberwiseClone();

    public static CompositionItem For(Species species, CompositionRole role) => new()
    {
        SpeciesId = species.Id,
        ScientificName = species.ScientificName,
        PopularName = species.PopularName,
        Role = role,
    };
}

/// <summary>Exemplar de um desenho de canteiro, em coordenadas relativas ao centro do desenho (m).</summary>
public sealed class TemplatePlant
{
    public string SpeciesId { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Rotação do símbolo (radianos).</summary>
    public double Rotation { get; set; }
}

/// <summary>Maciço de um desenho de canteiro: contorno relativo e os parâmetros de distribuição.</summary>
public sealed class TemplateMassing
{
    public string SpeciesId { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public List<List<Point2>> Rings { get; set; } = new();

    /// <summary>Parâmetros do maciço original (código, quantidade e semente são refeitos ao inserir).</summary>
    public Massing.MassingRecord Settings { get; set; } = new();
}

/// <summary>Desenho fixo de um canteiro pronto, para copiar em outro projeto sem refazer.</summary>
public sealed class TemplateDrawing
{
    /// <summary>Contorno do canteiro (relativo), quando ele foi escolhido por uma região ou desenhado.</summary>
    public List<List<Point2>> Outline { get; set; } = new();

    public List<TemplatePlant> Plants { get; set; } = new();

    public List<TemplateMassing> Massings { get; set; } = new();

    public bool IsEmpty => Plants.Count == 0 && Massings.Count == 0;

    /// <summary>Gira e desloca um ponto relativo do desenho para a posição de inserção.</summary>
    public static Point2 Place(Point2 relative, Point2 origin, double angleRad)
    {
        double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
        return new Point2(origin.X + relative.X * c - relative.Y * s, origin.Y + relative.X * s + relative.Y * c);
    }
}

/// <summary>
/// Modelo de canteiro reutilizável entre projetos, gravado na biblioteca do escritório: uma receita (espécies, papéis e
/// proporções, que se adapta a qualquer contorno) ou um desenho fixo copiado de um canteiro pronto.
/// </summary>
public sealed class BedTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Projeto de onde o modelo saiu (informativo).</summary>
    public string Source { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public CompositionLayout Layout { get; set; } = CompositionLayout.Encostado;

    public List<CompositionItem> Items { get; set; } = new();

    /// <summary>Desenho fixo (nulo nas receitas).</summary>
    public TemplateDrawing? Drawing { get; set; }

    public bool IsDrawing => Drawing is { IsEmpty: false };

    /// <summary>Resumo curto, por exemplo "receita · 4 espécies" ou "desenho · 23 exemplares, 2 maciços · 18 m²".</summary>
    public string Summary
    {
        get
        {
            if (Drawing is { } d && IsDrawing)
            {
                int species = d.Plants.Select(p => p.SpeciesId).Concat(d.Massings.Select(m => m.SpeciesId)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                double area = d.Outline.Count > 0 ? new Polygon2(d.Outline).Area : 0;
                return $"desenho · {species} espécie(s), {d.Plants.Count} exemplar(es), {d.Massings.Count} maciço(s)"
                       + (area > 0 ? $" · {area.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} m²" : string.Empty);
            }

            return $"receita · {Items.Count} espécie(s) · " + (Layout == CompositionLayout.Ilha ? "ilha" : "encostado");
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("Dê um nome ao modelo.");
        }

        if (!IsDrawing && Items.Count == 0)
        {
            problems.Add("O modelo não tem espécies.");
        }

        return problems;
    }
}

/// <summary>Um grupo de plantas planejado: uma espécie, o papel e as posições.</summary>
public sealed record PlannedGroup(CompositionItem Item, Species Species, IReadOnlyList<Point2> Points, double BandAreaM2, double SpacingM);

/// <summary>Resultado do planejamento de uma composição em um contorno.</summary>
public sealed class CompositionPlan
{
    public List<PlannedGroup> Groups { get; } = new();

    /// <summary>Espécie da forração de base (maciço em hachura sob todo o canteiro), se houver.</summary>
    public Species? BaseSpecies { get; set; }

    public CompositionItem? BaseItem { get; set; }

    public double AreaM2 { get; set; }

    public List<string> Warnings { get; } = new();

    public int PlantCount => Groups.Sum(g => g.Points.Count);
}

/// <summary>
/// Planeja uma composição multiespécie em um contorno: destaques espalhados, faixas de fundo, meio e frente por
/// proporção da área (encostado num lado ou em ilha) e forração de base. Não depende do Revit.
/// </summary>
public static class CompositionPlanner
{
    private const int TargetSamples = 6000;

    /// <param name="backPoint">Ponto do lado do fundo (canteiro encostado); nulo usa o lado mais largo do eixo principal.</param>
    public static CompositionPlan Plan(
        Polygon2 polygon,
        IReadOnlyList<(CompositionItem Item, Species Species)> items,
        CompositionLayout layout,
        Point2? backPoint,
        long seed = 1)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ArgumentNullException.ThrowIfNull(items);
        var plan = new CompositionPlan { AreaM2 = polygon.Area };
        if (polygon.IsEmpty || !(polygon.Area > 1e-6))
        {
            plan.Warnings.Add("O contorno não tem área.");
            return plan;
        }

        // Forração de base.
        foreach ((CompositionItem item, Species species) in items.Where(i => i.Item.Role == CompositionRole.Base))
        {
            if (plan.BaseSpecies is null)
            {
                plan.BaseSpecies = species;
                plan.BaseItem = item;
            }
            else
            {
                plan.Warnings.Add($"Só uma forração de base por canteiro: {species.PopularName} ficou de fora.");
            }
        }

        Func<Point2, double> key = KeyFunction(polygon, layout, backPoint);
        List<(Point2 P, double K)> samples = Samples(polygon, key);
        var accents = new List<(Point2 P, double R)>();
        var placed = new SpatialIndex(Math.Max(0.2, items.Where(i => i.Item.Role != CompositionRole.Base).Select(i => Spacing(i.Item, i.Species)).DefaultIfEmpty(0.5).Max()));

        // 1. Destaques: espalhados pela parte do fundo (a mais alta do canteiro), o mais afastados possível entre si.
        foreach ((CompositionItem item, Species species) in items.Where(i => i.Item.Role == CompositionRole.Destaque))
        {
            double radius = Math.Max(0.15, species.CrownDiameterM / 2);
            int count = Math.Clamp(item.Count, 0, 500);
            List<Point2> chosen = SpreadAccents(samples, polygon, radius, count, accents);
            if (chosen.Count < count)
            {
                plan.Warnings.Add($"{species.PopularName}: couberam {chosen.Count} de {count} destaque(s) sem encostar as copas.");
            }

            // As copas afastam os outros destaques; as faixas só respeitam a coroa junto ao tronco (planta-se sob a copa).
            foreach (Point2 p in chosen)
            {
                accents.Add((p, radius));
                placed.Add(p, TrunkClearance(species));
            }

            plan.Groups.Add(new PlannedGroup(item, species, chosen, 0, radius * 2));
        }

        // 2. Faixas por proporção da área: fundo, meio e frente, nessa ordem (várias espécies no mesmo papel = faixas seguidas).
        var bands = items.Where(i => i.Item.IsBand).OrderBy(i => (int)i.Item.Role).ToList();
        double total = bands.Sum(b => Math.Max(0, b.Item.SharePercent));
        if (bands.Count > 0 && !(total > 0))
        {
            plan.Warnings.Add("As faixas estão com 0% da área.");
            return plan;
        }

        if (bands.Count > 0 && Math.Abs(total - 100) > 0.5)
        {
            plan.Warnings.Add($"As faixas somam {total:0.#}% da área; as proporções foram ajustadas para 100%.");
        }

        double[] keys = samples.Select(s => s.K).OrderByDescending(k => k).ToArray();
        double cumulative = 0;
        double upper = double.PositiveInfinity;
        for (int b = 0; b < bands.Count; b++)
        {
            (CompositionItem item, Species species) = bands[b];
            cumulative += Math.Max(0, item.SharePercent) / total;
            double lower = b == bands.Count - 1 || keys.Length == 0
                ? double.NegativeInfinity
                : keys[Math.Clamp((int)Math.Round(cumulative * keys.Length) - 1, 0, keys.Length - 1)];
            double share = Math.Max(0, item.SharePercent) / total;
            double spacing = Spacing(item, species);
            if (Reports.SupplyUnits.IsAreaBased(species.SupplyUnit))
            {
                plan.Warnings.Add($"{species.PopularName} é vendida por área ({species.SupplyUnit}): use-a como forração de base.");
                upper = lower;
                continue;
            }

            var request = new DistributionRequest
            {
                Pattern = DistributionPattern.Natural,
                SpacingM = spacing,
                EdgeMarginM = Math.Min(spacing / 2, 0.5),
                Seed = seed + 101 * (b + 1),
                MaxCount = 50000,
            };
            DistributionResult result = PlantDistributor.Distribute(polygon, request);
            if (result.Exceeded)
            {
                plan.Warnings.Add($"{species.PopularName}: {result.Message}");
                upper = lower;
                continue;
            }

            double lo = lower, hi = upper;
            var points = new List<Point2>();
            foreach (Point2 p in result.Points)
            {
                double k = key(p);
                if (k > hi || k <= lo)
                {
                    continue;
                }

                if (placed.Conflicts(p, spacing / 2 * 0.8))
                {
                    continue;
                }

                points.Add(p);
            }

            foreach (Point2 p in points)
            {
                placed.Add(p, spacing / 2);
            }

            if (points.Count == 0)
            {
                plan.Warnings.Add($"{species.PopularName}: a faixa de {share * 100:0}% ficou estreita demais para o espaçamento de {spacing:0.##} m.");
            }

            plan.Groups.Add(new PlannedGroup(item, species, points, polygon.Area * share, spacing));
            upper = lower;
        }

        return plan;
    }

    /// <summary>Espaçamento usado para a espécie na composição.</summary>
    public static double Spacing(CompositionItem item, Species species) =>
        Math.Clamp(item.SpacingM > 0 ? item.SpacingM : species.EffectiveSpacingM, 0.05, 30);

    /// <summary>
    /// Valor que ordena os pontos do fundo (maior) para a frente (menor): distância à borda no canteiro em ilha;
    /// projeção na direção do fundo no canteiro encostado.
    /// </summary>
    public static Func<Point2, double> KeyFunction(Polygon2 polygon, CompositionLayout layout, Point2? backPoint)
    {
        if (layout == CompositionLayout.Ilha)
        {
            return p => polygon.DistanceToBoundary(p);
        }

        Point2 center = polygon.Centroid();
        Point2 across = PrincipalAxis(polygon).Perp();
        if (backPoint is Point2 back)
        {
            Point2 toward = back - center;
            if (toward.Length > 1e-9)
            {
                // O fundo fica do lado indicado, sempre na direção da largura (perpendicular ao eixo principal).
                across = toward.Dot(across) >= 0 ? across : -across;
                if (Math.Abs(toward.Normalized().Dot(across)) < 0.2)
                {
                    // Clique perto da ponta do canteiro: o fundo acompanha o comprimento.
                    across = toward.Normalized().Dot(across.Perp()) >= 0 ? across.Perp() : -across.Perp();
                }
            }
        }

        return p => (p - center).Dot(across);
    }

    /// <summary>Direção principal (do comprimento) do contorno externo, pela covariância dos vértices ponderada pelas arestas.</summary>
    public static Point2 PrincipalAxis(Polygon2 polygon)
    {
        IReadOnlyList<Point2> ring = polygon.Rings[0];
        double sw = 0, mx = 0, my = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            Point2 a = ring[i], b = ring[(i + 1) % ring.Count];
            double w = (b - a).Length;
            Point2 m = (a + b) / 2;
            sw += w;
            mx += m.X * w;
            my += m.Y * w;
        }

        if (!(sw > 0))
        {
            return new Point2(1, 0);
        }

        mx /= sw;
        my /= sw;
        double sxx = 0, syy = 0, sxy = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            Point2 a = ring[i], b = ring[(i + 1) % ring.Count];
            double w = (b - a).Length;
            Point2 m = (a + b) / 2;
            double dx = m.X - mx, dy = m.Y - my;
            sxx += dx * dx * w;
            syy += dy * dy * w;
            sxy += dx * dy * w;
        }

        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return new Point2(Math.Cos(angle), Math.Sin(angle));
    }

    private static List<(Point2 P, double K)> Samples(Polygon2 polygon, Func<Point2, double> key)
    {
        Bounds2 b = polygon.Bounds;
        double step = Math.Sqrt(Math.Max(1e-6, polygon.Area) / TargetSamples);
        var samples = new List<(Point2, double)>();
        for (double y = b.MinY + step / 2; y < b.MaxY; y += step)
        {
            for (double x = b.MinX + step / 2; x < b.MaxX; x += step)
            {
                var p = new Point2(x, y);
                if (polygon.Contains(p))
                {
                    samples.Add((p, key(p)));
                }
            }
        }

        return samples;
    }

    /// <summary>Raio livre em volta do tronco de um destaque (coroa de plantio), onde as faixas não entram.</summary>
    public static double TrunkClearance(Species species) => Math.Clamp(species.CrownDiameterM * 0.12, 0.35, 0.9);

    /// <summary>
    /// Destaques espalhados por igual na metade do fundo: escolha gulosa do ponto mais afastado e, depois, relaxação
    /// (cada destaque vai para o centro da sua parte do canteiro), sem copas encostadas.
    /// </summary>
    private static List<Point2> SpreadAccents(List<(Point2 P, double K)> samples, Polygon2 polygon, double radius, int count, List<(Point2 P, double R)> accents)
    {
        var chosen = new List<Point2>();
        if (count <= 0 || samples.Count == 0)
        {
            return chosen;
        }

        bool Free(Point2 p) => accents.All(a => (a.P - p).Length >= (a.R + radius) * 0.9);

        // Candidatos: metade do fundo do canteiro, com a copa o mais dentro possível do contorno.
        double median = samples.Select(s => s.K).OrderByDescending(k => k).ElementAt(samples.Count / 2);
        double inner = samples.Max(s => polygon.DistanceToBoundary(s.P));
        double need = Math.Min(radius * 0.8, inner * 0.8);
        List<Point2> candidates = samples.Where(s => s.K >= median && polygon.DistanceToBoundary(s.P) >= need && Free(s.P)).Select(s => s.P).ToList();
        if (candidates.Count == 0)
        {
            candidates = samples.Where(s => Free(s.P)).OrderByDescending(s => polygon.DistanceToBoundary(s.P)).Take(Math.Max(1, samples.Count / 10)).Select(s => s.P).ToList();
        }

        if (candidates.Count == 0)
        {
            return chosen;
        }

        Point2 first = candidates.OrderByDescending(c => polygon.DistanceToBoundary(c)).ThenBy(c => c.X).ThenBy(c => c.Y).First();
        chosen.Add(first);
        var best = candidates.Select(c => (c - first).Length).ToArray();
        while (chosen.Count < count)
        {
            int pick = -1;
            double far = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (best[i] > far)
                {
                    far = best[i];
                    pick = i;
                }
            }

            if (pick < 0 || far < radius * 2)
            {
                break; // não cabe outro sem encostar as copas
            }

            Point2 next = candidates[pick];
            chosen.Add(next);
            for (int i = 0; i < candidates.Count; i++)
            {
                best[i] = Math.Min(best[i], (candidates[i] - next).Length);
            }
        }

        if (chosen.Count < 2)
        {
            return chosen;
        }

        // Relaxação: cada destaque vai para o candidato mais perto do centro da sua região.
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var sums = new Point2[chosen.Count];
            var counts = new int[chosen.Count];
            foreach (Point2 c in candidates)
            {
                int nearest = 0;
                for (int k = 1; k < chosen.Count; k++)
                {
                    if ((c - chosen[k]).LengthSquared < (c - chosen[nearest]).LengthSquared)
                    {
                        nearest = k;
                    }
                }

                sums[nearest] += c;
                counts[nearest]++;
            }

            var moved = new List<Point2>(chosen.Count);
            for (int k = 0; k < chosen.Count; k++)
            {
                Point2 target = counts[k] == 0 ? chosen[k] : sums[k] / counts[k];
                moved.Add(candidates.OrderBy(c => (c - target).LengthSquared).First());
            }

            bool apart = true;
            for (int a = 0; a < moved.Count && apart; a++)
            {
                for (int b = a + 1; b < moved.Count; b++)
                {
                    if ((moved[a] - moved[b]).Length < radius * 2 - 1e-9)
                    {
                        apart = false;
                        break;
                    }
                }
            }

            if (!apart || moved.SequenceEqual(chosen))
            {
                break;
            }

            chosen = moved;
        }

        return chosen;
    }

    /// <summary>Índice espacial simples (grade) para conferir distâncias entre plantas de espécies diferentes.</summary>
    private sealed class SpatialIndex
    {
        private readonly double _cell;
        private readonly Dictionary<(int, int), List<(Point2 P, double R)>> _cells = new();
        private double _maxRadius;

        public SpatialIndex(double cell)
        {
            _cell = cell;
        }

        public void Add(Point2 p, double radius)
        {
            (int, int) k = ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell));
            if (!_cells.TryGetValue(k, out List<(Point2, double)>? list))
            {
                _cells[k] = list = new List<(Point2, double)>();
            }

            list.Add((p, radius));
            _maxRadius = Math.Max(_maxRadius, radius);
        }

        /// <summary>Indica se um ponto de raio <paramref name="radius"/> invadiria uma planta já colocada.</summary>
        public bool Conflicts(Point2 p, double radius)
        {
            double reach = radius + _maxRadius;
            int r = (int)Math.Ceiling(reach / _cell);
            int cx = (int)Math.Floor(p.X / _cell), cy = (int)Math.Floor(p.Y / _cell);
            for (int x = cx - r; x <= cx + r; x++)
            {
                for (int y = cy - r; y <= cy + r; y++)
                {
                    if (!_cells.TryGetValue((x, y), out List<(Point2 P, double R)>? list))
                    {
                        continue;
                    }

                    foreach ((Point2 q, double qr) in list)
                    {
                        double min = (radius + qr) * 0.9;
                        if ((q - p).LengthSquared < min * min)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
