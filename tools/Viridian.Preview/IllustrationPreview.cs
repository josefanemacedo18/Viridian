using System.Diagnostics;
using System.Globalization;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Illustration;
using Viridian.Core.Model3D.Profiles;
using Viridian.Library;

/// <summary>
/// Vegetação ilustrada (RPC): para cada espécie do piloto, as 16 imagens em volta e a de cima da fase adulta, as
/// fases, os estilos de vista e cenas 3D simuladas com a mesma regra de posicionamento usada no Revit.
/// </summary>
internal static class IllustrationPreview
{
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        List<Species> catalog = SeedCatalog.CreateSpecies();
        List<Plant3DProfile> profiles = Plant3DProfileDefaults.Create();
        var sprites = new Dictionary<string, PlantSprites>();
        foreach (string name in PlantSpritesPlanner.Showcase)
        {
            Species? s = catalog.FirstOrDefault(x => string.Equals(x.ScientificName, name, StringComparison.OrdinalIgnoreCase));
            if (s is null)
            {
                Console.WriteLine("Não está no catálogo: " + name);
                continue;
            }

            var watch = Stopwatch.StartNew();
            PlantSprites plant = PlantSpritesPlanner.Build(s, 0, profiles, new ColorSettings(), "#6B4F3A");
            sprites[name] = plant;
            string slug = name.ToLowerInvariant().Replace(' ', '-');
            SpriteStage adult = plant.StageFor(PlantSpritesPlanner.DefaultStage);
            double size = Math.Max(adult.HeightM, adult.CrownM);
            int triangles = 0;
            for (int v = 0; v < PlantSprites.SideViews; v++)
            {
                SpriteMesh mesh = plant.Get(adult.Code, v);
                triangles = Math.Max(triangles, mesh.TriangleCount);
                File.WriteAllText(Path.Combine(folder, $"{slug}-adulta-v{v:00}.svg"), CardSvg.Mesh(mesh, size));
            }

            SpriteMesh top = plant.Get(adult.Code, PlantSprites.TopView);
            File.WriteAllText(Path.Combine(folder, $"{slug}-adulta-topo.svg"), CardSvg.Mesh(top, Math.Max(top.MaxX - top.MinX, top.MaxY - top.MinY) * 1.05, centerVertically: true));
            File.WriteAllText(Path.Combine(folder, $"{slug}-adulta-linha.svg"), CardSvg.Mesh(plant.Get(adult.Code, 2), size, style: "linha"));
            File.WriteAllText(Path.Combine(folder, $"{slug}-adulta-arame.svg"), CardSvg.Mesh(plant.Get(adult.Code, 2), size, style: "arame"));
            foreach (SpriteStage stage in plant.Stages)
            {
                File.WriteAllText(Path.Combine(folder, $"{slug}-fase{stage.Code}.svg"), CardSvg.Mesh(plant.Get(stage.Code, 2), size));
            }

            Console.WriteLine($"{name}: {plant.Stages.Count} fases, até {triangles} triângulos por imagem, {watch.ElapsedMilliseconds} ms (17 imagens adultas + fases), hash {plant.Hash}");
        }

        // Cenas: um jardim visto como numa vista 3D do Revit (isométrica de dois lados, perspectiva à altura dos olhos
        // e quase de cima). As mesmas imagens, sempre de frente para a câmera.
        var layout = new List<(string Name, double X, double Y, double Rot, int Stage)>
        {
            ("Handroanthus chrysotrichus", 4, 10, 0.3, 4), ("Delonix regia", 15, 11, 1.2, 4), ("Syagrus romanzoffiana", 22, 4, 0.0, 4),
            ("Syagrus romanzoffiana", 24.5, 7, 2.0, 3), ("Bismarckia nobilis", 9, 3, 0.7, 4), ("Araucaria angustifolia", 26, 13, 0.1, 4),
            ("Agave attenuata", 12.5, 1.6, 0.4, 4), ("Agave attenuata", 13.6, 2.3, 1.9, 4), ("Agave attenuata", 14.5, 1.4, 3.0, 4),
            ("Strelitzia reginae", 3, 2.5, 0.2, 4), ("Strelitzia reginae", 4.4, 3.1, 1.4, 4), ("Dracaena marginata", 18, 2.5, 0.9, 4),
            ("Handroanthus chrysotrichus", 9, 13, 2.2, 2), ("Dracaena marginata", 19.5, 3.4, 2.6, 3),
        };
        var cameras = new List<(string File, Vec3 Eye, Vec3 Target, bool Perspective)>
        {
            ("cena-iso-sudeste.svg", new Vec3(48, -26, 30), new Vec3(14, 8, 2), false),
            ("cena-iso-noroeste.svg", new Vec3(-20, 42, 30), new Vec3(14, 8, 2), false),
            ("cena-perspectiva.svg", new Vec3(16, -14, 1.7), new Vec3(15, 8, 4), true),
            ("cena-quase-de-cima.svg", new Vec3(18, -10, 46), new Vec3(14, 8, 0), false),
        };
        // Maciços: gramado, forração e canteiro de arbustos (tapete + plantas ilustradas).
        var beds = new List<(Species Species, Polygon2 Polygon)>();
        void Bed(string popular, params Point2[] ring)
        {
            Species? sp = catalog.FirstOrDefault(x => x.PopularName == popular);
            if (sp is not null)
            {
                beds.Add((sp, new Polygon2(ring)));
            }
        }

