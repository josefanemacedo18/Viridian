using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Model;

namespace Viridian.Core.Detailing;

/// <summary>Tipos de detalhe de plantio.</summary>
public enum DetailKind
{
    CovaArvore = 0,
    BercoPalmeira = 1,
    CovaArbusto = 2,
    CanteiroForracao = 3,
    Gramado = 4,
}

/// <summary>Dimensões calculadas do plantio de uma espécie (metros).</summary>
public sealed record PlantingDimensions(
    DetailKind Kind,
    double SeedlingHeightM,
    double PitWidthM,
    double PitDepthM,
    double RootBallM,
    double StakeHeightM,
    int Stakes,
    double CrownRingM,
    double SoilLayerM,
    double MulchM);

/// <summary>
/// Detalhes de plantio prontos. As dimensões são valores de referência usuais em especificações de
/// paisagismo no Brasil e devem ser conferidos pelo responsável técnico e pelas normas locais.
/// </summary>
public static class PlantingDetails
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static DetailKind KindFor(PlantCategory category) => category switch
    {
        PlantCategory.Arvore => DetailKind.CovaArvore,
        PlantCategory.Palmeira => DetailKind.BercoPalmeira,
        PlantCategory.Arbusto or PlantCategory.Trepadeira => DetailKind.CovaArbusto,
        PlantCategory.Gramado => DetailKind.Gramado,
        _ => DetailKind.CanteiroForracao,
    };

    public static string Name(DetailKind kind) => kind switch
    {
        DetailKind.CovaArvore => "Plantio de árvore em cova",
        DetailKind.BercoPalmeira => "Plantio de palmeira em berço",
        DetailKind.CovaArbusto => "Plantio de arbusto em cova",
        DetailKind.CanteiroForracao => "Plantio de forração e herbáceas em canteiro",
        DetailKind.Gramado => "Implantação de gramado em placas",
        _ => kind.ToString(),
    };

    /// <summary>Dimensões do plantio, a partir da categoria e da altura da muda.</summary>
    public static PlantingDimensions Dimensions(Species species)
    {
        DetailKind kind = KindFor(species.Category);
        double h = species.PlantingHeightM > 0 ? species.PlantingHeightM : DefaultSeedlingHeight(species);
        switch (kind)
        {
            case DetailKind.CovaArvore:
            {
                double pit = h <= 1.0 ? 0.40 : h <= 2.0 ? 0.60 : h <= 3.0 ? 0.80 : 1.00;
                return new PlantingDimensions(kind, h, pit, pit, Round(pit * 0.6), Round(h * 0.75 + 0.5), h >= 1.5 ? 2 : 1, 1.00, 0, 0.05);
            }

            case DetailKind.BercoPalmeira:
            {
                double pit = h <= 2.0 ? 0.80 : h <= 4.0 ? 1.00 : 1.20;
                int stakes = h > 3.0 ? 3 : 2;
                return new PlantingDimensions(kind, h, pit, pit, Round(pit * 0.65), Round(Math.Min(h * 0.5, 2.5) + 0.5), stakes, 1.20, 0, 0.05);
            }

            case DetailKind.CovaArbusto:
            {
                double pit = h <= 0.5 ? 0.30 : h <= 1.0 ? 0.40 : 0.50;
                return new PlantingDimensions(kind, h, pit, pit, Round(pit * 0.6), 0, 0, 0, 0, 0.05);
            }

            case DetailKind.Gramado:
                return new PlantingDimensions(kind, 0, 0, 0, 0, 0, 0, 0, 0.10, 0);

            default:
                return new PlantingDimensions(kind, h, 0, 0, 0, 0, 0, 0, 0.20, 0.05);
        }
    }

    /// <summary>Cova quadrada: largura × largura × profundidade (m³ por unidade). Zero para canteiros e gramados.</summary>
    public static double PitVolumeM3(PlantingDimensions d) =>
        d.PitWidthM > 0 && d.PitDepthM > 0 ? d.PitWidthM * d.PitWidthM * d.PitDepthM : 0;

    /// <summary>
    /// Texto da escavação em centímetros: "60 × 60 × 60 cm" para covas ou "camada de 20 cm" para canteiros e gramados.
    /// </summary>
    public static string ExcavationText(PlantingDimensions d)
    {
        static string Cm(double m) => Math.Round(m * 100).ToString("0", Pt);
        if (d.PitWidthM > 0 && d.PitDepthM > 0)
        {
            return $"{Cm(d.PitWidthM)} × {Cm(d.PitWidthM)} × {Cm(d.PitDepthM)} cm";
        }

        return d.SoilLayerM > 0 ? $"camada de {Cm(d.SoilLayerM)} cm" : "—";
    }

    /// <summary>
    /// Volume de escavação (m³) para a quantidade informada: covas × volume da cova, ou área (m²) × camada de solo nos
    /// canteiros e gramados vendidos por área. Nulo quando não é possível estimar (forração vendida por muda sem área).
    /// </summary>
    public static double? ExcavationM3(PlantingDimensions d, double quantity, bool quantityIsArea)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        if (d.PitWidthM > 0 && d.PitDepthM > 0 && !quantityIsArea)
        {
            return quantity * PitVolumeM3(d);
        }

        if (d.SoilLayerM > 0 && quantityIsArea)
        {
            return quantity * d.SoilLayerM;
        }

        return null;
    }

    private static double DefaultSeedlingHeight(Species s) => s.Category switch
    {
        PlantCategory.Arvore => 2.0,
        PlantCategory.Palmeira => 1.5,
        PlantCategory.Arbusto => 0.6,
        PlantCategory.Trepadeira => 0.6,
        _ => 0.2,
    };

    private static double Round(double v) => Math.Round(v * 20) / 20; // múltiplos de 5 cm

    public static string M(double meters) => meters.ToString("0.00", Pt) + " m";

    /// <summary>Desenho do detalhe em corte (metros reais, solo em y = 0, eixo da planta em x = 0).</summary>
    public static Drawing Draw(Species species, ColorSettings colors)
    {
        PlantingDimensions d = Dimensions(species);
        var drawing = new Drawing { Title = Name(d.Kind) + " — " + species.PopularName };
        Rgb foliage = VegetationPalette.Foliage(species, colors);
        Rgb? flower = VegetationPalette.Flower(species, colors);
        int seed = species.EffectiveSymbolSeed;
        switch (d.Kind)
        {
            case DetailKind.CovaArvore:
            case DetailKind.BercoPalmeira:
            case DetailKind.CovaArbusto:
                DrawPit(drawing, species, d, foliage, flower, seed);
                break;
            case DetailKind.Gramado:
                DrawLawn(drawing, d, foliage, seed);
                break;
            default:
                DrawBed(drawing, species, d, foliage, flower, seed);
                break;
        }

        return drawing;
    }

    /// <summary>Espaço vertical entre notas, para texto de 2 mm na escala 1:20.</summary>
    private const double NoteGap = 0.09;

    private static readonly Rgb Wood = new(0xC2, 0x9A, 0x66);

    private static void DrawPit(Drawing g, Species species, PlantingDimensions d, Rgb foliage, Rgb? flower, int seed)
    {
        double w = d.PitWidthM;
        double depth = d.PitDepthM;
        double half = w / 2;
        double h = d.SeedlingHeightM;
        double groundExtent = Math.Max(half + 0.5, d.CrownRingM / 2 + 0.3);
        double below = depth + 0.18;
        var notes = new List<(Point2, string)>();

        // Terreno natural em corte (hachura de solo) e a cova com terra vegetal preparada.
        g.Box(-groundExtent, -below, groundExtent, 0, DrawingStyle.NaturalSoil, MaterialHatches.NaturalSoil, DrawingStyle.HatchInk(DrawingStyle.NaturalSoil));
        g.Box(-half, -depth, half, 0, DrawingStyle.Earth, MaterialHatches.Earth, DrawingStyle.EarthDark);
        double drain = 0;
        if (d.Kind == DetailKind.BercoPalmeira)
        {
            // Camada drenante de brita no fundo do berço.
            drain = Round(Math.Max(0.10, depth * 0.12));
            g.Box(-half, -depth, half, -depth + drain, DrawingStyle.Gravel, MaterialHatches.Gravel, DrawingStyle.HatchInk(DrawingStyle.Gravel));
            g.Stroke(new Point2(-half, -depth + drain), new Point2(half, -depth + drain), LinePen.Fina);
            notes.Add((new Point2(half * 0.55, -depth + drain / 2), $"Camada drenante de brita, {drain * 100:0} cm"));
        }

        g.Stroke(new Point2(-groundExtent, 0), new Point2(-half, 0), LinePen.Grossa);
        g.Stroke(new Point2(half, 0), new Point2(groundExtent, 0), LinePen.Grossa);
        g.Strokes(new List<Point2> { new(-half, 0), new(-half, -depth), new(half, -depth), new(half, 0) }, false, LinePen.Grossa);

        // Torrão com raízes.
        double ball = d.RootBallM;
        Point2 ballCenter = new(0, -0.02 - ball * 0.45);
        List<Point2> ballShape = Sketch.CrownOutline(ballCenter, ball / 2, ball * 0.45, seed + 3, 9, 0.05);
        g.Fill(ballShape, DrawingStyle.RootBall, MaterialHatches.RootBall, DrawingStyle.HatchInk(DrawingStyle.RootBall));
        g.Strokes(ballShape, true, LinePen.Media);
        var roots = new DeterministicRandom(seed + 9);
        for (int i = 0; i < 7; i++)
        {
            double a = Math.PI * (1.15 + 0.7 * i / 6.0) + roots.Range(-0.1, 0.1);
            Point2 from = ballCenter + new Point2(Math.Cos(a) * ball * 0.42, Math.Sin(a) * ball * 0.38);
            Point2 to = from + new Point2(Math.Cos(a) * ball * 0.22, Math.Sin(a) * ball * 0.2 - ball * 0.05);
            g.Stroke(from, to, LinePen.Textura);
        }

        notes.Add((ballCenter + new Point2(ball * 0.3, 0), $"Torrão íntegro Ø ≈ {M(d.RootBallM)}; colo da muda no nível do solo"));
        notes.Add((new Point2(half * 0.6, -depth * 0.6), "Terra vegetal + composto orgânico (3:1) e adubação de plantio"));

        // Coroamento com cobertura morta (sobre o solo).
        if (d.CrownRingM > 0 && d.MulchM > 0)
        {
            double r = d.CrownRingM / 2;
            g.Box(-r, 0, r, d.MulchM, DrawingStyle.Mulch, MaterialHatches.Mulch, DrawingStyle.HatchInk(DrawingStyle.Mulch), LinePen.Fina);
        }

        double crownBase;
        if (d.Kind == DetailKind.BercoPalmeira)
        {
            crownBase = h * 0.45;
            Sketch.Palm(g, new Point2(0, d.MulchM), Math.Max(0.4, h - d.MulchM), h * 0.95, foliage, seed);
            notes.Add((new Point2(Math.Max(0.07, h * 0.03), crownBase * 0.5), $"{species.PopularName}: muda h = {M(h)}, estipe sem danos"));
        }
        else
        {
            bool shrub = d.Kind == DetailKind.CovaArbusto;
            crownBase = shrub ? h * 0.12 : h * 0.5;
            double rx = Math.Max(0.15, shrub ? h * 0.45 : h * 0.22);
            double ry = (h - crownBase) / 2;
            Sketch.Trunk(g, new Point2(0, 0), crownBase + ry * 0.4, Math.Max(0.035, h * 0.03), seed);
            Sketch.Crown(g, new Point2(0, crownBase + ry), rx, ry, foliage, seed, flower);
            notes.Add((new Point2(rx * 0.7, crownBase + ry * 1.3), $"{species.PopularName}: muda h = {M(h)}"));
        }

        // Tutores ou escoras de madeira, até abaixo da copa, e amarrilho em "8".
        if (d.Stakes > 0)
        {
            double above = Math.Min(d.StakeHeightM - 0.5, crownBase * 0.92);
            double x = Math.Max(0.12, d.RootBallM / 2 + 0.05);
            const double stake = 0.035;
            foreach (double sx in d.Stakes > 1 ? new[] { -x, x } : new[] { -x })
            {
                g.Box(sx - stake / 2, -0.5, sx + stake / 2, above, Wood, MaterialHatches.Wood, Wood.Shade(0.35), LinePen.Fina);
            }

            double tie = above * 0.85;
            double tx = d.Stakes > 1 ? x : 0;
            g.Stroke(new Point2(-x, tie), new Point2(tx, tie - 0.03), LinePen.Media);
            g.Stroke(new Point2(-x, tie - 0.03), new Point2(tx, tie), LinePen.Media);
            string stakes = d.Kind == DetailKind.BercoPalmeira
                ? $"{d.Stakes} escoras de madeira tratada, {M(d.StakeHeightM)}, com proteção na estipe"
                : d.Stakes == 1 ? $"Tutor de madeira tratada, {M(d.StakeHeightM)} (0,50 m cravado)" : $"{d.Stakes} tutores de madeira tratada, {M(d.StakeHeightM)} (0,50 m cravados)";
            notes.Add((new Point2(d.Stakes > 1 ? x : -x, above * 0.6), stakes));
            notes.Add((new Point2(d.Stakes > 1 ? x * 0.5 : -x * 0.5, tie), "Amarrilho em \"8\" com borracha ou sisal"));
        }

        if (d.CrownRingM > 0)
        {
            double r = d.CrownRingM / 2;
            g.Dimension(new Point2(-r, d.MulchM), new Point2(r, d.MulchM), -(depth + 0.35), "Coroamento Ø " + M(d.CrownRingM));
        }

        notes.Add((new Point2(Math.Max(half * 0.8, d.CrownRingM * 0.35), d.MulchM * 0.6), $"Cobertura morta (casca ou folhas trituradas) {d.MulchM * 100:0} cm"));
        notes.Add((new Point2(half + 0.25, -0.08), "Solo natural descompactado nas paredes da cova"));

        g.Dimension(new Point2(-half, -depth), new Point2(half, -depth), -0.15, "Cova " + M(w));
        g.Dimension(new Point2(-half, 0), new Point2(-half, -depth), -0.15, "Prof. " + M(depth));
        g.Dimension(new Point2(-groundExtent, 0), new Point2(-groundExtent, h), 0.12, "h = " + M(h));
        g.Notes(notes, groundExtent + 0.35, NoteGap);
    }

    private static void DrawBed(Drawing g, Species species, PlantingDimensions d, Rgb foliage, Rgb? flower, int seed)
    {
        double layer = d.SoilLayerM;
        double spacing = species.EffectiveSpacingM > 0 ? Math.Clamp(species.EffectiveSpacingM, 0.1, 0.8) : 0.25;
        double width = Math.Clamp(spacing * 6, 1.2, 3.0);
        double extent = width / 2 + 0.35;
        g.Box(-extent, -layer - 0.15, extent, 0, DrawingStyle.NaturalSoil, MaterialHatches.NaturalSoil, DrawingStyle.HatchInk(DrawingStyle.NaturalSoil));
        g.Box(-width / 2, -layer, width / 2, 0, DrawingStyle.Earth, MaterialHatches.Earth, DrawingStyle.EarthDark);
        g.Box(-width / 2, 0, width / 2, d.MulchM, DrawingStyle.Mulch, MaterialHatches.Mulch, DrawingStyle.HatchInk(DrawingStyle.Mulch), LinePen.Fina);
        g.Stroke(new Point2(-extent, 0), new Point2(-width / 2, 0), LinePen.Grossa);
        g.Stroke(new Point2(width / 2, 0), new Point2(extent, 0), LinePen.Grossa);
        g.Strokes(new List<Point2> { new(-width / 2, 0), new(-width / 2, -layer), new(width / 2, -layer), new(width / 2, 0) }, false, LinePen.Grossa);

        double plantHeight = Math.Clamp(species.AdultHeightM > 0 ? species.AdultHeightM * 0.5 : 0.25, 0.08, 0.5);
        double r = Math.Min(spacing * 0.48, 0.28);
        double firstX = -width / 2 + spacing / 2;
        bool grassy = species.Category is PlantCategory.Herbacea && r < 0.12;
        int i = 0;
        for (double x = firstX; x <= width / 2 - spacing / 2 + 1e-9; x += spacing, i++)
        {
            if (grassy)
            {
                Sketch.Tuft(g, new Point2(x, d.MulchM), plantHeight, r * 2, seed + i, LinePen.Media);
            }
            else
            {
                Sketch.Shrub(g, new Point2(x, d.MulchM), plantHeight, r * 2, i % 2 == 0 ? foliage : foliage.Shade(0.08), seed + i * 13, flower);
            }
        }

        g.Dimension(new Point2(-width / 2, -layer), new Point2(-width / 2, 0), 0.15, "Preparo " + M(layer));
        g.Dimension(new Point2(firstX, -layer), new Point2(firstX + spacing, -layer), -0.12, "Espaçamento " + M(spacing));
        g.Notes(new (Point2, string)[]
        {
            (new Point2(width / 2 - spacing / 2, d.MulchM + plantHeight * 0.6), $"Mudas de {species.PopularName} em torrão ou bandeja, em quincôncio"),
            (new Point2(width / 2 - 0.1, d.MulchM * 0.5), $"Cobertura morta {d.MulchM * 100:0} cm até o fechamento do canteiro"),
            (new Point2(width / 2 - 0.2, -layer / 2), "Solo revolvido e corrigido: terra vegetal + composto orgânico (3:1)"),
        }, width / 2 + 0.45, NoteGap);
    }

    private static void DrawLawn(Drawing g, PlantingDimensions d, Rgb foliage, int seed)
    {
        double width = 2.0;
        double layer = d.SoilLayerM;
        double extent = width / 2 + 0.2;
        g.Box(-extent, -layer - 0.15, extent, 0, DrawingStyle.Compacted(), MaterialHatches.Compacted, DrawingStyle.HatchInk(DrawingStyle.Compacted()));
        g.Box(-width / 2, -layer, width / 2, 0, DrawingStyle.Earth, MaterialHatches.Earth, DrawingStyle.EarthDark);
        g.Stroke(new Point2(-extent, 0), new Point2(extent, 0), LinePen.Grossa);
        g.Stroke(new Point2(-width / 2, -layer), new Point2(width / 2, -layer), LinePen.Media, dashed: true);
        const double plate = 0.03;
        int k = 0;
        for (double x = -width / 2; x < width / 2 - 1e-9; x += 0.40, k++)
        {
            g.Box(x + 0.005, 0, x + 0.395, plate, k % 2 == 0 ? foliage : foliage.Shade(0.08), edge: LinePen.Fina);
            for (double tx = x + 0.03; tx < x + 0.38; tx += 0.06)
            {
                Sketch.Tuft(g, new Point2(tx, plate), 0.035, 0.03, seed + (int)(tx * 1000), LinePen.Textura);
            }
        }

        g.Dimension(new Point2(-width / 2, -layer), new Point2(-width / 2, 0), 0.15, "Terra " + M(layer));
        g.Dimension(new Point2(-width / 2, plate), new Point2(-width / 2 + 0.4, plate), 0.12, "Placa 0,40 m");
        g.Notes(new (Point2, string)[]
        {
            (new Point2(width / 2 - 0.2, plate), "Placas justapostas, juntas desencontradas; compactar levemente e rolar"),
            (new Point2(width / 2 - 0.3, -layer / 2), "Terra vegetal peneirada e nivelada, com adubação de base"),
            (new Point2(width / 2 - 0.4, -layer - 0.06), "Subleito descompactado e livre de entulho"),
        }, width / 2 + 0.4, NoteGap);
    }
}
