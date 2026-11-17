using System.Globalization;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Interference;

/// <summary>Tipo de elemento verificado em relação ao plantio.</summary>
public enum NetworkKind
{
    /// <summary>Tubulações: água, esgoto, drenagem, gás, irrigação.</summary>
    Tubulacao = 0,

    /// <summary>Eletrodutos e redes elétricas e de dados enterradas.</summary>
    Eletroduto = 1,

    /// <summary>Dutos de ar.</summary>
    Duto = 2,

    /// <summary>Eletrocalhas e leitos de cabos.</summary>
    Eletrocalha = 3,

    /// <summary>Postes e luminárias.</summary>
    Iluminacao = 4,

    /// <summary>Caixas, quadros, tomadas e equipamentos elétricos.</summary>
    Eletrico = 5,

    /// <summary>Paredes e fachadas.</summary>
    Edificacao = 6,

    /// <summary>Pisos e pavimentos (plantio sem berço).</summary>
    Pavimento = 7,
}

/// <summary>Trecho linear de rede ou parede, em planta.</summary>
public sealed record NetworkSegment(NetworkKind Kind, Point2 A, Point2 B, double HalfWidthM, string Description);

/// <summary>Elemento pontual: poste, luminária, caixa ou equipamento.</summary>
public sealed record NetworkPoint(NetworkKind Kind, Point2 Location, double RadiusM, string Description);

/// <summary>Área pavimentada (com as aberturas, como berços de árvores, como furos).</summary>
public sealed record PavementArea(Polygon2 Area, double ElevationM, string Description);

/// <summary>Exemplar verificado.</summary>
public sealed record InterferencePlant(long ElementId, string Code, string Name, PlantCategory Category, Point2 Location, double ElevationM, double CrownDiameterM);

/// <summary>Distâncias mínimas, a partir do eixo do tronco, para um tipo de elemento.</summary>
public sealed class InterferenceRule
{
    public NetworkKind Kind { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Árvores, em metros (zero = não verificar).</summary>
    public double TreeM { get; set; }

    /// <summary>Palmeiras, em metros.</summary>
    public double PalmM { get; set; }

    /// <summary>Arbustos e trepadeiras, em metros.</summary>
    public double ShrubM { get; set; }
}

/// <summary>Configurações da verificação de interferências.</summary>
public sealed class InterferenceSettings
{
    /// <summary>Inclui as interferências na verificação geral do projeto.</summary>
    public bool Enabled { get; set; } = true;

    public List<InterferenceRule> Rules { get; set; } = DefaultRules();

    /// <summary>Fração do raio da copa adulta que deve caber entre o tronco e a fachada (árvores e palmeiras).</summary>
    public double BuildingCrownFactor { get; set; } = 0.75;

    /// <summary>Verifica árvores, palmeiras e arbustos plantados sobre pisos e pavimentos sem abertura.</summary>
    public bool CheckPavements { get; set; } = true;

    /// <summary>Diferença de nível máxima para que um piso seja considerado o pavimento onde a planta está.</summary>
    public double PavementLevelToleranceM { get; set; } = 1.0;

    /// <summary>Lê também os modelos vinculados (instalações, arquitetura, infraestrutura).</summary>
    public bool IncludeLinks { get; set; } = true;

    /// <summary>
    /// Valores de referência usuais em manuais de arborização urbana. Variam por município e concessionária:
    /// o responsável técnico deve ajustá-los às normas locais.
    /// </summary>
    public static List<InterferenceRule> DefaultRules() => new()
    {
        new InterferenceRule { Kind = NetworkKind.Tubulacao, TreeM = 1.5, PalmM = 1.0, ShrubM = 0.5 },
        new InterferenceRule { Kind = NetworkKind.Eletroduto, TreeM = 1.0, PalmM = 1.0, ShrubM = 0.5 },
        new InterferenceRule { Kind = NetworkKind.Duto, TreeM = 1.0, PalmM = 1.0, ShrubM = 0.5 },
        new InterferenceRule { Kind = NetworkKind.Eletrocalha, TreeM = 1.0, PalmM = 1.0, ShrubM = 0.3 },
        new InterferenceRule { Kind = NetworkKind.Iluminacao, TreeM = 4.0, PalmM = 2.0, ShrubM = 0.5 },
        new InterferenceRule { Kind = NetworkKind.Eletrico, TreeM = 1.5, PalmM = 1.0, ShrubM = 0.5 },
        new InterferenceRule { Kind = NetworkKind.Edificacao, TreeM = 1.5, PalmM = 1.0, ShrubM = 0.3 },
    };

    public InterferenceRule? RuleOf(NetworkKind kind) => Rules.FirstOrDefault(r => r.Kind == kind);

    public void Normalize()
    {
        Rules ??= DefaultRules();
        foreach (InterferenceRule fallback in DefaultRules())
        {
            InterferenceRule? rule = Rules.FirstOrDefault(r => r.Kind == fallback.Kind);
            if (rule is null)
            {
                Rules.Add(fallback);
                continue;
            }

            rule.TreeM = Clamp(rule.TreeM);
            rule.PalmM = Clamp(rule.PalmM);
            rule.ShrubM = Clamp(rule.ShrubM);
        }

        Rules = Rules.Where(r => Enum.IsDefined(r.Kind) && r.Kind != NetworkKind.Pavimento)
            .GroupBy(r => r.Kind).Select(g => g.First()).OrderBy(r => r.Kind).ToList();
        BuildingCrownFactor = double.IsFinite(BuildingCrownFactor) ? Math.Clamp(BuildingCrownFactor, 0, 1.5) : 0.75;
        PavementLevelToleranceM = double.IsFinite(PavementLevelToleranceM) ? Math.Clamp(PavementLevelToleranceM, 0, 10) : 1.0;
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 50) : 0;
}

/// <summary>Uma interferência encontrada.</summary>
public sealed class InterferenceFinding
{
    public long PlantElementId { get; init; }

