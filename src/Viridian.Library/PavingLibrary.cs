using Viridian.Core.Config;
using Viridian.Core.Paving;

namespace Viridian.Library;

/// <summary>Arquivo da biblioteca de pisos.</summary>
public sealed class PavingLibraryFile
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Versão dos tipos padrão gravados (ver <see cref="PavingDefaults.Version"/>).</summary>
    public int SeedVersion { get; set; }

    public List<PavingType> Types { get; set; } = new();
}

/// <summary>
/// Biblioteca de pisos gravada em Dados\pisos.json, na pasta da biblioteca (compartilhável pelo escritório).
/// Tipos padrão não editados são atualizados quando o plugin traz uma versão nova; os do escritório são preservados.
/// </summary>
public sealed class PavingLibrary
{
    private readonly string _file;

    public PavingLibrary(LibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = paths.PavingFile;
        Types = Load();
    }

    public List<PavingType> Types { get; private set; }

    public PavingType? Find(string? id) => Types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Grava (inclui ou substitui) um tipo e salva o arquivo.</summary>
    public void Save(PavingType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        type.Normalize();
        IReadOnlyList<string> problems = type.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }

        int index = Types.FindIndex(t => string.Equals(t.Id, type.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Types[index] = type;
        }
        else
        {
            Types.Add(type);
        }

        Write();
    }

    public bool Delete(string id)
    {
        int removed = Types.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Write();
        }

        return removed > 0;
    }

    /// <summary>Volta um tipo padrão aos valores originais (falso para tipos criados pelo escritório).</summary>
    public PavingType? Restore(string id)
    {
        PavingType? seed = PavingDefaults.Create().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (seed is null)
        {
            return null;
        }

        Save(seed);
        return seed;
    }

    private List<PavingType> Load()
    {
        PavingLibraryFile? file = null;
        try
        {
            if (File.Exists(_file))
            {
                file = ViridianJson.Deserialize<PavingLibraryFile>(File.ReadAllText(_file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            ViridianLog.Warn("Biblioteca de pisos ilegível; recriada a partir do padrão: " + _file, ex);
            TryBackup();
        }

        (List<PavingType> types, bool changed) = Merge(file);
        if (changed)
        {
            try
            {
                Write(types);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Biblioteca de pisos não pôde ser gravada: " + _file, ex);
            }
        }

        return types;
    }

    /// <summary>Junta o arquivo com os tipos padrão. Público para os testes.</summary>
    public static (List<PavingType> Types, bool Changed) Merge(PavingLibraryFile? file)
    {
        List<PavingType> seeds = PavingDefaults.Create();
        if (file is null || file.Types is null || file.Types.Count == 0)
        {
            return (seeds, true);
        }

        var types = file.Types.Where(t => t is not null).ToList();
        foreach (PavingType t in types)
        {
            t.Normalize();
        }

        bool changed = false;
        if (file.SeedVersion < PavingDefaults.Version)
        {
            foreach (PavingType seed in seeds)
            {
                int index = types.FindIndex(t => string.Equals(t.Id, seed.Id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    types.Add(seed);
                }
                else if (!types[index].IsUserDefined)
                {
                    types[index] = seed;
                }
            }

            changed = true;
        }

        return (types, changed);
    }

    private void Write() => Write(Types);

    private void Write(List<PavingType> types)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        SafeFile.WriteAllTextAtomic(_file, ViridianJson.Serialize(new PavingLibraryFile { SeedVersion = PavingDefaults.Version, Types = types }));
    }

    private void TryBackup()
    {
        try
        {
            File.Copy(_file, _file + ".corrompido-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Warn("Cópia de segurança da biblioteca de pisos", ex);
        }
    }
}
