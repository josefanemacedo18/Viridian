using Viridian.Core.Geometry;
using Viridian.Core.Model3D.Profiles;
using Viridian.Core.Model3D.Seasonality;

namespace Viridian.Core.Model3D.Generation;

/// <summary>Tudo o que define a geometria de um exemplar (entra no hash).</summary>
public sealed class PlantRequest
{
    public Plant3DProfile Profile { get; init; } = new();

    public double HeightM { get; init; }

    public double CrownDiameterM { get; init; }

    public QualityLevel Quality { get; init; } = QualityLevel.Intermediaria;

    /// <summary>Semente estável do exemplar (derivada do identificador permanente da origem).</summary>
    public long Seed { get; init; } = 1;

    public FoliageState Foliage { get; init; } = FoliageState.Cheia;

    /// <summary>Mostrar flores agora (já considerada a estação e a configuração).</summary>
    public bool Flowers { get; init; }

    /// <summary>Frutos a mostrar agora; nulo = sem frutos.</summary>
    public FruitTraits? Fruits { get; init; }

    /// <summary>Somente sólidos fechados (famílias do Revit não aceitam superfícies abertas como forma livre).</summary>
    public bool SolidOnly { get; init; }

    /// <summary>Variação natural entre exemplares (proporções e forma); desligada, exemplares iguais da mesma espécie.</summary>
    public bool Variation { get; init; } = true;

    /// <summary>Semente usada de fato: a do exemplar com variação; sem variação, a do perfil (todos iguais).</summary>
    public long EffectiveSeed => Variation ? Seed : Geometry.DeterministicRandom.StableHash(Profile.Id);
}

/// <summary>Resultado da geração: malhas, orçamento, ajustes feitos e hash reprodutível.</summary>
public sealed class GeneratedPlant
{
    public PlantModel Model { get; init; } = new();

    public int Budget { get; init; }

    /// <summary>Fator de detalhe usado (1 = pleno; menor quando foi preciso reduzir para caber no orçamento).</summary>
    public double DetailFactor { get; init; } = 1;

    public List<string> Notes { get; } = new();

    public string Hash { get; init; } = string.Empty;
}

/// <summary>
/// Motor procedural de vegetação: gera a planta pelo perfil botânico, no porte, na qualidade e na estação pedidos.
/// Sem acesso ao Revit: malhas em metros, base da planta em (0, 0, 0). A mesma entrada gera sempre a mesma saída.
/// </summary>
public static class PlantGeometryGenerator
{
    /// <summary>Versão do gerador: aumente quando a geometria produzida mudar (entra no hash).</summary>
    public const int Version = 1;

    public static GeneratedPlant Generate(PlantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Plant3DProfile profile = request.Profile;
        double height = Clamp(request.HeightM, profile.Architecture == PlantArchitecture.Gramado ? 0.02 : 0.05, 80);
        double crown = Clamp(request.CrownDiameterM, 0.05, 60);
        var random = new DeterministicRandom(DeterministicRandom.Combine(request.EffectiveSeed, profile.Version, Version));
        if (request.Variation)
        {
            // Variação para menos: o exemplar nunca passa do porte previsto.
            height *= random.Range(0.93, 1.0);
            crown *= random.Range(0.9, 1.0);
        }

        int budget = profile.Budget.For(request.Quality);
        double factor = 1.0;
        PlantModel model = Build(request, height, crown, factor);
        var notes = new List<string>();
        for (int attempt = 0; attempt < 5 && model.TriangleCount > budget; attempt++)
        {
            factor *= Math.Clamp(Math.Sqrt((double)budget / model.TriangleCount) * 0.92, 0.35, 0.95);
            model = Build(request, height, crown, factor);
        }

        if (model.TriangleCount > budget)
        {
            int trimmed = Trim(model, budget);
            if (trimmed > 0)
            {
                notes.Add($"{trimmed} detalhe(s) (frutos, flores ou galhos finos) omitido(s) para caber no orçamento.");
            }
        }

        if (factor < 0.999)
        {
            notes.Add($"Detalhe reduzido a {factor:P0} para caber no orçamento de {budget} triângulos ({model.TriangleCount}).");
        }

        if (model.TriangleCount > budget)
        {
            notes.Add($"Orçamento de {budget} triângulos excedido ({model.TriangleCount}).");
        }

        var plant = new GeneratedPlant
        {
            Model = model,
            Budget = budget,
            DetailFactor = factor,
            Hash = GeometryHash.Of(request),
        };
        plant.Notes.AddRange(notes);
        return plant;
    }

