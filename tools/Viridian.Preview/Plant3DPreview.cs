using System.Globalization;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;
using Viridian.Library;

/// <summary>Pranchas de conferência do motor 3D botânico: perfis, níveis de qualidade, estações e espécies de referência.</summary>
internal static class Plant3DPreview
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly List<Species> Catalog = SeedCatalog.CreateSpecies();
    private static readonly List<Plant3DProfile> Profiles = Plant3DProfileDefaults.Create();
    private static readonly ColorSettings ColorConfig = new();

    private sealed record Face(Vec3 A, Vec3 B, Vec3 C, Rgb Color, bool Open);

    private sealed record Cell(List<Face> Faces, string Title, string Subtitle, double Height, double Radius);

    /// <summary>Todos os perfis no nível Intermediário, folhagem cheia e flores.</summary>
    public static string ProfilesSheet()
    {
        var cells = new List<Cell>();
        foreach (Plant3DProfile p in Profiles)
        {
            Species? s = SampleSpecies(p);
            (double h, double d) = Typical(p, s);
            var request = new PlantRequest { Profile = p, HeightM = h, CrownDiameterM = d, Quality = QualityLevel.Intermediaria, Seed = 11, Flowers = true };
            cells.Add(Make(request, s, $"{p.Name}", $"{ProfileLabels.Of(p.Architecture)} · {h:0.#} m"));
        }

        return Sheet(cells, 8, 250, 290, "Perfis 3D botânicos — nível Intermediário", fitEach: true);
    }

    /// <summary>Algumas plantas nos três níveis de qualidade, lado a lado, na mesma escala.</summary>
    public static string LodSheet() => LodSheet("Níveis de qualidade (Simplificada · Intermediária · Detalhada)", "ipe", "jeriva", "agave", "estrelitzia", "arbusto-arredondado", "araucaria");

    /// <summary>Perfis de forma mais difícil (palmeiras em leque e em touceira, cônicas, eretos, forrações, trepadeiras).</summary>
    public static string FocusSheet() => LodSheet("Perfis de forma especial nos três níveis", "palmeira-leque", "palmeira-touceira", "arvore-conica", "arbusto-ereto", "forracao", "trepadeira");

    private static string LodSheet(string title, params string[] ids)
    {
        var cells = new List<Cell>();
        foreach (string id in ids)
        {
            Plant3DProfile p = Profiles.Single(x => x.Id == id);
            Species? s = SampleSpecies(p);
            (double h, double d) = Typical(p, s);
            foreach (QualityLevel q in Enum.GetValues<QualityLevel>())
            {
                var request = new PlantRequest { Profile = p, HeightM = h, CrownDiameterM = d, Quality = q, Seed = 5, Flowers = true };
                GeneratedPlant g = PlantGeometryGenerator.Generate(request);
                cells.Add(Make(request, s, $"{p.Name} — {ProfileLabels.Of(q)}", $"{g.Model.TriangleCount} de {g.Budget} triângulos"));
            }
        }

        return Sheet(cells, 6, 270, 300, title, fitEach: true);
    }

    /// <summary>Mesma espécie ao longo do ano: folhagem, floração e frutos conforme o mês.</summary>
    public static string SeasonSheet()
    {
        var cells = new List<Cell>();
        (string Name, int[] Months)[] rows =
        {
            ("Handroanthus chrysotrichus", new[] { 1, 6, 8, 10 }),
            ("Delonix regia", new[] { 1, 7, 10, 12 }),
            ("Syagrus romanzoffiana", new[] { 1, 4, 8, 11 }),
        };
        foreach ((string name, int[] months) in rows)
        {
            Species? s = Catalog.FirstOrDefault(x => x.ScientificName == name);
            if (s is null)
            {
                continue;
            }

            (Plant3DProfile p, _) = ProfileResolver.Resolve(s, Profiles);
            (double d, double h) = GrowthModel.Size(s, GrowthStage.Maturidade);
            foreach (int month in months)
            {
                var options = new PlantOptions { Quality = QualityLevel.Intermediaria, Month = month, Flowers = true, Fruits = true };
                PlantSeason season = PlantPlanner.Season(s, p, options);
                PlantRequest request = PlantPlanner.Request(s, p, d, h, 3, options);
                string what = season.Foliage switch { FoliageState.SemFolhas => "sem folhas", FoliageState.Rala => "folhagem rala", _ => "folhagem cheia" }
                              + (season.Flowers ? " · flores" : string.Empty) + (season.Fruits is not null ? " · frutos" : string.Empty);
                cells.Add(Make(request, s, $"{s.PopularName} — {MonthSet.Abbreviations[month - 1]}", what, season.Fruits));
            }
        }

        return Sheet(cells, 4, 300, 320, "Sazonalidade por mês de referência (hemisfério sul)", fitEach: false);
    }

    /// <summary>Espécies de referência no nível Detalhado, como a maquete as gera (perfil, porte da fase, cores da ficha).</summary>
    public static string ReferenceSheet()
    {
        string[] names =
        {
            "Handroanthus chrysotrichus", "Delonix regia", "Syagrus romanzoffiana", "Roystonea oleracea", "Agave attenuata",
            "Strelitzia reginae", "Araucaria angustifolia", "Neoregelia carolinae", "Dracaena marginata", "Pennisetum setaceum",
        };
        var cells = new List<Cell>();
        foreach (string name in names)
        {
            Species? s = Catalog.FirstOrDefault(x => x.ScientificName == name) ?? Catalog.FirstOrDefault(x => x.ScientificName.StartsWith(name.Split(' ')[0] + " ", StringComparison.Ordinal));
            if (s is null)
            {
                continue;
            }

            (Plant3DProfile p, _) = ProfileResolver.Resolve(s, Profiles);
            (double d, double h) = GrowthModel.Size(s, GrowthStage.Maturidade);
            var options = new PlantOptions { Quality = QualityLevel.Detalhada, Flowers = true };
            PlantRequest request = PlantPlanner.Request(s, p, d, h, 21, options);
            GeneratedPlant g = PlantGeometryGenerator.Generate(request);
            cells.Add(Make(request, s, s.PopularName, $"{s.ScientificName} · {p.Name} · {g.Model.TriangleCount} tri"));
        }

        return Sheet(cells, 5, 330, 360, "Espécies de referência — nível Detalhado", fitEach: true);
    }

    private static Species? SampleSpecies(Plant3DProfile p) =>
        Catalog.FirstOrDefault(s => ProfileResolver.Resolve(s, Profiles).Profile.Id == p.Id && !string.IsNullOrWhiteSpace(s.FlowerColor))
        ?? Catalog.FirstOrDefault(s => ProfileResolver.Resolve(s, Profiles).Profile.Id == p.Id);

    private static (double Height, double Crown) Typical(Plant3DProfile p, Species? s)
    {
        if (s is not null)
        {
            (double crown, double height) = GrowthModel.Size(s, GrowthStage.Maturidade);
            return (height, crown);
        }

        return p.Architecture switch
        {
            PlantArchitecture.ArvoreColunar => (12, 3),
            PlantArchitecture.ArvoreConica => (14, 6),
            >= PlantArchitecture.ArvoreArredondada and <= PlantArchitecture.ArvoreIrregular => (9, 8),
            PlantArchitecture.PalmeiraEstipeUnico => (10, 5),
            PlantArchitecture.PalmeiraTouceira => (5, 4),
            PlantArchitecture.Forracao => (0.2, 0.5),
            PlantArchitecture.Gramado => (0.05, 1),
            PlantArchitecture.AquaticaFlutuante => (0.1, 1),
            _ => (1.4, 1.2),
        };
    }

    private static Cell Make(PlantRequest request, Species? species, string title, string subtitle, FruitTraits? fruits = null)
    {
        GeneratedPlant g = PlantGeometryGenerator.Generate(request);
        PlantColors colors = species is null
            ? new PlantColors(new Rgb(0x4E, 0x7D, 0x3A), PlantColors.DefaultTrunk, new Rgb(0xE8, 0xC2, 0x30), new Rgb(0xC0, 0x30, 0x20))
            : PlantPlanner.Colors(species, request.Profile, ColorConfig, "#6B4F3A", fruits ?? request.Fruits);
        var faces = new List<Face>();
        foreach (MeshPiece piece in g.Model.Pieces)
        {
            Rgb color = colors.For(piece.Part);
            foreach (Tri t in piece.Triangles)
            {
                (Vec3 a, Vec3 b, Vec3 c) = piece.Corners(t);
                faces.Add(new Face(a, b, c, color, !piece.Closed));
            }
        }

        return new Cell(faces, title, subtitle, g.Model.HeightM, g.Model.CrownDiameterM / 2);
    }

    private static string Sheet(List<Cell> cells, int columns, double cellW, double cellH, string title, bool fitEach)
    {
        int rows = (cells.Count + columns - 1) / columns;
        double width = columns * cellW + 40, height = rows * cellH + 90;
        var sb = new StringBuilder();
        sb.Append(Inv, $"<svg xmlns='http://www.w3.org/2000/svg' width='{width:0}' height='{height:0}' viewBox='0 0 {width:0} {height:0}'>");
        sb.Append(Inv, $"<rect width='{width:0}' height='{height:0}' fill='#F4F6F2'/>");
        sb.Append(Inv, $"<text x='20' y='40' font-family='Segoe UI, Arial' font-size='24' fill='#1F4D3A'>{Esc(title)}</text>");

        // Na prancha sazonal, todas as células usam a mesma escala (compara o porte).
        double? shared = fitEach ? null : cells.Min(c => FitScale(c, cellW, cellH));
        for (int i = 0; i < cells.Count; i++)
        {
            double x = 20 + (i % columns) * cellW, y = 60 + (i / columns) * cellH;
            Draw(sb, cells[i], x, y, cellW, cellH, shared);
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static readonly Vec3 ToEye = new Vec3(-1.0, -1.3, 0.62).Normalized();
    private static readonly Vec3 Forward = ToEye * -1;
    private static readonly Vec3 Right = Forward.Cross(new Vec3(0, 0, 1)).Normalized();
    private static readonly Vec3 UpView = Right.Cross(Forward).Normalized();
    private static readonly Vec3 Light = new Vec3(-0.45, -0.25, 0.86).Normalized();

    private static (double X, double Y) Project(Vec3 v) => (v.Dot(Right), -v.Dot(UpView));

    private static double FitScale(Cell cell, double w, double h)
    {
        var pts = cell.Faces.SelectMany(f => new[] { Project(f.A), Project(f.B), Project(f.C) }).ToList();
        if (pts.Count == 0)
        {
            return 1;
        }

        double bw = Math.Max(0.01, pts.Max(p => p.X) - pts.Min(p => p.X));
        double bh = Math.Max(0.01, pts.Max(p => p.Y) - pts.Min(p => p.Y));
        return Math.Min((w - 30) / bw, (h - 70) / bh);
    }

    private static void Draw(StringBuilder sb, Cell cell, double x, double y, double w, double h, double? sharedScale)
    {
        sb.Append(Inv, $"<rect x='{x + 4:0.#}' y='{y + 4:0.#}' width='{w - 8:0.#}' height='{h - 8:0.#}' rx='10' fill='#FFFFFF' stroke='#DDE3DA'/>");
        double k = sharedScale ?? FitScale(cell, w, h);
        var pts = cell.Faces.SelectMany(f => new[] { Project(f.A), Project(f.B), Project(f.C) }).ToList();
        if (pts.Count == 0)
        {
            return;
        }

        double cx = (pts.Max(p => p.X) + pts.Min(p => p.X)) / 2;
        double bottom = pts.Max(p => p.Y);
        double ox = x + w / 2 - cx * k, oy = y + h - 52 - bottom * k;
        (double X, double Y) P(Vec3 v)
        {
            (double px, double py) = Project(v);
            return (ox + px * k, oy + py * k);
        }

        // Sombra no chão (elipse sob a copa).
        (double gx, double gy) = P(Vec3.Zero);
        double rx = Math.Max(4, cell.Radius * k * 0.95);
        sb.Append(Inv, $"<ellipse cx='{gx:0.#}' cy='{gy:0.#}' rx='{rx:0.#}' ry='{rx * 0.42:0.#}' fill='#000' fill-opacity='0.08'/>");

        var drawn = cell.Faces
            .Select(f =>
            {
                Vec3 n = (f.B - f.A).Cross(f.C - f.A).Normalized();
                double facing = n.Dot(ToEye);
                if (f.Open && facing < 0)
                {
                    n = n * -1; // superfície aberta: os dois lados aparecem (como no Revit)
                    facing = -facing;
                }

                return (Face: f, Normal: n, Facing: facing, Depth: ((f.A + f.B + f.C) * (1.0 / 3)).Dot(Forward));
            })
            .Where(d => d.Facing > -1e-9)
            .OrderByDescending(d => d.Depth)
            .ToList();
        foreach ((Face f, Vec3 n, _, _) in drawn)
        {
            double shade = 0.5 + 0.55 * Math.Max(0, n.Dot(Light));
            Rgb c = new((byte)Math.Min(255, f.Color.R * shade), (byte)Math.Min(255, f.Color.G * shade), (byte)Math.Min(255, f.Color.B * shade));
            (double ax, double ay) = P(f.A);
            (double bx, double by) = P(f.B);
            (double qx, double qy) = P(f.C);
            sb.Append(Inv, $"<path d='M{ax:0.#},{ay:0.#}L{bx:0.#},{by:0.#}L{qx:0.#},{qy:0.#}Z' fill='{c.Hex}' stroke='{c.Hex}' stroke-width='0.35' stroke-linejoin='round'/>");
        }

        sb.Append(Inv, $"<text x='{x + 16:0.#}' y='{y + h - 30:0.#}' font-family='Segoe UI, Arial' font-size='13' font-weight='600' fill='#1F4D3A'>{Esc(cell.Title)}</text>");
        sb.Append(Inv, $"<text x='{x + 16:0.#}' y='{y + h - 14:0.#}' font-family='Segoe UI, Arial' font-size='11' fill='#5F6F66'>{Esc(cell.Subtitle)}</text>");
    }

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