        Bed("Grama-esmeralda", new(-1.5, -2.5), new(29, -2.5), new(29, 0.2), new(-1.5, 0.2));
        Bed("Grama-esmeralda", new(-1.5, 5.5), new(12, 5.5), new(12, 8.2), new(-1.5, 8.2));
        Bed("Grama-amendoim", new(1, 0.8), new(7.5, 0.8), new(7.5, 4.6), new(1, 4.6));
        Bed("Moreia", new(19.5, 8.5), new(24, 8.5), new(25, 11.5), new(19.5, 12));
        Bed("Azaleia", new(10, 14.5), new(17, 14.5), new(17, 17), new(10, 17));
        foreach ((string file, Vec3 eye, Vec3 target, bool perspective) in cameras)
        {
            File.WriteAllText(Path.Combine(folder, Path.ChangeExtension(file, ".tri")), Scene(layout, sprites, beds, profiles, eye, target, perspective));
        }
    }

    /// <summary>Todas as espécies do catálogo: imagem adulta (direção 2) e de cima, com tempo e triângulos.</summary>
    public static void Catalog(string folder)
    {
        Directory.CreateDirectory(folder);
        List<Species> catalog = SeedCatalog.CreateSpecies();
        List<Plant3DProfile> profiles = Plant3DProfileDefaults.Create();
        var lines = new List<string> { "categoria;codigo;nome;cientifico;ms;triangulos;topo;erro" };
        foreach (Species s in catalog.OrderBy(x => x.Category).ThenBy(x => x.Code))
        {
            var watch = Stopwatch.StartNew();
            try
            {
                PlantSprites plant = PlantSpritesPlanner.Build(s, 0, profiles, new ColorSettings(), "#6B4F3A");
                SpriteStage adult = plant.StageFor(PlantSpritesPlanner.DefaultStage);
                SpriteMesh side = plant.Get(adult.Code, 2);
                SpriteMesh top = plant.Get(adult.Code, PlantSprites.TopView);
                double size = Math.Max(adult.HeightM, adult.CrownM);
                File.WriteAllText(Path.Combine(folder, $"{(int)s.Category}-{s.Code}.svg"), CardSvg.Mesh(side, size));
                lines.Add(string.Join(";", s.Category, s.Code, s.PopularName, s.ScientificName, watch.ElapsedMilliseconds, side.TriangleCount, top.TriangleCount, ""));
            }
            catch (Exception ex)
            {
                lines.Add(string.Join(";", s.Category, s.Code, s.PopularName, s.ScientificName, watch.ElapsedMilliseconds, 0, 0, ex.GetType().Name + ": " + ex.Message.Replace(';', ',')));
            }
        }

        File.WriteAllLines(Path.Combine(folder, "catalogo.csv"), lines);
        Console.WriteLine($"{lines.Count - 1} espécies; erros: {lines.Count(l => l.EndsWith(";", StringComparison.Ordinal) == false) - 1}");
    }

    private static string Scene(List<(string Name, double X, double Y, double Rot, int Stage)> layout, Dictionary<string, PlantSprites> sprites,
        List<(Species Species, Polygon2 Polygon)> beds, List<Plant3DProfile> profiles, Vec3 eye, Vec3 target, bool perspective)
    {
        const int w = 1400, h = 900;
        Vec3 dir = (target - eye).Normalized();
        Vec3 right = dir.Cross(new Vec3(0, 0, 1)).Normalized();
        Vec3 up = right.Cross(dir);
        double focal = perspective ? w * 0.75 : 0;
        double ortho = 40.0 / w; // metros por pixel na isométrica
        (double X, double Y, double D) Project(Vec3 p)
        {
            Vec3 v = p - eye;
            double x = v.Dot(right), y = v.Dot(up), d = v.Dot(dir);
            return perspective
                ? (w / 2 + focal * x / Math.Max(d, 0.05), h / 2 - focal * y / Math.Max(d, 0.05), d)
                : (w / 2 + x / ortho, h / 2 - y / ortho, d);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{w} {h}\n");
        Vec3[] lot = { new(-2, -3, 0), new(30, -3, 0), new(30, 18, 0), new(-2, 18, 0) };
        sb.Append('P');
        foreach (Vec3 corner in lot)
        {
            (double x, double y, double _) = Project(corner);
            sb.Append(CultureInfo.InvariantCulture, $" {x:0.#} {y:0.#}");
        }

        sb.Append(" #D9D4C7\n");
        var plants = new List<(double Depth, SpriteFrame Frame, SpriteMesh Mesh)>();
        int bedIndex = 0;
        foreach ((Species species, Polygon2 polygon) in beds)
        {
            var record = new Viridian.Core.Massing.MassingRecord { Id = "M" + bedIndex++, Seed = 11 + bedIndex };
            BedVisual visual = BedIllustration.Build(polygon, species, record, new ColorSettings(), true, true);
            FlatMesh carpet = visual.Carpet;
            for (int i = 0; i + 2 < carpet.Triangles.Count; i += 3)
            {
                var pts = new (double X, double Y, double D)[3];
                for (int k = 0; k < 3; k++)
                {
                    SpriteVertex v = carpet.Vertices[carpet.Triangles[i + k]];
                    pts[k] = Project(new Vec3(v.X, v.Y, v.Z));
                }

                string color = carpet.Vertices[carpet.Triangles[i]].Color.Hex;
                sb.Append(CultureInfo.InvariantCulture, $"P {pts[0].X:0.##} {pts[0].Y:0.##} {pts[1].X:0.##} {pts[1].Y:0.##} {pts[2].X:0.##} {pts[2].Y:0.##} {color}\n");
            }

            if (visual.Points.Count > 0)
            {
                PlantSprites bedSprites = PlantSpritesPlanner.Build(species, 0, profiles, new ColorSettings(), "#6B4F3A");
                SpriteStage stage = bedSprites.StageFor(PlantSpritesPlanner.DefaultStage);
                for (int i = 0; i < visual.Points.Count; i++)
                {
                    Point2 p = visual.Points[i];
                    SpriteFrame frame = SpriteBillboard.Place(new Vec3(p.X, p.Y, 0), visual.Rotations[i], stage.HeightM, eye, (target - eye).Normalized(), up, perspective, 0.08);
                    plants.Add((Project(new Vec3(p.X, p.Y, stage.HeightM / 2)).D, frame, bedSprites.Get(stage.Code, frame.View)));
                }
            }
        }

        foreach ((string name, double px, double py, double rot, int stageCode) in layout)
        {
            if (!sprites.TryGetValue(name, out PlantSprites? plant))
            {
                continue;
            }

            SpriteStage stage = plant.StageFor(stageCode);
            SpriteFrame frame = SpriteBillboard.Place(new Vec3(px, py, 0), rot, stage.HeightM, eye, dir, up, perspective, 0.08);
            SpriteMesh mesh = plant.Get(stage.Code, frame.View);
            plants.Add((Project(new Vec3(px, py, stage.HeightM / 2)).D, frame, mesh));
        }

        foreach ((double _, SpriteFrame frame, SpriteMesh mesh) in plants.OrderByDescending(p => p.Depth))
        {
            var tris = new List<(double Z, int I)>();
            for (int i = 0; i + 2 < mesh.Triangles.Count; i += 3)
            {
                tris.Add((mesh.Vertices[mesh.Triangles[i]].Z, i));
            }

            foreach ((double _, int i) in tris.OrderBy(t => t.Z).ThenBy(t => t.I))
            {
                var pts = new (double X, double Y, double D)[3];
                for (int k = 0; k < 3; k++)
                {
                    SpriteVertex v = mesh.Vertices[mesh.Triangles[i + k]];
                    pts[k] = Project(frame.ToWorld(v.X, v.Y, v.Z));
                }

                string color = mesh.Vertices[mesh.Triangles[i]].Color.Hex;
                sb.Append(CultureInfo.InvariantCulture, $"P {pts[0].X:0.##} {pts[0].Y:0.##} {pts[1].X:0.##} {pts[1].Y:0.##} {pts[2].X:0.##} {pts[2].Y:0.##} {color}\n");
            }
        }

        return sb.ToString();
    }
}
