using Viridian.Core.Colors;

namespace Viridian.Core.Model3D;

/// <summary>
/// Cores de um exemplar por parte da malha: folhagem em três tons (luz, base e sombra), tronco, galhos, flor e fruto.
/// Usadas igualmente na maquete do Revit (materiais) e nas prévias.
/// </summary>
public sealed record PlantColors(Rgb Foliage, Rgb Trunk, Rgb Flower, Rgb Fruit)
{
    /// <summary>Verde-amarelado que dá a impressão de luz nas folhas de cima e de fora.</summary>
    private static readonly Rgb Sunlit = new(0xD9, 0xE4, 0x7C);

    public static readonly Rgb DefaultTrunk = new(0x6B, 0x4F, 0x3A);

    public Rgb For(MeshPart part) => part switch
    {
        MeshPart.Tronco => Trunk,
        MeshPart.Galho => Trunk.Mix(Foliage, 0.12).Shade(0.08),
        MeshPart.Flor => Flower,
        MeshPart.Fruto => Fruit,
        MeshPart.FolhagemClara => LightFoliage(Foliage),
        MeshPart.FolhagemEscura => DarkFoliage(Foliage),
        _ => Foliage,
    };

    public static Rgb LightFoliage(Rgb foliage) => foliage.Mix(Sunlit, 0.24);

    public static Rgb DarkFoliage(Rgb foliage) => foliage.Shade(0.26);

    /// <summary>Partes que recebem material próprio (a ordem define a ordem dos materiais criados).</summary>
    public static readonly MeshPart[] Parts =
    {
        MeshPart.Folhagem, MeshPart.FolhagemClara, MeshPart.FolhagemEscura, MeshPart.Tronco, MeshPart.Galho, MeshPart.Flor, MeshPart.Fruto,
    };
}
