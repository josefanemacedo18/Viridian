using System.Collections.Concurrent;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Generation;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Uma fase da planta ilustrada: código (valor do parâmetro "VIR Fase 3D"), nome e porte (m).</summary>
public sealed record SpriteStage(int Code, string Label, double HeightM, double CrownM);

/// <summary>
/// A vegetação ilustrada de uma espécie e variante, como um RPC: para cada fase, uma imagem por direção de quem olha
/// (16 em volta da planta, de 22,5° em 22,5°) e uma vista de cima. Quem desenha escolhe a imagem pelo ângulo da
/// câmera e a mantém sempre de frente. As imagens são feitas sob demanda e guardadas; a classe pode ser usada por
/// várias linhas de execução ao mesmo tempo.
/// </summary>
public sealed class PlantSprites
{
    /// <summary>Imagens em volta da planta.</summary>
    public const int SideViews = 16;

    /// <summary>Índice da vista de cima.</summary>
    public const int TopView = -1;

    private readonly ConcurrentDictionary<(int Code, int View), Lazy<SpriteMesh>> _meshes = new();
    private readonly Func<int, int, SpriteMesh> _build;

    internal PlantSprites(string hash, IReadOnlyList<SpriteStage> stages, Func<int, int, SpriteMesh> build)
    {
        Hash = hash;
        Stages = stages;
        _build = build;
    }

    /// <summary>Assinatura do desenho (muda quando a espécie, as cores ou o estilo mudam).</summary>
    public string Hash { get; }

    public IReadOnlyList<SpriteStage> Stages { get; }

    /// <summary>A fase pedida pelo exemplar; se a espécie não a tem (por exemplo, "sem folhas" numa perene), a adulta.</summary>
    public SpriteStage StageFor(int code) =>
        Stages.FirstOrDefault(s => s.Code == code)
        ?? Stages.FirstOrDefault(s => s.Code == PlantSpritesPlanner.DefaultStage)
        ?? Stages[^1];

    /// <summary>Índice da imagem para quem olha da direção <paramref name="azimuth"/> (radianos, no sistema da família).</summary>
    public static int ViewIndex(double azimuth)
    {
        double step = Geo.TwoPi / SideViews;
        int index = (int)Math.Round(azimuth / step) % SideViews;
        return index < 0 ? index + SideViews : index;
    }

    /// <summary>Direção de quem olha a imagem <paramref name="view"/> (radianos).</summary>
    public static double ViewAzimuth(int view) => view * Geo.TwoPi / SideViews;

