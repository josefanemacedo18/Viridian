using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Viridian.Core.Model;
using Viridian.Library;

namespace Viridian.UI;

/// <summary>
/// Importa a fotografia de uma espécie para a pasta de fotos da biblioteca (compartilhada pelo escritório): a imagem é
/// reduzida para no máximo 1600 px e gravada em JPEG com nome único (a ficha passa a mostrar a foto nova na hora).
/// </summary>
public static class PhotoImport
{
    public const int MaxSize = 1600;

    private static readonly Lazy<Dictionary<string, Species>> Seeds = new(() =>
        SeedCatalog.CreateSpecies().GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

    public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    public static bool IsImageFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path);

    /// <summary>Importa um arquivo de imagem. Retorna o nome gravado na biblioteca.</summary>
    public static string FromFile(LibraryPaths paths, string speciesId, string file)
    {
        BitmapSource source;
        using (FileStream stream = File.OpenRead(file))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            source = decoder.Frames[0];
        }

        return Save(paths, speciesId, source);
    }

    /// <summary>Importa a imagem da área de transferência (imagem copiada no navegador ou arquivo copiado).</summary>
    public static string? FromClipboard(LibraryPaths paths, string speciesId)
    {
        if (Clipboard.ContainsFileDropList())
        {
            string? file = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(IsImageFile);
            if (file is not null)
            {
                return FromFile(paths, speciesId, file);
            }
        }

        if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image)
        {
            return Save(paths, speciesId, image);
        }

        return null;
    }

    private static string Save(LibraryPaths paths, string speciesId, BitmapSource source)
    {
        double scale = Math.Min(1.0, MaxSize / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource image = scale < 1.0 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
        if (image.Format != PixelFormats.Bgr24 && image.Format != PixelFormats.Bgr32)
        {
            // Transparência (PNG) sobre fundo branco, para o JPEG não ficar com fundo preto.
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            }

            var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            image = target;
        }

        Directory.CreateDirectory(paths.PhotosFolder);
        string safeId = new(speciesId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        string name = $"{(safeId.Length == 0 ? "especie" : safeId)}-{DateTime.Now:yyyyMMddHHmmss}.jpg";
        string path = Path.Combine(paths.PhotosFolder, name);
        var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
        encoder.Frames.Add(BitmapFrame.Create(image));
        string temporary = path + ".tmp";
        using (FileStream output = File.Create(temporary))
        {
            encoder.Save(output);
        }

        File.Move(temporary, path, overwrite: true);
        return name;
    }

    /// <summary>Aplica a foto importada à espécie (foto própria do escritório).</summary>
    public static void Assign(Species species, string fileName, string credit)
    {
        species.PhotoFile = fileName;
        species.PhotoCredit = string.IsNullOrWhiteSpace(credit) ? "Foto do escritório" : credit.Trim();
        species.PhotoLicense = string.Empty;
        species.PhotoSource = string.Empty;
    }

    /// <summary>Volta à foto do catálogo padrão do Viridian; falso quando a espécie não é do catálogo padrão.</summary>
    public static bool RestoreSeed(Species species)
    {
        if (!Seeds.Value.TryGetValue(species.Id, out Species? seed) || string.IsNullOrWhiteSpace(seed.PhotoFile))
        {
            return false;
        }

        species.PhotoFile = seed.PhotoFile;
        species.PhotoCredit = seed.PhotoCredit;
        species.PhotoLicense = seed.PhotoLicense;
        species.PhotoSource = seed.PhotoSource;
        return true;
    }

    public static bool HasSeedPhoto(Species species) =>
        Seeds.Value.TryGetValue(species.Id, out Species? seed) && !string.IsNullOrWhiteSpace(seed.PhotoFile);
}
