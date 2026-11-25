using Viridian.Core.Composition;
using Viridian.Core.Config;
using Viridian.Core.Model;

namespace Viridian.Library;

/// <summary>Arquivo dos modelos de canteiro.</summary>
public sealed class BedTemplateFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<BedTemplate> Templates { get; set; } = new();
}

/// <summary>
/// Modelos de canteiro gravados em Dados\modelos-canteiro.json, na pasta da biblioteca: ficam disponíveis em todos os
/// projetos e, com a biblioteca na rede, para todo o escritório.
/// </summary>
public sealed class BedTemplateLibrary
{
    private readonly string _file;

    public BedTemplateLibrary(LibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = paths.BedTemplatesFile;
        Templates = Load();
    }

    public List<BedTemplate> Templates { get; private set; }

    public string File => _file;

    public BedTemplate? Find(string? id) => Templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Grava (ou substitui, pelo identificador) um modelo. Nome repetido recebe um sufixo.</summary>
    public void Save(BedTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.Name = (template.Name ?? string.Empty).Trim();
        IReadOnlyList<string> problems = template.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }

        string baseName = template.Name;
        for (int n = 2; Templates.Any(t => !string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(t.Name, template.Name, StringComparison.CurrentCultureIgnoreCase)); n++)
        {
            template.Name = $"{baseName} ({n})";
        }

        int index = Templates.FindIndex(t => string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Templates[index] = template;
        }
        else
        {
            Templates.Add(template);
        }

        Write();
    }

    public bool Delete(string id)
    {
        int removed = Templates.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Write();
        }

        return removed > 0;
    }

    /// <summary>
    /// Reencontra a espécie de um modelo no catálogo atual: pelo identificador e, se não houver (outro escritório ou
    /// catálogo), pelo nome científico.
    /// </summary>
    public static Species? Resolve(CatalogService catalog, string speciesId, string scientificName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Find(speciesId) is Species byId)
        {
            return byId;
        }

        string key = TextSearch.Normalize((scientificName ?? string.Empty).Trim());
        return key.Length == 0 ? null : catalog.All.FirstOrDefault(s => TextSearch.Normalize(s.ScientificName.Trim()) == key);
    }

    private List<BedTemplate> Load()
    {
        try
        {
            if (System.IO.File.Exists(_file))
            {
                return ViridianJson.Deserialize<BedTemplateFile>(System.IO.File.ReadAllText(_file))?.Templates ?? new List<BedTemplate>();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            ViridianLog.Warn("Modelos de canteiro ilegíveis; guardada uma cópia e começada uma lista nova: " + _file, ex);
            try
            {
                System.IO.File.Copy(_file, _file + ".ilegivel-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture), overwrite: true);
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Cópia dos modelos ilegíveis não pôde ser gravada.", copy);
            }
        }

        return new List<BedTemplate>();
    }

    private void Write()
    {
        string? folder = Path.GetDirectoryName(_file);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        SafeFile.WriteAllTextAtomic(_file, ViridianJson.Serialize(new BedTemplateFile { Templates = Templates }));
    }
}