    /// <summary>A imagem (calcula na primeira vez; as chamadas seguintes devolvem a mesma).</summary>
    public SpriteMesh Get(int code, int view)
    {
        int stage = StageFor(code).Code;
        return _meshes.GetOrAdd((stage, view), key => new Lazy<SpriteMesh>(() => _build(key.Code, key.View), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>A imagem, se já estiver pronta (não calcula).</summary>
    public SpriteMesh? TryGet(int code, int view)
    {
        int stage = StageFor(code).Code;
        return _meshes.TryGetValue((stage, view), out Lazy<SpriteMesh>? lazy) && lazy.IsValueCreated ? lazy.Value : null;
    }

    /// <summary>Uma imagem pronta próxima da pedida (para mostrar algo enquanto a exata é calculada).</summary>
    public SpriteMesh? TryGetNearest(int code, int view)
    {
        if (TryGet(code, view) is SpriteMesh exact)
        {
            return exact;
        }

        if (view == TopView)
        {
            return null;
        }

        for (int d = 1; d <= SideViews / 2; d++)
        {
            SpriteMesh? near = TryGet(code, (view + d) % SideViews) ?? TryGet(code, (view - d + SideViews) % SideViews);
            if (near is not null)
            {
                return near;
            }
        }

        return null;
    }

    /// <summary>Todas as imagens de uma fase estão prontas.</summary>
    public bool IsComplete(int code) =>
        Enumerable.Range(0, SideViews).Append(TopView).All(v => TryGet(code, v) is not null);

    /// <summary>Calcula todas as imagens de uma fase (para uso em segundo plano).</summary>
    public void Warm(int code, CancellationToken cancel = default)
    {
        foreach (int view in Enumerable.Range(0, SideViews).Append(TopView))
        {
            cancel.ThrowIfCancellationRequested();
            Get(code, view);
        }
    }
}

/// <summary>
/// Vegetação ilustrada como RPC, no estilo dos símbolos: uma imagem por direção e por fase (plantio, 3 anos, 6 anos,
/// adulta e, nas decíduas, adulta sem folhas), escolhida em cada exemplar pelo parâmetro "VIR Fase 3D".
/// </summary>
public static class PlantSpritesPlanner
{
    /// <summary>Versão do desenho: aumente quando o traço mudar.</summary>
    public const int Version = 6;

    /// <summary>Parâmetros do traço (entram na assinatura).</summary>
    public const string Style = "rpc16-topo-nivel1-folhas-por-especie-luz-profundidade-contorno-casca-chao-idade-traco-flores-fase-grade600-dp0.95-escala-geom0.4-tom0.3";

    /// <summary>Fase mostrada quando o exemplar não escolhe outra.</summary>
    public const int DefaultStage = 4;

    /// <summary>Tom das imagens: um pouco mais de massas claras que escuras, igual em todas as direções.</summary>
    public const double Tone = 0.3;

    /// <summary>
    /// Espécies de referência (formas bem diferentes entre si), usadas nas prévias e nos testes: árvores de copa
    /// arredondada e espalhada, palmeiras pinada e em leque, conífera, roseta, herbácea de folha larga e dracena.
    /// </summary>
    public static readonly IReadOnlyList<string> Showcase = new[]
    {
        "Handroanthus chrysotrichus", "Delonix regia", "Syagrus romanzoffiana", "Bismarckia nobilis",
        "Araucaria angustifolia", "Agave attenuata", "Strelitzia reginae", "Dracaena marginata",
    };

    /// <summary>
    /// A espécie ganha a vegetação ilustrada: todas as do catálogo (e as criadas pelo usuário), exceto as que usam
    /// uma família de plantio própria do escritório.
    /// </summary>
    public static bool IsIllustrated(Species species) => species is not null && !species.HasCustomPlantingFamily;

    /// <summary>Gêneros de casca lisa e de tronco com acúleos (o restante das árvores tem casca com sulcos).</summary>
    private static readonly string[] SmoothBark = { "Libidibia", "Caesalpinia", "Psidium", "Plinia", "Myrciaria", "Eucalyptus", "Corymbia", "Lagerstroemia", "Eugenia", "Ficus", "Schinus", "Cassia" };

    private static readonly string[] SpinyBark = { "Ceiba", "Chorisia", "Erythrina", "Hura" };

    /// <summary>
    /// Estilo do desenho detalhado da espécie: formato e tamanho das folhas (pelo perfil 3D e pela textura), casca
    /// (anéis nas palmeiras, lisa ou com acúleos pelos gêneros conhecidos, sulcos nas demais) e flores mais ralas nas
    /// fases jovens.
    /// </summary>
    public static CardStyle StyleFor(Species species, Plant3DProfile profile, int stageCode)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(profile);
        bool palm = profile.Architecture is PlantArchitecture.PalmeiraEstipeUnico or PlantArchitecture.PalmeiraTouceira;
        LeafShape leaf = palm && profile.Palm is not null ? profile.Palm.Frond : profile.Foliage.Shape;
        double scale = species.EffectiveTexture switch
        {
            Texture.Fina => 0.8,
            Texture.Grossa => 1.3,
            _ => 1.0,
        };
        string genus = (species.ScientificName ?? string.Empty).Trim().Split(' ')[0];
        BarkKind bark = palm || profile.Architecture == PlantArchitecture.TufosEmCaule ? BarkKind.Aneis
            : SpinyBark.Contains(genus, StringComparer.OrdinalIgnoreCase) ? BarkKind.Espinhos
            : SmoothBark.Contains(genus, StringComparer.OrdinalIgnoreCase) || species.Category == PlantCategory.Arbusto ? BarkKind.Lisa
            : BarkKind.Fissurada;
        double flowers = stageCode switch
        {
            1 => 0.3,
            2 => 0.6,
            3 => 0.85,
            _ => 1.0,
        };
        return new CardStyle(leaf, scale, bark, false, flowers, true, profile.Architecture == PlantArchitecture.RosetaRigida);
    }

    /// <summary>
    /// Fidelidade à ficha: quando a planta gerada sai bem mais baixa, mais alta ou mais estreita que a altura e a
    /// copa da espécie (acontece com rosetas, samambaias, bromélias, herbáceas de folhas eretas e aquáticas), ela é
    /// ajustada às medidas da ficha, dentro de limites.
    /// </summary>
    internal static List<MeshPiece> FitToData(IReadOnlyList<MeshPiece> pieces, double heightM, double crownM, PlantCategory category, PlantArchitecture architecture)
    {
        // A largura conta só a folhagem e os galhos: flores que se projetam (estrelítzia) não são copa.
        double maxZ = 0, reach = 0;
        foreach (MeshPiece piece in pieces)
        {
            bool decoration = piece.Part is MeshPart.Flor or MeshPart.Fruto;
            foreach (Vec3 v in piece.Vertices)
            {
                maxZ = Math.Max(maxZ, v.Z);
                if (!decoration)
                {
                    reach = Math.Max(reach, Math.Sqrt(v.X * v.X + v.Y * v.Y));
                }
            }
        }

        // Altura: estica até a ficha quando sai bem menor (até 4,5 vezes; nas aquáticas, cujas folhas saem rentes à água
        // no modelo, até 12) e encolhe quando passa muito (até a metade).
        double limit = category == PlantCategory.Aquatica ? 12 : 4.5;
        double fz = 1;
        if (heightM > 0 && maxZ > 1e-6)
        {
            if (maxZ < 0.82 * heightM)
            {
                fz = Math.Min(heightM / maxZ, limit);
            }
            else if (maxZ > 1.25 * heightM)
            {
                fz = Math.Max(heightM / maxZ, 0.5);
            }
        }

        // Largura: abre até a copa da ficha quando sai bem mais estreita (até 2,5 vezes).
        double width = 2 * reach;
        double fxy = crownM > 0 && width > 1e-6 && width < 0.7 * crownM ? Math.Min(crownM / width, 2.5) : 1;
        if (fz == 1 && fxy == 1)
        {
            return pieces.ToList();
        }

        var result = new List<MeshPiece>(pieces.Count);
        foreach (MeshPiece piece in pieces)
        {
            var copy = new MeshPiece(piece.Part, piece.Closed);

            // Flores e frutos não se deformam: sobem (ou descem) inteiros com a planta. Quando sobem muito acima da
            // folhagem (lótus, aquáticas), ganham uma haste fina até a água.
            if (piece.Part is MeshPart.Flor or MeshPart.Fruto && piece.Vertices.Count > 0)
            {
                double cx = piece.Vertices.Average(v => v.X), cy = piece.Vertices.Average(v => v.Y);
                double low = piece.Vertices.Min(v => v.Z), top = piece.Vertices.Max(v => v.Z), lift = low * (fz - 1);

                // Aquáticas: a flor fica no alto da planta (a altura da ficha é a da haste floral).
                if (category == PlantCategory.Aquatica && fz > 1 && heightM > top)
                {
                    lift = Math.Max(lift, heightM - top);
                }

                foreach (Vec3 v in piece.Vertices)
                {
                    copy.Add(new Vec3(cx * fxy + (v.X - cx), cy * fxy + (v.Y - cy), v.Z + lift));
                }

                foreach (Tri t in piece.Triangles)
                {
                    copy.Triangle(t.A, t.B, t.C);
                }

                result.Add(copy);
                if (lift > 0.15)
                {
                    var stem = new MeshPiece(MeshPart.Folhagem, closed: false);
                    double w = 0.006;
                    int a = stem.Add(new Vec3(cx * fxy - w, cy * fxy, 0.02));
                    int b = stem.Add(new Vec3(cx * fxy + w, cy * fxy, 0.02));
                    int c = stem.Add(new Vec3(cx * fxy + w, cy * fxy, low + lift));
                    int d = stem.Add(new Vec3(cx * fxy - w, cy * fxy, low + lift));
                    stem.Triangle(a, b, c);
                    stem.Triangle(a, c, d);
                    int e = stem.Add(new Vec3(cx * fxy, cy * fxy - w, 0.02));
                    int f = stem.Add(new Vec3(cx * fxy, cy * fxy + w, 0.02));
                    int g = stem.Add(new Vec3(cx * fxy, cy * fxy + w, low + lift));
                    int h = stem.Add(new Vec3(cx * fxy, cy * fxy - w, low + lift));
                    stem.Triangle(e, f, g);
                    stem.Triangle(e, g, h);
                    result.Add(stem);
                }

                continue;
            }

            foreach (Vec3 v in piece.Vertices)
            {
                copy.Add(new Vec3(v.X * fxy, v.Y * fxy, v.Z * fz));
            }

            foreach (Tri t in piece.Triangles)
            {
                copy.Triangle(t.A, t.B, t.C);
            }

            result.Add(copy);
        }

        return result;
    }

    public static PlantSprites Build(Species species, int variant, IReadOnlyList<Plant3DProfile> profiles, ColorSettings colors, string trunkColor, bool southernHemisphere = true)
    {
        ArgumentNullException.ThrowIfNull(species);
        (Plant3DProfile profile, _) = ProfileResolver.Resolve(species, profiles);
        bool showy = !string.IsNullOrWhiteSpace(species.FlowerColor) && profile.Flowers is not null;
        var stages = new List<(int Code, string Label, GrowthStage Stage, int Month)>
        {
            (1, "Plantio", GrowthStage.Plantio, 0),
            (2, "3 anos", GrowthStage.TresAnos, 0),
            (3, "6 anos", GrowthStage.SeisAnos, 0),
            (4, "Adulta", GrowthStage.Maturidade, 0),
        };
        if (species.EffectiveLeafHabit == LeafHabit.Decidua)
        {
            int month = Enumerable.Range(1, 12).FirstOrDefault(m => species.EffectiveLeaflessMonths.Contains(m));
            if (month > 0)
            {
                stages.Add((5, "Adulta sem folhas", GrowthStage.Maturidade, month));
            }
        }

        long seed = DeterministicRandom.StableHash(species.Id + "#" + variant);
        (double adultCrown, double adultHeight) = GrowthModel.Size(species, GrowthStage.Maturidade);
        double reference = Math.Max(adultHeight, adultCrown);
        var signature = new List<string> { "sprites v" + Version, Style };
        var requests = new Dictionary<int, (PlantRequest Request, PlantColors Colors)>();
        var spriteStages = new List<SpriteStage>();
        foreach ((int code, string label, GrowthStage stage, int month) in stages)
        {
            (double crown, double height) = GrowthModel.Size(species, stage);
            var options = new PlantOptions
            {
                Quality = QualityLevel.Intermediaria,
                Month = month,
                SouthernHemisphere = southernHemisphere,
                Flowers = showy,
                Variation = true,
            };
            PlantRequest request = PlantPlanner.Request(species, profile, crown, height, seed, options);
            if (month == 0 && showy)
            {
                request = new PlantRequest
                {
                    Profile = request.Profile, HeightM = request.HeightM, CrownDiameterM = request.CrownDiameterM, Quality = request.Quality, Seed = request.Seed,
                    Foliage = request.Foliage, Flowers = true, Fruits = request.Fruits, SolidOnly = false, Variation = true,
                };
            }

            PlantColors partColors = PlantPlanner.Colors(species, profile, colors, trunkColor, request.Fruits);
            requests[code] = (request, partColors);
            spriteStages.Add(new SpriteStage(code, label, request.HeightM, request.CrownDiameterM));
            signature.Add(code + ":" + GeometryHash.Of(request) + ":" + string.Join(",", PlantColors.Parts.Select(p => partColors.For(p).Hex)));
        }

        // As peças 3D de cada fase são geradas uma vez só, na primeira imagem pedida.
        var parts = new ConcurrentDictionary<int, Lazy<List<IllustratedPart>>>();
        List<IllustratedPart> PartsOf(int code) => parts.GetOrAdd(code, c => new Lazy<List<IllustratedPart>>(() =>
        {
            (PlantRequest request, PlantColors partColors) = requests[c];
            GeneratedPlant plant = PlantGeometryGenerator.Generate(request);
            List<MeshPiece> pieces = FitToData(plant.Model.Pieces, request.HeightM, request.CrownDiameterM, species.Category, profile.Architecture);
            return pieces.Select(p => new IllustratedPart(p, partColors.For(p.Part))).ToList();
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        SpriteMesh Draw(int code, int view)
        {
            List<IllustratedPart> stageParts = PartsOf(code);
            int artSeed = (int)(seed % int.MaxValue) + code * 31;
            CardStyle style = StyleFor(species, profile, code);
            CardArt art = view == PlantSprites.TopView
                ? CardArtist.DrawTop(stageParts, Tone, artSeed, reference, style)
                : CardArtist.Draw(stageParts, PlantSprites.ViewAzimuth(view), Tone, artSeed, reference, style);
            return SpriteMesh.From(CardRaster.Flatten(art, referenceSize: reference), art);
        }

        return new PlantSprites(GeometryHash.Text(string.Join("|", signature))[..12], spriteStages, Draw);
    }
}
