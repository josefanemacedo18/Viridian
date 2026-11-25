using Viridian.Core.Colors;
using Viridian.Core.Model3D.Textures;
using Viridian.Core.Paving;

namespace Viridian.Tests;

public class StoneTextureTests
{
    private static readonly List<LooseMaterial> Materials = LooseMaterialDefaults.Create();

    private static LooseMaterial M(string id) => Materials.First(m => m.Id == id);

    private static double ColumnDifference(RgbaImage image, int a, int b)
    {
        double sum = 0;
        for (int y = 0; y < image.Height; y++)
        {
            var p = image.Get(a, y);
            var q = image.Get(b, y);
            sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
        }

        return sum / image.Height;
    }

    [Fact]
    public void EveryMaterial_Renders_AndTheTileSizeIsSensible()
    {
        foreach (LooseMaterial m in Materials)
        {
            RgbaImage image = StoneTexture.Render(m, 96);
            Assert.Equal(96, image.Width);
            Assert.InRange(StoneTexture.TileMeters(m), 0.12, 1.6);
        }

        Assert.True(StoneTexture.TileMeters(M("vir-seixo-grande")) > StoneTexture.TileMeters(M("vir-pedrisco-branco")));
    }

    [Fact]
    public void Texture_IsReproducible_AndChangesWithTheMaterial()
    {
        LooseMaterial seixo = M("vir-seixo-rio");
        Assert.Equal(StoneTexture.Render(seixo, 128).Pixels, StoneTexture.Render(seixo, 128).Pixels);
        Assert.NotEqual(StoneTexture.Render(seixo, 128).Pixels, StoneTexture.Render(M("vir-brita-cinza"), 128).Pixels);

        LooseMaterial changed = Viridian.Core.Config.ViridianJson.Deserialize<LooseMaterial>(Viridian.Core.Config.ViridianJson.Serialize(seixo))!;
        changed.Color = "#AA3322";
        Assert.NotEqual(StoneTexture.Signature(seixo), StoneTexture.Signature(changed));
    }

    [Theory]
    [InlineData("vir-seixo-rio")]
    [InlineData("vir-brita-cinza")]
    [InlineData("vir-saibro")]
    [InlineData("vir-casca-pinus-media")]
    public void Texture_RepeatsWithoutASeam(string id)
    {
        RgbaImage image = StoneTexture.Render(M(id), 256);

        // A diferença entre a última e a primeira coluna (onde as cópias se encontram) é da mesma ordem que entre
        // colunas vizinhas no meio da imagem.
        double seam = ColumnDifference(image, image.Width - 1, 0);
        double inside = Enumerable.Range(60, 40).Average(x => ColumnDifference(image, x, x + 1));
        Assert.True(seam < inside * 2.2 + 6, $"{id}: emenda {seam:0.0} × interior {inside:0.0}");
    }

    [Theory]
    [InlineData("vir-seixo-branco")]
    [InlineData("vir-pedrisco-basalto")]
    [InlineData("vir-pedrisco-vermelho")]
    [InlineData("vir-po-de-pedra")]
    public void AverageColor_FollowsTheMaterial(string id)
    {
        LooseMaterial m = M(id);
        Rgb average = StoneTexture.Average(StoneTexture.Render(m, 128));
        Assert.True(Rgb.TryParse(m.Color, out Rgb color));
        // Um canteiro de pedras soltas fica mais escuro que a cor de uma pedra (sombras entre elas), mas na mesma família.
        Assert.InRange(average.Luminance, color.Luminance * 0.35, Math.Min(1, color.Luminance * 1.15 + 0.05));

        // A textura tem variação (não é uma cor chapada).
        RgbaImage image = StoneTexture.Render(m, 128);
        double mean = 0, var = 0;
        int n = image.Width * image.Height;
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            mean += image.Pixels[i + 1];
        }

        mean /= n;
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            var += (image.Pixels[i + 1] - mean) * (image.Pixels[i + 1] - mean);
        }

        Assert.True(Math.Sqrt(var / n) > 6, $"{id}: textura sem variação");
    }
}
