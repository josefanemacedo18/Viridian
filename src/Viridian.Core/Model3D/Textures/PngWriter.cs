using System.IO.Compression;
using System.Text;

namespace Viridian.Core.Model3D.Textures;

/// <summary>Imagem RGBA de 8 bits por canal, em memória.</summary>
public sealed class RgbaImage
{
    public RgbaImage(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Tamanho de imagem fora da faixa (1 a 8192).");
        }

        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>R, G, B, A por pixel, linha a linha de cima para baixo.</summary>
    public byte[] Pixels { get; }

    public (byte R, byte G, byte B, byte A) Get(int x, int y)
    {
        int i = Index(x, y);
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = Index(x, y);
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
        Pixels[i + 3] = a;
    }

    public void Fill(byte r, byte g, byte b, byte a = 255)
    {
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            Pixels[i] = r;
            Pixels[i + 1] = g;
            Pixels[i + 2] = b;
            Pixels[i + 3] = a;
        }
    }

    /// <summary>Coordenadas que dão a volta na borda (texturas que se repetem sem emenda).</summary>
    private int Index(int x, int y)
    {
        x = ((x % Width) + Width) % Width;
        y = ((y % Height) + Height) % Height;
        return (y * Width + x) * 4;
    }
}

/// <summary>Gravação de PNG sem dependências (RGBA 8 bits, sem entrelaçamento), para as texturas procedurais.</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)image.Width);
        WriteBigEndian(header, 4, (uint)image.Height);
        header[8] = 8; // bits por canal
        header[9] = 6; // RGBA
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        Chunk(output, "IHDR", header);

        // Cada linha começa com o filtro 0 (nenhum); o bloco todo vai comprimido em zlib.
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            int stride = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, y * stride, stride);
            }
        }

        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    public static void Save(RgbaImage image, string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllBytes(path, Encode(image));
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>CRC-32 do PNG (polinômio 0xEDB88320).</summary>
    public static uint Crc32(byte[] data) => Crc(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
