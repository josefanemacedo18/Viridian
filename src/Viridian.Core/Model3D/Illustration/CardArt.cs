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

    /// <summary>Cores na paleta, no máximo: a grade de achatamento guarda o índice da cor num byte.</summary>
    public const int MaxColors = 250;

    /// <summary>Índice da cor na paleta. Cheia a paleta, devolve a cor mais próxima já usada (nunca outra cor qualquer).</summary>
    public int ColorIndex(Rgb color)
    {
        int index = Palette.IndexOf(color);
        if (index >= 0)
        {
            return index;
        }

        if (Palette.Count < MaxColors)
        {
            Palette.Add(color);
            return Palette.Count - 1;
        }

        int best = Black;
        int bestDistance = int.MaxValue;
        for (int i = 2; i < Palette.Count; i++)
        {
            Rgb p = Palette[i];
            int d = (p.R - color.R) * (p.R - color.R) + (p.G - color.G) * (p.G - color.G) + (p.B - color.B) * (p.B - color.B);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }
}

/// <summary>Casca do tronco no desenho.</summary>
public enum BarkKind
{
    /// <summary>Sulcos longitudinais (a maioria das árvores).</summary>
    Fissurada = 0,

    /// <summary>Lisa, com manchas claras (pau-ferro, goiabeira, jabuticabeira, eucalipto).</summary>
    Lisa = 1,

    /// <summary>Anéis das folhas caídas (palmeiras).</summary>
    Aneis = 2,

    /// <summary>Acúleos no tronco (paineira).</summary>
    Espinhos = 3,
}

/// <summary>
/// Como desenhar a espécie no nível de detalhe: formato e tamanho das folhas, casca, chão sob a planta e quantidade de
/// flores (menos nas fases jovens). <see cref="Basic"/> é o desenho simples, sem folhas individuais.
/// </summary>
public sealed record CardStyle(Profiles.LeafShape Leaf, double LeafScale, BarkKind Bark, bool Ground, double FlowerDensity, bool Detailed)
{
    public static readonly CardStyle Basic = new(Profiles.LeafShape.Eliptica, 1, BarkKind.Fissurada, false, 1, false);
}
