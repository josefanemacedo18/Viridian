using Viridian.Core.Model;

namespace Viridian.Library;

/// <summary>Estrutura de pastas do Viridian em disco.</summary>
public sealed class LibraryPaths
{
    public LibraryPaths(string? libraryRoot = null)
    {
        Root = string.IsNullOrWhiteSpace(libraryRoot) ? DefaultRoot : libraryRoot.Trim();
    }

    /// <summary>Pasta do usuário, sempre local: guarda configurações e registros de erro.</summary>
    public static string UserRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Viridian");

    public static string DefaultRoot => Path.Combine(UserRoot, "Biblioteca");

    public static string ConfigFolder => Path.Combine(UserRoot, "Config");

    public static string SettingsFile => Path.Combine(ConfigFolder, "viridian.config.json");

    public static string SharedParametersFile => Path.Combine(ConfigFolder, "Viridian_ParametrosCompartilhados.txt");

    public static string LogFolder => Path.Combine(UserRoot, "Logs");

    /// <summary>Raiz da biblioteca (pode ser uma pasta de rede compartilhada pelo escritório).</summary>
    public string Root { get; }

    public string DataFolder => Path.Combine(Root, "Dados");

    public string CatalogFile => Path.Combine(DataFolder, "catalogo.json");

    /// <summary>Famílias 2D geradas pelo plugin, organizadas por categoria.</summary>
    public string GeneratedFolder(PlantCategory category) => Path.Combine(Root, "Familias", Labels.Folder(category));

    public string GeneratedIndexFile => Path.Combine(Root, "Familias", "indice.json");

    public string CustomFamiliesFolder => Path.Combine(Root, "Personalizado", "Familias");

    public string CustomSymbolsFolder => Path.Combine(Root, "Personalizado", "Simbolos");

    public string CustomHatchesFolder => Path.Combine(Root, "Personalizado", "Hachuras");

    public string CustomTagsFolder => Path.Combine(Root, "Personalizado", "Etiquetas");

    public string PhotosFolder => Path.Combine(Root, "Fotos");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataFolder);
        foreach (PlantCategory category in Labels.Categories)
        {
            Directory.CreateDirectory(GeneratedFolder(category));
        }

        Directory.CreateDirectory(CustomFamiliesFolder);
        Directory.CreateDirectory(CustomSymbolsFolder);
        Directory.CreateDirectory(CustomHatchesFolder);
        Directory.CreateDirectory(CustomTagsFolder);
        Directory.CreateDirectory(PhotosFolder);
    }

    /// <summary>Converte um caminho gravado no catálogo (relativo à biblioteca) em caminho completo.</summary>
    public string Resolve(string storedPath, string baseFolder)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(storedPath) ? storedPath : Path.Combine(baseFolder, storedPath);
    }
}

/// <summary>Operações de arquivo seguras: gravação atômica e nomes válidos.</summary>
public static class SafeFile
{
    /// <summary>Grava o texto em arquivo temporário e só então substitui o destino, evitando arquivos truncados.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        File.WriteAllText(temp, content, new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Remove caracteres inválidos para nomes de arquivo e de famílias do Revit.</summary>
    public static string SanitizeName(string name, int maxLength = 80)
    {
        const string forbidden = "\\/:*?\"<>|{}[];`~";
        var chars = (name ?? string.Empty)
            .Select(c => forbidden.Contains(c) || char.IsControl(c) ? ' ' : c)
            .ToArray();
        string cleaned = string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].Trim();
        }

        return cleaned.Length == 0 ? "sem-nome" : cleaned;
    }
}

/// <summary>Registro de erros em arquivo, para diagnóstico sem depender do Revit.</summary>
public static class ViridianLog
{
    private static readonly object Gate = new();

    public static string CurrentFile => Path.Combine(LibraryPaths.LogFolder, $"viridian-{DateTime.Now:yyyy-MM}.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("AVISO", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERRO", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LibraryPaths.LogFolder);
                using var writer = new StreamWriter(CurrentFile, append: true, new System.Text.UTF8Encoding(false));
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
                if (exception is not null)
                {
                    writer.WriteLine(exception.ToString());
                }
            }
        }
        catch (Exception)
        {
            // O registro de erros nunca pode derrubar um comando.
        }
    }
}