    private static PlantModel Build(PlantRequest request, double height, double crown, double factor)
    {
        var context = new BuildContext(request, height, crown, factor);
        switch (request.Profile.Architecture)
        {
            case PlantArchitecture.PalmeiraEstipeUnico or PlantArchitecture.PalmeiraTouceira:
                PalmBuilder.Build(context);
                break;
            case PlantArchitecture.ArbustoArredondado or PlantArchitecture.ArbustoEreto or PlantArchitecture.Trepadeira
                or PlantArchitecture.Forracao or PlantArchitecture.TufosEmCaule:
                ShrubBuilder.Build(context);
                break;
            case PlantArchitecture.Graminea or PlantArchitecture.RosetaRigida or PlantArchitecture.HerbaceaFolhaLarga
                or PlantArchitecture.AquaticaEmergente or PlantArchitecture.AquaticaFlutuante:
                RosetteBuilder.Build(context);
                break;
            case PlantArchitecture.Gramado:
                context.Add(Primitives.Frustum(MeshPart.Folhagem, Vec3.Zero, height, crown / 2, crown / 2 * 0.92, 10));
                break;
            default:
                TreeBuilder.Build(context);
                break;
        }

        var model = new PlantModel
        {
            Shape = LegacyShape(request.Profile.Architecture),
            Architecture = request.Profile.Architecture,
            HeightM = height,
            CrownDiameterM = crown,
        };
        model.Pieces.AddRange(MeshBuild.MergeOpen(context.Pieces));
        GroundClamp(model);
        MeshValidator.Sanitize(model);
        return model;
    }

    private static PlantShape LegacyShape(PlantArchitecture a) => a switch
    {
        PlantArchitecture.ArvoreEspalhada => PlantShape.ArvoreEspalhada,
        PlantArchitecture.ArvoreColunar => PlantShape.ArvoreColunar,
        PlantArchitecture.ArvoreConica => PlantShape.ArvoreConica,
        PlantArchitecture.ArvoreIrregular => PlantShape.ArvoreIrregular,
        PlantArchitecture.PalmeiraEstipeUnico or PlantArchitecture.PalmeiraTouceira => PlantShape.Palmeira,
        PlantArchitecture.ArbustoArredondado or PlantArchitecture.ArbustoEreto or PlantArchitecture.Trepadeira or PlantArchitecture.TufosEmCaule => PlantShape.Arbusto,
        PlantArchitecture.Gramado => PlantShape.Gramado,
        PlantArchitecture.ArvoreArredondada => PlantShape.ArvoreArredondada,
        _ => PlantShape.Touceira,
    };

