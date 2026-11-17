using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Model3D;

/// <summary>Forma 3D escolhida para a espécie.</summary>
public enum PlantShape
{
    ArvoreArredondada = 0,
    ArvoreEspalhada = 1,
    ArvoreColunar = 2,
    ArvoreConica = 3,
    ArvoreIrregular = 4,
    Palmeira = 5,
    Arbusto = 6,
    Touceira = 7,
    Gramado = 8,
}

/// <summary>Modelo 3D de um exemplar: partes em malha, em metros, com a base no ponto (0, 0, 0).</summary>
public sealed class PlantModel
{
    public PlantShape Shape { get; init; }

    public double HeightM { get; init; }

    public double CrownDiameterM { get; init; }

    public List<MeshPiece> Pieces { get; } = new();

    public int TriangleCount => Pieces.Sum(p => p.TriangleCount);
}

/// <summary>
/// Modelos 3D simplificados para a maquete: não paramétricos, gerados a partir da categoria, da forma da copa e do
/// porte (altura e copa) da fase escolhida. A semente torna cada exemplar um pouco diferente e sempre igual a si mesmo.
/// </summary>
public static class PlantModels
{
    /// <summary>Menor medida usada nas malhas (o Revit não aceita arestas muito curtas).</summary>
    public const double MinSize = 0.08;

    public static PlantShape ShapeOf(Species species) => species.Category switch
    {
        PlantCategory.Palmeira => PlantShape.Palmeira,
        PlantCategory.Arbusto or PlantCategory.Trepadeira => PlantShape.Arbusto,
        PlantCategory.Herbacea or PlantCategory.Forracao or PlantCategory.Aquatica => PlantShape.Touceira,
        PlantCategory.Gramado => PlantShape.Gramado,
        _ => species.CrownForm switch
        {
            CrownForm.Espalhada => PlantShape.ArvoreEspalhada,
            CrownForm.Colunar => PlantShape.ArvoreColunar,
            CrownForm.Piramidal => PlantShape.ArvoreConica,
            CrownForm.Irregular => PlantShape.ArvoreIrregular,
            _ => PlantShape.ArvoreArredondada,
        },
    };

    /// <summary>Gera o modelo. <paramref name="flower"/> nulo desliga as manchas de flor.</summary>
    public static PlantModel Build(Species species, double crownDiameterM, double heightM, long seed, bool flower)
    {
        ArgumentNullException.ThrowIfNull(species);
        PlantShape shape = ShapeOf(species);
        double crown = Clamp(crownDiameterM, MinSize, 60);
        double height = Clamp(heightM, shape == PlantShape.Gramado ? 0.03 : MinSize, 80);
        var model = new PlantModel { Shape = shape, HeightM = height, CrownDiameterM = crown };
        var random = new DeterministicRandom(seed);
        double r = crown / 2;

        // Malhas mais leves para plantas pequenas: a maquete pode ter milhares de exemplares.
        int segments = crown >= 4 ? 14 : crown >= 1.5 ? 11 : 8;
        int rings = crown >= 4 ? 9 : crown >= 1.5 ? 7 : 5;

        switch (shape)
        {
            case PlantShape.Palmeira:
                BuildPalm(model, r, height, random);
                break;

            case PlantShape.Arbusto:
            {
                MeshPiece dome = Primitives.Dome(MeshPart.Folhagem, Vec3.Zero, r, r * random.Range(0.85, 1.0), height, segments, Math.Max(3, rings / 2 + 1), 0.08, random.NextInt(1, int.MaxValue));
                model.Pieces.Add(dome);
                if (flower)
                {
                    AddFlowers(model, new Vec3(0, 0, 0), r, height, hemisphere: true, random);
                }

                break;
            }

            case PlantShape.Touceira:
            {
                model.Pieces.Add(Primitives.Dome(MeshPart.Folhagem, Vec3.Zero, r, r, height, Math.Max(6, segments - 2), 3, 0.1, random.NextInt(1, int.MaxValue)));
                if (flower)
                {
                    AddFlowers(model, Vec3.Zero, r, height, hemisphere: true, random);
                }

                break;
            }

            case PlantShape.Gramado:
                model.Pieces.Add(Primitives.Frustum(MeshPart.Folhagem, Vec3.Zero, height, r, r * 0.92, Math.Max(8, segments)));
                break;

            default:
                BuildTree(model, shape, r, height, segments, rings, random, flower);
                break;
        }

        return model;
    }

