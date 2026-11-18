using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
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
        Rgb foliage = VegetationPalette.CrownFill(species, colors);
        Rgb soil = new(0xC9, 0xB0, 0x8C);
        switch (d.Kind)
        {
            case DetailKind.CovaArvore:
            case DetailKind.BercoPalmeira:
            case DetailKind.CovaArbusto:
                DrawPit(drawing, species, d, foliage, soil);
                break;
            case DetailKind.Gramado:
                DrawLawn(drawing, d, foliage, soil);
                break;
            default:
                DrawBed(drawing, species, d, foliage, soil);
                break;
        }

        return drawing;
    }

    /// <summary>Espaço vertical entre notas, para texto de 2 mm na escala 1:20.</summary>
    private const double NoteGap = 0.09;

    private static void DrawPit(Drawing g, Species species, PlantingDimensions d, Rgb foliage, Rgb soil)
    {
        double w = d.PitWidthM;
        double depth = d.PitDepthM;
        double half = w / 2;
        double h = d.SeedlingHeightM;
        double groundExtent = Math.Max(half + 0.5, d.CrownRingM / 2 + 0.3);
        var notes = new List<(Point2, string)>();

        // Solo natural (com hachura) e cova preenchida com terra preparada.
        g.Line(new Point2(-groundExtent, 0), new Point2(-half, 0), heavy: true);
        g.Line(new Point2(half, 0), new Point2(groundExtent, 0), heavy: true);
        var pit = new List<Point2> { new(-half, 0), new(-half, -depth), new(half, -depth), new(half, 0) };
        g.Fills.Add(new DrawFill(new List<Point2>(pit) { new(-half, 0) }, soil.Tint(0.35)));
        g.Polyline(pit, false, heavy: true);
        for (double x = -groundExtent + 0.08; x < -half - 0.05; x += 0.12)
        {
            g.Line(new Point2(x, -0.02), new Point2(x - 0.08, -0.10));
        }

        for (double x = half + 0.13; x < groundExtent; x += 0.12)
        {
            g.Line(new Point2(x, -0.02), new Point2(x - 0.08, -0.10));
        }

        // Torrão.
        double ball = d.RootBallM;
        Point2 ballCenter = new(0, -0.02 - ball * 0.45);
        List<Point2> ballShape = Drawing.Ellipse(ballCenter, ball / 2, ball * 0.45);
        g.Fills.Add(new DrawFill(ballShape, soil.Shade(0.15)));
        g.Polyline(ballShape, true);
        notes.Add((ballCenter + new Point2(ball * 0.3, 0), $"Torrão íntegro Ø ≈ {M(d.RootBallM)}; colo da muda no nível do solo"));
        notes.Add((new Point2(half * 0.6, -depth * 0.85), "Terra vegetal + composto orgânico (3:1) e adubação de plantio"));

        double crownBase;
        if (d.Kind == DetailKind.BercoPalmeira)
        {
            // Estipe e folhas arqueadas.
            double stipe = Math.Max(0.07, h * 0.06);
            crownBase = h * 0.45;
            g.Line(new Point2(-stipe / 2, 0), new Point2(-stipe * 0.4, crownBase), heavy: true);
            g.Line(new Point2(stipe / 2, 0), new Point2(stipe * 0.4, crownBase), heavy: true);
            Point2 top = new(0, crownBase);
            double length = h * 0.55;
            for (int i = 0; i < 9; i++)
            {
                double angle = Math.PI * (0.12 + 0.76 * i / 8.0);
                var frond = new List<Point2>();
                for (int k = 0; k <= 8; k++)
                {
                    double t = k / 8.0;
                    double reach = length * t;
                    double droop = length * 0.45 * t * t;
                    frond.Add(top + new Point2(Math.Cos(angle) * reach, Math.Sin(angle) * reach - droop));
                }

                g.Polyline(frond, false, heavy: true);
            }

            notes.Add((new Point2(stipe * 0.5, crownBase * 0.5), $"{species.PopularName}: muda h = {M(h)}, estipe sem danos"));
        }
        else
        {
            // Tronco e copa da muda.
            double trunk = Math.Max(0.02, h * 0.015);
            crownBase = d.Kind == DetailKind.CovaArbusto ? h * 0.12 : h * 0.5;
            g.Line(new Point2(-trunk, 0), new Point2(-trunk, crownBase), heavy: true);
            g.Line(new Point2(trunk, 0), new Point2(trunk, crownBase), heavy: true);
            double rx = Math.Max(0.15, d.Kind == DetailKind.CovaArbusto ? h * 0.45 : h * 0.22);
            double ry = (h - crownBase) / 2;
            List<Point2> crown = Drawing.Ellipse(new Point2(0, crownBase + ry), rx, ry);
            g.Fills.Add(new DrawFill(crown, foliage));
            g.Polyline(crown, true, heavy: true);
            notes.Add((new Point2(rx * 0.7, crownBase + ry * 1.3), $"{species.PopularName}: muda h = {M(h)}"));
        }

        // Tutores ou escoras, até abaixo da copa, e amarrilho em "8".
        if (d.Stakes > 0)
        {
            double above = Math.Min(d.StakeHeightM - 0.5, crownBase * 0.92);
            double x = Math.Max(0.12, d.RootBallM / 2 + 0.05);
            g.Line(new Point2(-x, -0.5), new Point2(-x, above));
            g.Line(new Point2(-x + 0.03, -0.5), new Point2(-x + 0.03, above));
            if (d.Stakes > 1)
            {
                g.Line(new Point2(x, -0.5), new Point2(x, above));
                g.Line(new Point2(x - 0.03, -0.5), new Point2(x - 0.03, above));
            }

            double tie = above * 0.85;
            g.Line(new Point2(-x, tie), new Point2(d.Stakes > 1 ? x : 0, tie), dashed: true);
            string stakes = d.Kind == DetailKind.BercoPalmeira
                ? $"{d.Stakes} escoras de madeira tratada, {M(d.StakeHeightM)}, com proteção na estipe"
                : d.Stakes == 1 ? $"Tutor de madeira tratada, {M(d.StakeHeightM)} (0,50 m cravado)" : $"{d.Stakes} tutores de madeira tratada, {M(d.StakeHeightM)} (0,50 m cravados)";
            notes.Add((new Point2(d.Stakes > 1 ? x : -x, above * 0.6), stakes));
            notes.Add((new Point2(d.Stakes > 1 ? x * 0.5 : -x * 0.5, tie), "Amarrilho em \"8\" com borracha ou sisal"));
        }

        // Coroamento com cobertura morta.
        if (d.CrownRingM > 0)
        {
            double r = d.CrownRingM / 2;
            g.Line(new Point2(-r, d.MulchM), new Point2(r, d.MulchM), dashed: true);
            g.Dimension(new Point2(-r, d.MulchM), new Point2(r, d.MulchM), -(depth + 0.35), "Coroamento Ø " + M(d.CrownRingM));
        }

        notes.Add((new Point2(Math.Max(half * 0.8, d.CrownRingM * 0.35), d.MulchM), $"Cobertura morta (casca ou folhas trituradas) {d.MulchM * 100:0} cm"));
        notes.Add((new Point2(half + 0.25, -0.06), "Solo natural descompactado nas paredes da cova"));

        g.Dimension(new Point2(-half, -depth), new Point2(half, -depth), -0.15, "Cova " + M(w));
        g.Dimension(new Point2(-half, 0), new Point2(-half, -depth), -0.15, "Prof. " + M(depth));
        g.Dimension(new Point2(-groundExtent, 0), new Point2(-groundExtent, h), 0.12, "h = " + M(h));
        g.Notes(notes, groundExtent + 0.35, NoteGap);
    }

    private static void DrawBed(Drawing g, Species species, PlantingDimensions d, Rgb foliage, Rgb soil)
    {
        double layer = d.SoilLayerM;
        double spacing = species.EffectiveSpacingM > 0 ? Math.Clamp(species.EffectiveSpacingM, 0.1, 0.8) : 0.25;
        double width = Math.Clamp(spacing * 6, 1.2, 3.0);
        g.Line(new Point2(-width / 2 - 0.35, 0), new Point2(width / 2 + 0.35, 0), heavy: true);
        var bed = new List<Point2> { new(-width / 2, 0), new(-width / 2, -layer), new(width / 2, -layer), new(width / 2, 0) };
        g.Fills.Add(new DrawFill(new List<Point2>(bed) { new(-width / 2, 0) }, soil.Tint(0.35)));
        g.Polyline(bed, false, heavy: true);

        double plantHeight = Math.Clamp(species.AdultHeightM > 0 ? species.AdultHeightM * 0.5 : 0.25, 0.08, 0.5);
        double r = Math.Min(spacing * 0.45, 0.25);
        double firstX = -width / 2 + spacing / 2;
        for (double x = firstX; x <= width / 2 - spacing / 2 + 1e-9; x += spacing)
        {
            var tuft = new List<Point2>();
            for (int k = 0; k <= 16; k++)
            {
                double t = Math.PI * k / 16;
                tuft.Add(new Point2(x + r * Math.Cos(t), d.MulchM + plantHeight * Math.Sin(t)));
            }

            g.Fills.Add(new DrawFill(tuft, foliage));
            g.Polyline(tuft, true);
        }

        g.Line(new Point2(-width / 2, d.MulchM), new Point2(width / 2, d.MulchM), dashed: true);
        g.Dimension(new Point2(-width / 2, -layer), new Point2(-width / 2, 0), 0.15, "Preparo " + M(layer));
        g.Dimension(new Point2(firstX, -layer), new Point2(firstX + spacing, -layer), -0.12, "Espaçamento " + M(spacing));
        g.Notes(new (Point2, string)[]
        {
            (new Point2(width / 2 - spacing / 2, d.MulchM + plantHeight * 0.6), $"Mudas de {species.PopularName} em torrão ou bandeja, em quincôncio"),
            (new Point2(width / 2 - 0.1, d.MulchM), $"Cobertura morta {d.MulchM * 100:0} cm até o fechamento do canteiro"),
            (new Point2(width / 2 - 0.2, -layer / 2), "Solo revolvido e corrigido: terra vegetal + composto orgânico (3:1)"),
        }, width / 2 + 0.45, NoteGap);
    }

    private static void DrawLawn(Drawing g, PlantingDimensions d, Rgb foliage, Rgb soil)
    {
        double width = 2.0;
        double layer = d.SoilLayerM;
        g.Line(new Point2(-width / 2 - 0.2, 0), new Point2(width / 2 + 0.2, 0), heavy: true);
        g.Fills.Add(new DrawFill(new List<Point2> { new(-width / 2, 0), new(-width / 2, -layer), new(width / 2, -layer), new(width / 2, 0) }, soil.Tint(0.35)));
        g.Line(new Point2(-width / 2, -layer), new Point2(width / 2, -layer), dashed: true);
        double plate = 0.03;
        for (double x = -width / 2; x < width / 2 - 1e-9; x += 0.40)
        {
            var p = new List<Point2> { new(x + 0.005, 0), new(x + 0.395, 0), new(x + 0.395, plate), new(x + 0.005, plate) };
            g.Fills.Add(new DrawFill(p, foliage));
            g.Polyline(p, true);
        }

        g.Dimension(new Point2(-width / 2, -layer), new Point2(-width / 2, 0), 0.15, "Terra " + M(layer));
        g.Dimension(new Point2(-width / 2, plate), new Point2(-width / 2 + 0.4, plate), -0.12, "Placa 0,40 m");
        g.Notes(new (Point2, string)[]
        {
            (new Point2(width / 2 - 0.2, plate), "Placas justapostas, juntas desencontradas; compactar levemente e rolar"),
            (new Point2(width / 2 - 0.3, -layer / 2), "Terra vegetal peneirada e nivelada, com adubação de base"),
            (new Point2(width / 2 - 0.4, -layer), "Subleito descompactado e livre de entulho"),
        }, width / 2 + 0.4, NoteGap);
    }
}