    /// <summary>
    /// Último recurso do orçamento: retira, do fim para o começo, frutos, depois flores e depois galhos (nunca o tronco
    /// nem a folhagem), até caber.
    /// </summary>
    private static int Trim(PlantModel model, int budget)
    {
        int removed = 0;
        foreach (MeshPart part in new[] { MeshPart.Fruto, MeshPart.Flor, MeshPart.Galho })
        {
            for (int i = model.Pieces.Count - 1; i >= 0 && model.TriangleCount > budget; i--)
            {
                if (model.Pieces[i].Part == part)
                {
                    model.Pieces.RemoveAt(i);
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Nada abaixo do chão: peças inteiramente enterradas sobem até o solo; as que só passam dele encostam no solo.
    /// </summary>
    private static void GroundClamp(PlantModel model)
    {
        foreach (MeshPiece piece in model.Pieces)
        {
            if (piece.Vertices.Count == 0)
            {
                continue;
            }

            double maxZ = piece.Vertices.Max(v => v.Z);
            double minZ = piece.Vertices.Min(v => v.Z);
            if (minZ >= 0)
            {
                continue;
            }

            if (maxZ < (maxZ - minZ) * 0.5)
            {
                for (int i = 0; i < piece.Vertices.Count; i++)
                {
                    piece.Vertices[i] += new Vec3(0, 0, -minZ);
                }

                continue;
            }

            for (int i = 0; i < piece.Vertices.Count; i++)
            {
                Vec3 v = piece.Vertices[i];
                if (v.Z < 0)
                {
                    piece.Vertices[i] = new Vec3(v.X, v.Y, 0);
                }
            }
        }
    }

    internal static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}

/// <summary>Estado de uma geração: medidas, qualidade, sorteio determinístico e as peças produzidas.</summary>
internal sealed class BuildContext
{
    public BuildContext(PlantRequest request, double height, double crown, double factor)
    {
        Request = request;
        Profile = request.Profile;
        H = height;
        D = crown;
        R = crown / 2;
        Factor = factor;
        Random = new DeterministicRandom(DeterministicRandom.Combine(request.EffectiveSeed, request.Profile.Version, 7919));
    }

    public PlantRequest Request { get; }

    public Plant3DProfile Profile { get; }

    public double H { get; }

    public double D { get; }

    public double R { get; }

    public double Factor { get; }

    public DeterministicRandom Random { get; }

    public List<MeshPiece> Pieces { get; } = new();

    public QualityLevel Quality => Request.Quality;

    public bool Detailed => Quality == QualityLevel.Detalhada;

    public bool Simple => Quality == QualityLevel.Simplificada;

    public bool Leafless => Request.Foliage == FoliageState.SemFolhas;

    public bool Sparse => Request.Foliage == FoliageState.Rala;

    public int Triangles => Pieces.Sum(p => p.TriangleCount);

    /// <summary>Triângulos ainda disponíveis no orçamento (já com o fator de detalhe).</summary>
    public int Remaining => (int)(Profile.Budget.For(Quality) * Factor) - Triangles;

    public long NextSeed() => Random.NextInt(1, int.MaxValue);

    /// <summary>Contagem por nível de qualidade, multiplicada pelo fator de detalhe.</summary>
    public int Count(double simple, double intermediate, double detailed, int min = 1)
    {
        double value = Quality switch { QualityLevel.Simplificada => simple, QualityLevel.Intermediaria => intermediate, _ => detailed };
        return Math.Max(min, (int)Math.Round(value * Factor));
    }

    /// <summary>Tamanho de flor, fruto ou ornamento limitado ao porte: num exemplar pequeno, nada desproporcional.</summary>
    public double Fit(double size, double fraction = 0.3) => Math.Clamp(size, MeshBuild.MinFeature * 3, Math.Max(MeshBuild.MinFeature * 3, Math.Min(H, D) * fraction));

    /// <summary>
    /// Mantém um grupo esférico dentro da projeção da copa: afasta-o para dentro (e, se preciso, reduz) para que o
    /// alcance horizontal não passe de <paramref name="limit"/> vezes o raio previsto.
    /// </summary>
    public (Vec3 Center, double Radius) FitInCrown(Vec3 center, double radius, double limit = 1.06)
    {
        double max = Math.Max(0.05, R * limit);
        radius = Math.Min(radius, max * 0.8);
        double reach = radius * 1.12; // ruído do grupo
        double d = Math.Sqrt(center.X * center.X + center.Y * center.Y);
        if (d + reach > max && d > 1e-9)
        {
            double k = Math.Max(0, max - reach) / d;
            center = new Vec3(center.X * k, center.Y * k, center.Z);
        }

        return (center, radius);
    }

    /// <summary>Resolução (lados, segmentos) por nível; não cai com o fator para não serrilhar silhuetas.</summary>
    public int Res(int simple, int intermediate, int detailed) => Quality switch
    {
        QualityLevel.Simplificada => simple,
        QualityLevel.Intermediaria => intermediate,
        _ => detailed,
    };

    public void Add(MeshPiece piece)
    {
        if (piece.TriangleCount > 0 && (!Request.SolidOnly || piece.Closed))
        {
            Pieces.Add(piece);
        }
    }

    /// <summary>Tom da folhagem: mais claro em cima e por fora, mais escuro embaixo e por dentro, com variação do perfil.</summary>
    public MeshPart FoliageTone(double heightFraction, double outward)
    {
        double tone = heightFraction * 0.6 + outward * 0.4 + Random.Range(-1, 1) * Profile.Foliage.ToneVariation * 2;
        return tone > 0.72 ? MeshPart.FolhagemClara : tone < 0.32 ? MeshPart.FolhagemEscura : MeshPart.Folhagem;
    }
}