    private static void BuildTree(PlantModel model, PlantShape shape, double r, double h, int segments, int rings, DeterministicRandom random, bool flower)
    {
        // Proporções médias de cada forma: altura do fuste (início da copa) e altura da copa.
        double crownBase = shape switch
        {
            PlantShape.ArvoreEspalhada => 0.45,
            PlantShape.ArvoreColunar or PlantShape.ArvoreConica => 0.12,
            PlantShape.ArvoreIrregular => 0.35,
            _ => 0.33,
        } * h;
        crownBase = Math.Max(Math.Min(crownBase, h - MinSize * 2), 0);
        double crownHeight = Math.Max(MinSize * 2, h - crownBase);
        double trunkRadius = Clamp(h * 0.018, 0.04, 0.45);
        if (shape == PlantShape.ArvoreColunar)
        {
            r = Math.Min(r, Math.Max(MinSize, crownHeight * 0.32));
        }

        trunkRadius = Math.Min(trunkRadius, Math.Max(0.03, r * 0.3));
        double trunkHeight = crownBase + crownHeight * (shape == PlantShape.ArvoreConica ? 0.2 : 0.35);
        model.Pieces.Add(Primitives.Frustum(MeshPart.Tronco, Vec3.Zero, trunkHeight, trunkRadius, trunkRadius * 0.7, 8));

        double rz = crownHeight / 2;
        var center = new Vec3(0, 0, crownBase + rz);
        long Seed() => random.NextInt(1, int.MaxValue);
        switch (shape)
        {
            case PlantShape.ArvoreConica:
                model.Pieces.Add(Primitives.Frustum(MeshPart.Folhagem, new Vec3(0, 0, crownBase), crownHeight, r, 0, Math.Max(8, segments)));
                if (flower)
                {
                    AddFlowers(model, new Vec3(0, 0, crownBase), r * 0.6, crownHeight * 0.75, hemisphere: true, random);
                }

                return;

            case PlantShape.ArvoreIrregular:
            {
                model.Pieces.Add(Primitives.Ellipsoid(MeshPart.Folhagem, center, r * 0.75, r * 0.7, rz * 0.85, segments, rings, 0.08, Seed()));
                double angle = random.Range(0, Geo.TwoPi);
                for (int i = 0; i < 2; i++)
                {
                    double a = angle + i * random.Range(2.2, 3.6);
                    var lobe = center + new Vec3(Math.Cos(a) * r * 0.45, Math.Sin(a) * r * 0.45, random.Range(-0.2, 0.2) * rz);
                    model.Pieces.Add(Primitives.Ellipsoid(MeshPart.Folhagem, lobe, r * 0.55, r * 0.5, rz * 0.6, Math.Max(8, segments - 2), Math.Max(5, rings - 2), 0.08, Seed()));
                }

                break;
            }

            default:
                model.Pieces.Add(Primitives.Ellipsoid(MeshPart.Folhagem, center, r, r * random.Range(0.9, 1.0), rz, segments, rings, 0.07, Seed()));
                break;
        }

        if (flower)
        {
            AddFlowers(model, center, r, rz, hemisphere: false, random);
        }
    }

    private static void BuildPalm(PlantModel model, double r, double h, DeterministicRandom random)
    {
        double frondLength = Math.Max(0.5, r * 1.05);
        double top = Math.Max(MinSize * 2, h - frondLength * 0.15);
        double stipe = Clamp(h * 0.022, 0.06, 0.35);
        model.Pieces.Add(Primitives.Frustum(MeshPart.Tronco, Vec3.Zero, top, stipe * 1.15, stipe, 8));

        // Capitel (base das folhas) e folhas arqueadas em leque.
        model.Pieces.Add(Primitives.Ellipsoid(MeshPart.Folhagem, new Vec3(0, 0, top), stipe * 1.6, stipe * 1.6, Math.Max(MinSize, stipe * 2.2), 8, 5));
        int fronds = r >= 2.5 ? 12 : r >= 1.2 ? 10 : 8;
        double start = random.Range(0, Geo.TwoPi);
        const int steps = 6;
        for (int f = 0; f < fronds; f++)
        {
            double angle = start + Geo.TwoPi * f / fronds + random.Range(-0.15, 0.15);
            double lift = random.Range(0.55, 0.95);
            double length = frondLength * random.Range(0.85, 1.05);
            var path = new List<Vec3>(steps + 1);
            var widths = new List<double>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double horizontal = length * t;
                double z = top + length * (lift * t - 1.1 * t * t);
                path.Add(new Vec3(Math.Cos(angle) * horizontal, Math.Sin(angle) * horizontal, z));
                widths.Add(Math.Max(0.03, length * 0.2 * Math.Sin(Math.PI * (0.12 + 0.88 * t))));
            }

            model.Pieces.Add(Primitives.Blade(MeshPart.Folhagem, path, widths, Math.Max(0.02, length * 0.015)));
        }
    }

    private static void AddFlowers(PlantModel model, Vec3 center, double r, double rz, bool hemisphere, DeterministicRandom random)
    {
        int count = (int)Clamp(Math.Round(r * 4), 4, 14);
        double blob = Clamp(r * 0.14, 0.04, 0.4);
        for (int i = 0; i < count; i++)
        {
            double theta = random.Range(0, Geo.TwoPi);
            double phi = Geo.DegToRad(hemisphere ? random.Range(25, 70) : random.Range(5, 65));
            var p = center + new Vec3(r * Math.Cos(phi) * Math.Cos(theta), r * Math.Cos(phi) * Math.Sin(theta), rz * Math.Sin(phi));
            model.Pieces.Add(Primitives.Blob(MeshPart.Flor, p, blob, theta));
        }
    }

    private static double Clamp(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
