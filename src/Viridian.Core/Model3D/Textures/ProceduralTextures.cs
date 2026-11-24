using Viridian.Core.Colors;
using Viridian.Core.Geometry;

namespace Viridian.Core.Model3D.Textures;

/// <summary>
/// Texturas procedurais (geradas aqui, sem imagens de terceiros) para o teste T0: folhagem que se repete sem emenda,
/// com máscara de recorte (folhas brancas, vãos pretos), flores e casca. Determinísticas pela semente.
/// </summary>
public static class ProceduralTextures
{
    /// <summary>
    /// Folhagem: muitas folhas elípticas pontudas, em tons variados e com nervura, sobre fundo verde-escuro (para quando
    /// o recorte não for aplicado). Devolve a cor e a máscara de recorte.
    /// </summary>
    public static (RgbaImage Color, RgbaImage Mask) Foliage(int size, Rgb leaf, int seed, int leaves = 0, double leafLengthFraction = 0.11)
    {
        var color = new RgbaImage(size, size);
        var mask = new RgbaImage(size, size);
        Rgb background = leaf.Shade(0.55);
        color.Fill(background.R, background.G, background.B);
        mask.Fill(0, 0, 0);
        var random = new DeterministicRandom(DeterministicRandom.Combine(seed, 4241));
        // Quantidade pela proporção da folha (não pelo tamanho da imagem): a cobertura é a mesma em qualquer resolução.
        leaves = leaves > 0 ? leaves : (int)Math.Round(3.5 / (leafLengthFraction * leafLengthFraction));
        double length = size * leafLengthFraction;
        for (int i = 0; i < leaves; i++)
        {
            double cx = random.Range(0, size), cy = random.Range(0, size);
            double angle = random.Range(0, Geo.TwoPi);
            double l = length * random.Range(0.7, 1.2), w = l * random.Range(0.32, 0.45);
            double depth = (double)i / leaves; // as últimas ficam por cima e mais claras (luz)
            Rgb tone = Rgb.FromHsl(leaf.ToHsl().H + random.Range(-8, 8), Math.Clamp(leaf.ToHsl().S + random.Range(-0.08, 0.06), 0.15, 0.9),
                Math.Clamp(leaf.ToHsl().L * (0.75 + 0.45 * depth) + random.Range(-0.04, 0.04), 0.1, 0.8));
            DrawLeaf(color, mask, cx, cy, angle, l, w, tone);
        }

        return (color, mask);
    }

    /// <summary>Flores pequenas de cinco pétalas, com miolo, sobre fundo transparente (e máscara).</summary>
    public static (RgbaImage Color, RgbaImage Mask) Flowers(int size, Rgb petal, int seed, int count = 0)
    {
        var color = new RgbaImage(size, size);
        var mask = new RgbaImage(size, size);
        color.Fill(petal.Shade(0.35).R, petal.Shade(0.35).G, petal.Shade(0.35).B);
        mask.Fill(0, 0, 0);
        var random = new DeterministicRandom(DeterministicRandom.Combine(seed, 977));
        count = count > 0 ? count : 100; // flores com raio proporcional à imagem: cobertura igual em qualquer resolução
        for (int i = 0; i < count; i++)
        {
            double cx = random.Range(0, size), cy = random.Range(0, size);
            double r = size * random.Range(0.025, 0.045);
            double rotation = random.Range(0, Geo.TwoPi);
            Rgb tone = petal.Mix(Rgb.White, random.Range(0, 0.18)).Shade(random.Range(0, 0.12));
            for (int k = 0; k < 5; k++)
            {
                double a = rotation + k * Geo.TwoPi / 5;
                DrawLeaf(color, mask, cx + Math.Cos(a) * r * 0.55, cy + Math.Sin(a) * r * 0.55, a, r * 1.1, r * 0.75, tone, midrib: false);
            }

            Disc(color, mask, cx, cy, r * 0.28, petal.Mix(new Rgb(0x7A, 0x4A, 0x10), 0.6));
        }

        return (color, mask);
    }

    /// <summary>Casca: estrias verticais e fissuras, que se repetem sem emenda.</summary>
    public static RgbaImage Bark(int size, Rgb bark, int seed)
    {
        var image = new RgbaImage(size, size);
        var random = new DeterministicRandom(DeterministicRandom.Combine(seed, 7717));
        int columns = 24;
        var offsets = Enumerable.Range(0, columns).Select(_ => random.Range(-0.12, 0.12)).ToArray();
        var phases = Enumerable.Range(0, 6).Select(_ => random.Range(0, Geo.TwoPi)).ToArray();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double u = (double)x / size, v = (double)y / size;
                int column = (int)(u * columns) % columns;
                double within = u * columns - Math.Floor(u * columns);
                double fissure = Math.Pow(Math.Abs(Math.Sin(Math.PI * within)), 0.35); // escuro na divisa das placas
                double wave = 0.06 * Math.Sin(Geo.TwoPi * v * 3 + phases[column % phases.Length]) + 0.04 * Math.Sin(Geo.TwoPi * (u * 5 + v * 7) + phases[1]);
                double light = Math.Clamp(0.55 + 0.45 * fissure + offsets[column] + wave, 0.2, 1.15);
                Rgb c = light >= 1 ? bark.Tint(light - 1) : bark.Shade(1 - light);
                image.Set(x, y, c.R, c.G, c.B);
            }
        }

        return image;
    }

    /// <summary>Fração de pixels opacos da máscara (para o relatório).</summary>
    public static double Coverage(RgbaImage mask)
    {
        int opaque = 0;
        for (int i = 0; i < mask.Pixels.Length; i += 4)
        {
            opaque += mask.Pixels[i] > 127 ? 1 : 0;
        }

        return (double)opaque / (mask.Width * mask.Height);
    }

    /// <summary>Folha elíptica pontuda (com nervura clara), desenhada com volta nas bordas.</summary>
    private static void DrawLeaf(RgbaImage color, RgbaImage mask, double cx, double cy, double angle, double length, double width, Rgb tone, bool midrib = true)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        int reach = (int)Math.Ceiling(length / 2 + 1);
        Rgb vein = tone.Tint(0.25);
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                double along = (dx * c + dy * s) / (length / 2); // -1 a 1 ao longo da folha
                double across = (-dx * s + dy * c) / (width / 2);
                if (Math.Abs(along) > 1)
                {
                    continue;
                }

                double half = Math.Pow(1 - Math.Abs(along), 0.8) * (along < 0 ? 1.0 : 0.95); // ponta e base afinadas
                if (Math.Abs(across) > half)
                {
                    continue;
                }

                // Sombreado leve do centro para a borda e nervura.
                double shade = 1 - 0.18 * Math.Abs(across) / Math.Max(half, 1e-6);
                Rgb pixel = midrib && Math.Abs(across) < 0.07 ? vein : tone.Shade(1 - shade);
                int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
                color.Set(x, y, pixel.R, pixel.G, pixel.B);
                mask.Set(x, y, 255, 255, 255);
            }
        }
    }

    private static void Disc(RgbaImage color, RgbaImage mask, double cx, double cy, double radius, Rgb tone)
    {
        int r = (int)Math.Ceiling(radius);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy <= radius * radius)
                {
                    int x = (int)Math.Round(cx + dx), y = (int)Math.Round(cy + dy);
                    color.Set(x, y, tone.R, tone.G, tone.B);
                    mask.Set(x, y, 255, 255, 255);
                }
            }
        }
    }
}
