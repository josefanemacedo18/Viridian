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
/// Piloto: oito espécies de formas bem diferentes.
/// </summary>
public static class PlantSpritesPlanner
{
    /// <summary>Versão do desenho: aumente quando o traço mudar.</summary>
    public const int Version = 2;

    /// <summary>Parâmetros do traço (entram na assinatura).</summary>
    public const string Style = "rpc16-topo-volume3-fundo-copa-foliolos-verdes-marcas-cachos-traco-variavel-grade600-dp0.95-escala-geom0.4-tom0.3";

    /// <summary>Fase mostrada quando o exemplar não escolhe outra.</summary>
    public const int DefaultStage = 4;

    /// <summary>Tom das imagens: um pouco mais de massas claras que escuras, igual em todas as direções.</summary>
    public const double Tone = 0.3;

    public static readonly IReadOnlyList<string> Pilot = new[]
    {
        "Handroanthus chrysotrichus", "Delonix regia", "Syagrus romanzoffiana", "Bismarckia nobilis",
        "Araucaria angustifolia", "Agave attenuata", "Strelitzia reginae", "Dracaena marginata",
    };

    public static bool IsPilot(Species species) =>
        species is not null && Pilot.Any(n => string.Equals(n, species.ScientificName?.Trim(), StringComparison.OrdinalIgnoreCase));

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
            return plant.Model.Pieces.Select(p => new IllustratedPart(p, partColors.For(p.Part))).ToList();
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        SpriteMesh Draw(int code, int view)
        {
            List<IllustratedPart> stageParts = PartsOf(code);
            int artSeed = (int)(seed % int.MaxValue) + code * 31;
            CardArt art = view == PlantSprites.TopView
                ? CardArtist.DrawTop(stageParts, Tone, artSeed, reference)
                : CardArtist.Draw(stageParts, PlantSprites.ViewAzimuth(view), Tone, artSeed, reference);
            return SpriteMesh.From(CardRaster.Flatten(art, referenceSize: reference), art);
        }

        return new PlantSprites(GeometryHash.Text(string.Join("|", signature))[..12], spriteStages, Draw);
    }
}
