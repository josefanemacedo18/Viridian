namespace Viridian.Core.Model3D.Illustration;

/// <summary>Onde e como desenhar a imagem de um exemplar: origem e eixos (direita, cima, frente) e qual imagem usar.</summary>
public readonly record struct SpriteFrame(Vec3 Origin, Vec3 Right, Vec3 Up, Vec3 Normal, int View)
{
    /// <summary>Leva um ponto da imagem (x, y, z em unidades do desenho) para o espaço do modelo.</summary>
    public Vec3 ToWorld(double x, double y, double z) => Origin + Right * x + Up * y + Normal * z;
}

/// <summary>
/// Regra do RPC: a imagem fica sempre de frente para a câmera, em pé sobre o ponto do exemplar, e a imagem escolhida
/// é a da direção de onde a planta é vista (16 em volta). Olhando bem de cima, passa à vista de cima, deitada na
/// altura da copa. Vale para câmera em perspectiva (cada planta encara o olho) e ortogonal (todas encaram a mesma
/// direção). As medidas são em qualquer unidade, desde que todas na mesma.
/// </summary>
public static class SpriteBillboard
{
    /// <summary>Acima deste ângulo da câmera sobre o horizonte, mostra a vista de cima (graus).</summary>
    public const double TopViewAngleDeg = 58;

    /// <summary>Altura da vista de cima, em fração da altura da planta.</summary>
    public const double TopViewHeightFraction = 0.85;

    private static readonly Vec3 WorldUp = new(0, 0, 1);

    /// <param name="basePoint">Ponto de inserção do exemplar (no chão).</param>
    /// <param name="rotation">Rotação do exemplar em torno do eixo vertical (radianos).</param>
    /// <param name="height">Altura da planta (para o centro da imagem e a altura da vista de cima).</param>
    /// <param name="eye">Posição do olho (só usada em perspectiva).</param>
    /// <param name="viewDirection">Para onde a câmera olha.</param>
    /// <param name="cameraUp">Direção "para cima" da tela.</param>
    /// <param name="perspective">Câmera em perspectiva.</param>
    /// <param name="forward">Afastamento da imagem para a frente (cobre o eixo de seleção da família).</param>
    public static SpriteFrame Place(Vec3 basePoint, double rotation, double height, Vec3 eye, Vec3 viewDirection, Vec3 cameraUp, bool perspective, double forward)
    {
        Vec3 center = basePoint + WorldUp * (height / 2);
        Vec3 toEye = perspective ? eye - center : -viewDirection;
        if (toEye.Length < 1e-9)
        {
            toEye = -viewDirection;
        }

        toEye = toEye.Normalized();
        double elevation = Math.Asin(Math.Clamp(toEye.Z, -1, 1)) * 180 / Math.PI;
        if (elevation >= TopViewAngleDeg)
        {
            var east = new Vec3(Math.Cos(rotation), Math.Sin(rotation), 0);
            var north = new Vec3(-Math.Sin(rotation), Math.Cos(rotation), 0);
            return new SpriteFrame(basePoint + WorldUp * (height * TopViewHeightFraction), east, north, WorldUp, PlantSprites.TopView);
        }

        // Imagem de frente para a câmera, com o "para cima" da tela (as plantas ficam em pé na tela).
        Vec3 up = cameraUp - toEye * cameraUp.Dot(toEye);
        if (up.Length < 1e-6)
        {
            up = WorldUp - toEye * WorldUp.Dot(toEye);
        }

        up = up.Normalized();
        Vec3 right = up.Cross(toEye).Normalized();

        // Qual das 16 imagens: a direção horizontal de onde se olha, no sistema da família.
        double azimuth = Math.Atan2(toEye.Y, toEye.X) - rotation;
        int view = PlantSprites.ViewIndex(azimuth);
        return new SpriteFrame(basePoint + toEye * forward, right, up, toEye, view);
    }
}
