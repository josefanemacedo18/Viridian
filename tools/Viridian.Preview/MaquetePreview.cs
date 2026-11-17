using System.Globalization;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Library;

/// <summary>Render isométrico simples (pintor + sombreamento plano) das malhas da maquete, para conferência visual.</summary>
internal static class MaquetePreview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record Face(Vec3 A, Vec3 B, Vec3 C, Rgb Color, bool Ground = false);

    public static string Scene(GrowthStage stage)
    {
        var colors = new ColorSettings();
        List<Species> all = SeedCatalog.CreateSpecies();
        Species Find(string n) => all.First(s => s.ScientificName == n);
        var faces = new List<Face>();
        var trunk = new Rgb(0x6B, 0x4F, 0x3A);

        void AddModel(Species s, double x, double y, double z = 0)
        {
            (double crown, double height) = GrowthModel.Size(s, stage);
            int seed = DeterministicRandom.StableHash(s.Id + x + y);
            PlantModel model = PlantModels.Build(s, crown, height, seed, !string.IsNullOrWhiteSpace(s.FlowerColor));
            Rgb foliage = VegetationPalette.Foliage(s, colors);
            foreach (MeshPiece piece in model.Pieces)
            {
                MeshPiece placed = piece.Transform(seed % 360 * Math.PI / 180, new Vec3(x, y, z));
                Rgb color = piece.Part switch { MeshPart.Tronco => trunk, MeshPart.Flor => Rgb.Parse(s.FlowerColor, Rgb.White), _ => foliage };
                faces.AddRange(placed.Triangles.Select(t => { var c = placed.Corners(t); return new Face(c.A, c.B, c.C, color); }));
            }
        }

        // Terreno levemente inclinado.
        double Ground(Point2 p) => p.Y * 0.04;
        var groundColor = new Rgb(0xD9, 0xD4, 0xC4);
        for (int i = -2; i < 32; i += 2)
        {
            for (int j = -2; j < 22; j += 2)
            {
                Vec3 P(double x, double y) => new(x, y, Ground(new Point2(x, y)) - 0.02);
                faces.Add(new Face(P(i, j), P(i + 2, j), P(i + 2, j + 2), groundColor, true));
                faces.Add(new Face(P(i, j), P(i + 2, j + 2), P(i, j + 2), groundColor, true));
            }
        }

        Species ipe = Find("Handroanthus chrysotrichus");
        AddModel(ipe, 4, 15, Ground(new Point2(4, 15)));
        AddModel(Find("Syagrus romanzoffiana"), 12, 16, Ground(new Point2(12, 16)));
        AddModel(Find("Bauhinia variegata"), 21, 15, Ground(new Point2(21, 15)));
        Species conic = all.FirstOrDefault(s => s.Category == PlantCategory.Arvore && s.CrownForm == CrownForm.Piramidal) ?? ipe;
        Species column = all.FirstOrDefault(s => s.Category == PlantCategory.Arvore && s.CrownForm == CrownForm.Colunar) ?? ipe;
        Species spread = all.FirstOrDefault(s => s.Category == PlantCategory.Arvore && s.CrownForm == CrownForm.Espalhada) ?? ipe;
        Species irregular = all.FirstOrDefault(s => s.Category == PlantCategory.Arvore && s.CrownForm == CrownForm.Irregular) ?? ipe;
        AddModel(conic, 27, 17, Ground(new Point2(27, 17)));
        AddModel(column, 27, 10, Ground(new Point2(27, 10)));
        AddModel(spread, 4, 5, Ground(new Point2(4, 5)));
        AddModel(irregular, 15, 6, Ground(new Point2(15, 6)));
        Species shrub = all.First(s => s.Category == PlantCategory.Arbusto && !string.IsNullOrEmpty(s.FlowerColor));
        for (int k = 0; k < 5; k++)
        {
            AddModel(shrub, 9 + k * 1.6, 10 + (k % 2) * 0.8, Ground(new Point2(9 + k * 1.6, 10)));
        }

        Species herb = all.First(s => s.Category == PlantCategory.Herbacea);
        for (int k = 0; k < 6; k++)
        {
            AddModel(herb, 20 + k * 0.7, 6 + (k % 2) * 0.6, Ground(new Point2(20 + k * 0.7, 6)));
        }

        // Maciço de forração com flores e gramado.
        Species cover = Find("Arachis repens");
        var bed = new Polygon2(new[] { new Point2(7, 1), new Point2(14, 1), new Point2(16, 3), new Point2(12, 4.5), new Point2(7, 3.5) });
        (_, double coverHeight) = GrowthModel.Size(cover, stage);
        MeshPiece bedMesh = BedModels.Build(bed, Ground, coverHeight);
        Rgb coverColor = VegetationPalette.Foliage(cover, colors);
        faces.AddRange(bedMesh.Triangles.Select(t => { var c = bedMesh.Corners(t); return new Face(c.A, c.B, c.C, coverColor); }));
        foreach (Vec3 p in BedModels.FlowerPoints(bedMesh, bed, 0.6, 60, 2))
        {
            MeshPiece blob = Primitives.Blob(MeshPart.Flor, p + new Vec3(0, 0, 0.03), 0.09);
            faces.AddRange(blob.Triangles.Select(t => { var c = blob.Corners(t); return new Face(c.A, c.B, c.C, Rgb.Parse(cover.FlowerColor, Rgb.White)); }));
        }

        Species grass = all.First(s => s.Category == PlantCategory.Gramado);
        var lawnOuter = new[] { new Point2(18, 0), new Point2(30, 0), new Point2(30, 4), new Point2(18, 4) };
        var lawnHole = new[] { new Point2(23, 1.5), new Point2(25, 1.5), new Point2(25, 2.5), new Point2(23, 2.5) };
        MeshPiece lawn = BedModels.Build(new Polygon2(new[] { lawnOuter, lawnHole }), Ground, 0.05);
        Rgb grassColor = VegetationPalette.Foliage(grass, colors);
        faces.AddRange(lawn.Triangles.Select(t => { var c = lawn.Corners(t); return new Face(c.A, c.B, c.C, grassColor); }));

        return Render(faces, $"Maquete 3D — {GrowthModel.Name(stage)}");
    }

    private static string Render(List<Face> faces, string title)
    {
        Vec3 toEye = new Vec3(-1.0, -1.35, 1.05).Normalized();
        Vec3 forward = toEye * -1;
        Vec3 right = forward.Cross(new Vec3(0, 0, 1)).Normalized();
        Vec3 up = right.Cross(forward).Normalized();
        Vec3 light = new Vec3(-0.5, -0.3, 0.85).Normalized();
        const double k = 26;

        var drawn = faces
            .Select(f =>
            {
                Vec3 n = (f.B - f.A).Cross(f.C - f.A).Normalized();
                Vec3 mid = (f.A + f.B + f.C) * (1.0 / 3);
                return (Face: f, Normal: n, Depth: mid.Dot(forward));
            })
            .Where(x => x.Normal.Dot(toEye) > 0)
            .OrderByDescending(x => x.Face.Ground).ThenByDescending(x => x.Depth)
            .ToList();

        (double X, double Y) P(Vec3 v) => (v.Dot(right) * k, -v.Dot(up) * k);
        var pts = drawn.SelectMany(d => new[] { P(d.Face.A), P(d.Face.B), P(d.Face.C) }).ToList();
        double minX = pts.Min(p => p.X) - 20, maxX = pts.Max(p => p.X) + 20, minY = pts.Min(p => p.Y) - 50, maxY = pts.Max(p => p.Y) + 20;
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns='http://www.w3.org/2000/svg' width='{maxX - minX:0}' height='{maxY - minY:0}' viewBox='{minX:0.#} {minY:0.#} {maxX - minX:0.#} {maxY - minY:0.#}'>");
        sb.Append(Inv, $"<rect x='{minX:0.#}' y='{minY:0.#}' width='{maxX - minX:0.#}' height='{maxY - minY:0.#}' fill='#EEF3F6'/>");
        sb.Append(Inv, $"<text x='{minX + 20:0.#}' y='{minY + 32:0.#}' font-family='Segoe UI, Arial' font-size='20' fill='#1F4D3A'>{title}</text>");
        foreach ((Face f, Vec3 n, _) in drawn)
        {
            double shade = 0.5 + 0.5 * Math.Max(0, n.Dot(light));
            Rgb c = new((byte)Math.Min(255, f.Color.R * shade), (byte)Math.Min(255, f.Color.G * shade), (byte)Math.Min(255, f.Color.B * shade));
            var a = P(f.A);
            var b = P(f.B);
            var cc = P(f.C);
            sb.Append(Inv, $"<path d='M{a.X:0.##},{a.Y:0.##}L{b.X:0.##},{b.Y:0.##}L{cc.X:0.##},{cc.Y:0.##}Z' fill='{c.Hex}' stroke='{c.Hex}' stroke-width='0.4'/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
