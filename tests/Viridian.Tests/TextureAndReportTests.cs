using System.IO.Compression;
using System.Text;
using Viridian.Core.Colors;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Textures;

namespace Viridian.Tests;

public class TextureAndReportTests
{
    [Fact]
    public void Png_HasValidSignature_Chunks_AndCrc()
    {
        var image = new RgbaImage(3, 2);
        image.Set(0, 0, 255, 0, 0);
        image.Set(2, 1, 0, 0, 255, 128);
        byte[] png = PngWriter.Encode(image);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());

        // Percorre os blocos conferindo o CRC de cada um.
        int offset = 8;
        var types = new List<string>();
        byte[]? idat = null;
        while (offset < png.Length)
        {
            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            byte[] typeAndData = png.Skip(offset + 4).Take(4 + length).ToArray();
            uint crc = (uint)((png[offset + 8 + length] << 24) | (png[offset + 9 + length] << 16) | (png[offset + 10 + length] << 8) | png[offset + 11 + length]);
            Assert.Equal(PngWriter.Crc32(typeAndData), crc);
            if (type == "IDAT")
            {
                idat = png.Skip(offset + 8).Take(length).ToArray();
            }

            types.Add(type);
            offset += 12 + length;
        }

        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, types);
        Assert.Equal(0xAE426082u, PngWriter.Crc32(Encoding.ASCII.GetBytes("IEND"))); // valor conhecido do bloco vazio

        // Os dados descomprimidos são as linhas com o filtro 0 e os pixels originais.
        using var inflated = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(idat!), CompressionMode.Decompress))
        {
            z.CopyTo(inflated);
        }

        byte[] raw = inflated.ToArray();
        Assert.Equal(2 * (1 + 3 * 4), raw.Length);
        Assert.Equal(0, raw[0]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, raw.Skip(1).Take(4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 128 }, raw.Skip(13 + 1 + 8).Take(4).ToArray());
    }

    [Fact]
    public void Image_WrapsAroundEdges_ForSeamlessTiles()
    {
        var image = new RgbaImage(4, 4);
        image.Set(-1, -1, 10, 20, 30);
        Assert.Equal((10, 20, 30, 255), image.Get(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(0, 10));
    }

    [Fact]
    public void ProceduralTextures_AreDeterministic_AndHaveReasonableCoverage()
    {
        (RgbaImage a, RgbaImage maskA) = ProceduralTextures.Foliage(128, new Rgb(0x4E, 0x7D, 0x3A), 5);
        (RgbaImage b, RgbaImage maskB) = ProceduralTextures.Foliage(128, new Rgb(0x4E, 0x7D, 0x3A), 5);
        Assert.Equal(a.Pixels, b.Pixels);
        Assert.Equal(maskA.Pixels, maskB.Pixels);
        double coverage = ProceduralTextures.Coverage(maskA);
        Assert.InRange(coverage, 0.35, 0.85);
        (_, RgbaImage large) = ProceduralTextures.Foliage(512, new Rgb(0x4E, 0x7D, 0x3A), 5);
        Assert.InRange(ProceduralTextures.Coverage(large), coverage - 0.12, coverage + 0.12); // mesma cobertura em outra resolução

        (RgbaImage c, _) = ProceduralTextures.Foliage(128, new Rgb(0x4E, 0x7D, 0x3A), 6);
        Assert.NotEqual(a.Pixels, c.Pixels);

        (_, RgbaImage flowerMask) = ProceduralTextures.Flowers(128, new Rgb(0xF2, 0xC2, 0x1E), 1);
        Assert.InRange(ProceduralTextures.Coverage(flowerMask), 0.05, 0.8);

        RgbaImage bark = ProceduralTextures.Bark(64, new Rgb(0x6E, 0x63, 0x58), 1);
        Assert.All(Enumerable.Range(0, 64 * 64), i => Assert.Equal(255, bark.Pixels[i * 4 + 3]));
    }

    [Fact]
    public void Report_RunsSteps_CatchesFailures_AndFormatsText()
    {
        var report = new EngineTestReport("Teste");
        report.Environment["Revit"] = "2027";
        int value = report.Run("etapa boa", () => (42, TestOutcome.Ok, "tudo certo"));
        string? missing = report.Run<string>("etapa ruim", () => throw new InvalidOperationException("quebrou"));
        report.Add("aviso", TestOutcome.Aviso, "linha 1\nlinha 2");
        Assert.Equal(42, value);
        Assert.Null(missing);
        Assert.True(report.Failed);
        Assert.Equal(1, report.Count(TestOutcome.Ok));
        string text = report.ToText();
        Assert.Contains("[OK]", text);
        Assert.Contains("[FALHA] etapa ruim", text);
        Assert.Contains("InvalidOperationException: quebrou", text);
        Assert.Contains("        linha 2", text);
        Assert.Contains("Revit: 2027", text);
        Assert.Contains("1 etapa(s) aprovada(s), 1 aviso(s), 1 falha(s).", text);
    }
}
