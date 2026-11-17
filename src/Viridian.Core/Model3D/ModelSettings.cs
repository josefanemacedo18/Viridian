using Viridian.Core.Colors;
using Viridian.Core.Detailing;

namespace Viridian.Core.Model3D;

/// <summary>Configurações da maquete 3D.</summary>
public sealed class ModelSettings
{
    public const int HardLimit = 20000;

    /// <summary>Máximo de exemplares por maquete (acima disso, a vista 3D fica pesada).</summary>
    public int MaxPlants { get; set; } = 4000;

    /// <summary>Assenta as plantas e os maciços sobre o terreno (toposólido ou superfície topográfica).</summary>
    public bool FollowTerrain { get; set; } = true;

    /// <summary>Distância máxima, para cima ou para baixo, em que o terreno é procurado a partir do nível (m).</summary>
    public double TerrainSearchM { get; set; } = 30.0;

    public string TrunkColor { get; set; } = "#6B4F3A";

    /// <summary>Mostra as manchas de flor nas espécies de floração vistosa.</summary>
    public bool ShowFlowers { get; set; } = true;

    public void Normalize()
    {
        MaxPlants = Math.Clamp(MaxPlants <= 0 ? 4000 : MaxPlants, 1, HardLimit);
        TerrainSearchM = double.IsFinite(TerrainSearchM) && TerrainSearchM > 0 ? Math.Clamp(TerrainSearchM, 0.5, 200) : 30.0;
        if (!Rgb.TryParse(TrunkColor, out _))
        {
            TrunkColor = "#6B4F3A";
        }
    }
}

/// <summary>O que a maquete atual mostra, gravado na vista 3D para a atualização refazer o mesmo recorte.</summary>
public sealed class MaqueteState
{
    public int SchemaVersion { get; set; } = 1;

    public GrowthStage Stage { get; set; } = GrowthStage.Maturidade;

    /// <summary>Contorno da região (anéis de pares X, Y em metros); vazio quando a maquete é do projeto inteiro.</summary>
    public List<List<double[]>> Region { get; set; } = new();

    /// <summary>Nível da planta usada para escolher a região (vazio no projeto inteiro).</summary>
    public string LevelName { get; set; } = string.Empty;

    public bool IsWholeProject => Region.Count == 0;

    public Geometry.Polygon2? RegionPolygon() =>
        Region.Count == 0 ? null : new Geometry.Polygon2(Region.Select(r => r.Where(p => p is { Length: >= 2 }).Select(p => new Geometry.Point2(p[0], p[1]))));

    public static List<List<double[]>> FromPolygon(Geometry.Polygon2? polygon) =>
        polygon is null ? new() : polygon.Rings.Select(r => r.Select(p => new[] { p.X, p.Y }).ToList()).ToList();
}
