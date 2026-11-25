using Viridian.Core.Geometry;

namespace Viridian.Core.Solar;

/// <summary>
/// Norte verdadeiro indicado na planta: o projetista clica em dois pontos (o segundo na direção do norte verdadeiro)
/// e o ângulo fica gravado no projeto. Ângulos em graus, medidos a partir do eixo Y do modelo (o "para cima" da
/// planta orientada pelo norte do projeto), positivos no sentido anti-horário.
/// </summary>
public static class TrueNorth
{
    /// <summary>Distância mínima entre os dois pontos clicados (m).</summary>
    public const double MinPickDistanceM = 0.05;

    /// <summary>Ângulo do norte verdadeiro pela direção de <paramref name="from"/> para <paramref name="toward"/>.</summary>
    public static double FromPoints(Point2 from, Point2 toward)
    {
        double dx = toward.X - from.X, dy = toward.Y - from.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 1e-9)
        {
            throw new ArgumentException("Os dois pontos são iguais: clique o segundo ponto na direção do norte.");
        }

        return Normalize(Geo.RadToDeg(Math.Atan2(-dx, dy)));
    }

    /// <summary>Ângulo entre -180 e 180 graus.</summary>
    public static double Normalize(double degrees)
    {
        double a = degrees % 360;
        if (a > 180)
        {
            a -= 360;
        }
        else if (a <= -180)
        {
            a += 360;
        }

        return a;
    }

    /// <summary>Direção do norte verdadeiro no modelo (vetor unitário) para o ângulo indicado.</summary>
    public static Point2 Direction(double degrees)
    {
        double r = Geo.DegToRad(degrees);
        return new Point2(-Math.Sin(r), Math.Cos(r));
    }

    /// <summary>Ângulo em texto ("12,5° para a esquerda do topo da planta").</summary>
    public static string Describe(double degrees)
    {
        double a = Normalize(degrees);
        if (Math.Abs(a) < 0.05)
        {
            return "norte verdadeiro para o topo da planta";
        }

        string side = a > 0 ? "à esquerda" : "à direita";
        return $"norte verdadeiro {Math.Abs(a).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}° {side} do topo da planta";
    }
}
