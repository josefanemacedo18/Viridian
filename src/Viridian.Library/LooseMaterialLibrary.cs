using Viridian.Core.Config;
using Viridian.Core.Paving;

namespace Viridian.Library;

/// <summary>Arquivo da biblioteca de pedras e coberturas.</summary>
public sealed class LooseMaterialFile
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Versão dos materiais padrão gravados (ver <see cref="LooseMaterialDefaults.Version"/>).</summary>
    public int SeedVersion { get; set; }

    public List<LooseMaterial> Materials { get; set; } = new();
}

/// <summary>
/// Biblioteca de pedras e coberturas soltas gravada em Dados\pedras.json, na pasta da biblioteca (compartilhável pelo
/// escritório). Materiais padrão não editados são atualizados com o plugin; os do escritório são preservados.
/// </summary>
public sealed class LooseMaterialLibrary
{
    private readonly string _file;

    public LooseMaterialLibrary(LibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = paths.StonesFile;
        Materials = Load();
    }

    public List<LooseMaterial> Materials { get; private set; }

    public LooseMaterial? Find(string? id) => Materials.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Todos os tipos de piso derivados (um por material e uso), para reconhecer os pisos do projeto.</summary>
    public IEnumerable<PavingType> PavingTypes() =>
        Materials.SelectMany(m => LooseLabels.AllUses.Where(u => m.Uses.HasFlag(u)).Select(u => LooseMaterialUse.ToPaving(m, u)));

    public void Save(LooseMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        material.Normalize();
        IReadOnlyList<string> problems = material.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }

        int index = Materials.FindIndex(m => string.Equals(m.Id, material.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Materials[index] = material;
        }
        else
        {
            Materials.Add(material);
        }

        Write(Materials);
    }

    public bool Delete(string id)
    {
        int removed = Materials.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Write(Materials);
        }

        return removed > 0;
    }

    /// <summary>Volta um material padrão aos valores originais (nulo para materiais criados pelo escritório).</summary>
    public LooseMaterial? Restore(string id)
    {
        LooseMaterial? seed = LooseMaterialDefaults.Create().FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (seed is null)
        {
            return null;
        }

        Save(seed);
        return seed;
    }

    private List<LooseMaterial> Load()
    {
        LooseMaterialFile? file = null;
        try
        {
            if (File.Exists(_file))
            {
                file = ViridianJson.Deserialize<LooseMaterialFile>(File.ReadAllText(_file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            ViridianLog.Warn("Biblioteca de pedras ilegível; recriada a partir do padrão: " + _file, ex);
            TryBackup();
        }

        (List<LooseMaterial> materials, bool changed) = Merge(file);
        if (changed)
        {
            try
            {
                Write(materials);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Biblioteca de pedras não pôde ser gravada: " + _file, ex);
            }
        }

        return materials;
    }

    /// <summary>Junta o arquivo com os materiais padrão. Público para os testes.</summary>
    public static (List<LooseMaterial> Materials, bool Changed) Merge(LooseMaterialFile? file)
    {
        List<LooseMaterial> seeds = LooseMaterialDefaults.Create();
        if (file?.Materials is null || file.Materials.Count == 0)
        {
            return (seeds, true);
        }

        var materials = file.Materials.Where(m => m is not null).ToList();
        foreach (LooseMaterial m in materials)
        {
            m.Normalize();
        }

        bool changed = false;
        if (file.SeedVersion < LooseMaterialDefaults.Version)
        {
            foreach (LooseMaterial seed in seeds)
            {
                int index = materials.FindIndex(m => string.Equals(m.Id, seed.Id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    materials.Add(seed);
                }
                else if (!materials[index].IsUserDefined)
                {
                    materials[index] = seed;
                }
            }

            changed = true;
        }

        return (materials, changed);
    }

    private void Write(List<LooseMaterial> materials)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        SafeFile.WriteAllTextAtomic(_file, ViridianJson.Serialize(new LooseMaterialFile { SeedVersion = LooseMaterialDefaults.Version, Materials = materials }));
    }

    private void TryBackup()
    {
        try
        {
            File.Copy(_file, _file + ".corrompido-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Warn("Cópia de segurança da biblioteca de pedras", ex);
        }
    }
}