    public NetworkKind Kind { get; init; }

    /// <summary>Distância livre encontrada (zero quando há sobreposição).</summary>
    public double DistanceM { get; init; }

    public double RequiredM { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>Verifica distâncias entre o plantio e redes, postes, edificações e pavimentos.</summary>
public static class InterferenceChecker
{
    public static string DisplayName(NetworkKind kind) => kind switch
    {
        NetworkKind.Tubulacao => "Tubulações",
        NetworkKind.Eletroduto => "Eletrodutos e redes enterradas",
        NetworkKind.Duto => "Dutos",
        NetworkKind.Eletrocalha => "Eletrocalhas",
        NetworkKind.Iluminacao => "Postes e luminárias",
        NetworkKind.Eletrico => "Caixas e equipamentos elétricos",
        NetworkKind.Edificacao => "Edificações (paredes)",
        NetworkKind.Pavimento => "Pisos e pavimentos",
        _ => kind.ToString(),
    };

    /// <summary>Distância mínima exigida para a planta em relação a um tipo de elemento (zero = não verificar).</summary>
    public static double Required(InterferenceSettings settings, NetworkKind kind, InterferencePlant plant)
    {
        InterferenceRule? rule = settings.RuleOf(kind);
        if (rule is null || !rule.Enabled)
        {
            return 0;
        }

        double base_ = plant.Category switch
        {
            PlantCategory.Arvore => rule.TreeM,
            PlantCategory.Palmeira => rule.PalmM,
            PlantCategory.Arbusto or PlantCategory.Trepadeira => rule.ShrubM,
            _ => 0,
        };
        if (base_ > 0 && kind == NetworkKind.Edificacao && plant.Category is PlantCategory.Arvore or PlantCategory.Palmeira)
        {
            // A copa adulta não deve encostar na fachada.
            base_ = Math.Max(base_, plant.CrownDiameterM / 2 * settings.BuildingCrownFactor);
        }

        return base_;
    }

    public static IReadOnlyList<InterferenceFinding> Check(
        IReadOnlyList<InterferencePlant> plants,
        IReadOnlyList<NetworkSegment> segments,
        IReadOnlyList<NetworkPoint> points,
        IReadOnlyList<PavementArea> pavements,
        InterferenceSettings settings)
    {
        settings.Normalize();
        CultureInfo pt = CultureInfo.GetCultureInfo("pt-BR");
        var findings = new List<InterferenceFinding>();

        foreach (InterferencePlant plant in plants)
        {
            // Para cada tipo, só a ocorrência mais próxima de cada planta é relatada.
            var nearest = new Dictionary<NetworkKind, (double Distance, string Description)>();

            foreach (NetworkSegment s in segments)
            {
                double required = Required(settings, s.Kind, plant);
                if (required <= 0 || !Near(plant.Location, s.A, s.B, required + s.HalfWidthM))
                {
                    continue;
                }

                double d = Math.Max(0, Geo.DistancePointSegment(plant.Location, s.A, s.B) - s.HalfWidthM);
                if (d < required - 1e-6 && (!nearest.TryGetValue(s.Kind, out var current) || d < current.Distance))
                {
                    nearest[s.Kind] = (d, s.Description);
                }
            }

            foreach (NetworkPoint p in points)
            {
                double required = Required(settings, p.Kind, plant);
                if (required <= 0)
                {
                    continue;
                }

                double d = Math.Max(0, plant.Location.DistanceTo(p.Location) - p.RadiusM);
                if (d < required - 1e-6 && (!nearest.TryGetValue(p.Kind, out var current) || d < current.Distance))
                {
                    nearest[p.Kind] = (d, p.Description);
                }
            }

            foreach ((NetworkKind kind, (double distance, string description)) in nearest.OrderBy(n => n.Key))
            {
                double required = Required(settings, kind, plant);
                findings.Add(new InterferenceFinding
                {
                    PlantElementId = plant.ElementId,
                    Kind = kind,
                    DistanceM = distance,
                    RequiredM = required,
                    Message = string.Format(pt, "{0} a {1:0.00} m de {2} (mínimo {3:0.00} m).", Describe(plant), distance, description, required),
                });
            }

            if (settings.CheckPavements && plant.Category is PlantCategory.Arvore or PlantCategory.Palmeira or PlantCategory.Arbusto)
            {
                PavementArea? over = pavements.FirstOrDefault(a =>
                    Math.Abs(a.ElevationM - plant.ElevationM) <= settings.PavementLevelToleranceM
                    && a.Area.Bounds.Contains(plant.Location)
                    && a.Area.Contains(plant.Location));
                if (over is not null)
                {
                    findings.Add(new InterferenceFinding
                    {
                        PlantElementId = plant.ElementId,
                        Kind = NetworkKind.Pavimento,
                        Message = $"{Describe(plant)} está sobre {over.Description}, sem abertura (berço ou canteiro) no piso.",
                    });
                }
            }
        }

        return findings;
    }

    private static bool Near(Point2 p, Point2 a, Point2 b, double reach) =>
        p.X >= Math.Min(a.X, b.X) - reach && p.X <= Math.Max(a.X, b.X) + reach
        && p.Y >= Math.Min(a.Y, b.Y) - reach && p.Y <= Math.Max(a.Y, b.Y) + reach;

    private static string Describe(InterferencePlant plant)
    {
        string name = string.IsNullOrWhiteSpace(plant.Name) ? "Exemplar" : plant.Name;
        return string.IsNullOrWhiteSpace(plant.Code) ? name : $"{name} ({plant.Code})";
    }
}
