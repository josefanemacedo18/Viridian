using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Illustration;

/// <summary>Parte da malha com a cor já escolhida (folhagem em tons, tronco, flor, fruto).</summary>
public sealed record IllustratedPart(MeshPiece Piece, Rgb Color);

public enum ArtOpKind
{
    /// <summary>Polígono preenchido (com contorno preto opcional).</summary>
    Fill,

    /// <summary>Triângulos unidos numa forma só, com contorno preto apenas na silhueta.</summary>
    Union,

    /// <summary>Traço reto (hachura).</summary>
    Line,
}

/// <summary>Uma operação de pintura do cartão, em metros (u ao longo do plano, v para cima, chão em v = 0).</summary>
public sealed record ArtOp(ArtOpKind Kind, int Color, IReadOnlyList<Point2> Points, double Width, bool Outline);

/// <summary>
/// Arte de um plano do cartão: operações de pintura em ordem (as de depois cobrem as de antes), paleta de cores
/// (índice 0 = vazio, 1 = contorno preto) e espessura do contorno, em metros.
/// </summary>
public sealed class CardArt
{
    public const int Empty = 0;
    public const int Black = 1;

    public List<Rgb> Palette { get; } = new() { Rgb.White, new Rgb(0x1E, 0x1E, 0x1E) };

    public List<ArtOp> Ops { get; } = new();

    /// <summary>Espessura do contorno preto (m).</summary>
    public double Outline { get; set; }

    /// <summary>Tamanho de referência da planta (maior entre altura e largura), em metros.</summary>
    public double Size { get; set; }

    public int ColorIndex(Rgb color)
    {
        int index = Palette.IndexOf(color);
        if (index < 0)
        {
            Palette.Add(color);
            index = Palette.Count - 1;
        }

        return index;
    }
}
