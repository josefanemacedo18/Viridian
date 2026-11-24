using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.Library;

/// <summary>Arquivo da biblioteca de perfis botânicos 3D.</summary>
public sealed class Plant3DProfileFile
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Versão dos perfis padrão gravados (ver <see cref="Plant3DProfileDefaults.Version"/>).</summary>
    public int SeedVersion { get; set; }

    public List<Plant3DProfile> Profiles { get; set; } = new();
}

/// <summary>
/// Perfis botânicos 3D em Dados\perfis-3d.json, na pasta da biblioteca (compartilhável pelo escritório). Perfis padrão
/// não editados são atualizados com o plugin; perfis do escritório são preservados. Arquivo ilegível é guardado como
/// cópia de segurança e substituído pelos perfis padrão.
/// </summary>
public sealed class Plant3DProfileLibrary
{
    private readonly string _file;

    public Plant3DProfileLibrary(LibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = paths.Profiles3DFile;
        Profiles = Load();
    }

    /// <summary>Biblioteca em memória (testes e prévias sem pasta de biblioteca).</summary>
    public Plant3DProfileLibrary(IEnumerable<Plant3DProfile> profiles)
    {
        _file = string.Empty;
        Profiles = profiles.ToList();
    }

    public List<Plant3DProfile> Profiles { get; private set; }

    public Plant3DProfile? Find(string? id) => Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public (Plant3DProfile Profile, bool FromSpecies) Resolve(Species species) => ProfileResolver.Resolve(species, Profiles);

    /// <summary>Quantas espécies usam o perfil (para avisar que a edição altera todas).</summary>
    public int UsageCount(string id, IEnumerable<Species> catalog) =>
        catalog.Count(s => string.Equals(Resolve(s).Profile.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Save(Plant3DProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Normalize();
        IReadOnlyList<string> problems = profile.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }

        int index = Profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Profiles[index] = profile;
        }
        else
        {
            Profiles.Add(profile);
        }

        Write(Profiles);
    }

    private List<Plant3DProfile> Load()
    {
        Plant3DProfileFile? file = null;
        try
        {
            if (File.Exists(_file))
            {
                file = ViridianJson.Deserialize<Plant3DProfileFile>(File.ReadAllText(_file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
        {
            ViridianLog.Warn("Perfis 3D ilegíveis; recriados a partir do padrão: " + _file, ex);
            TryBackup();
        }

        (List<Plant3DProfile> profiles, bool changed) = Merge(file);
        if (changed)
        {
            try
            {
                Write(profiles);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Perfis 3D não puderam ser gravados: " + _file, ex);
            }
        }

        return profiles;
    }

    /// <summary>
    /// Junta o arquivo com os perfis padrão. Perfis inválidos (esquema mais novo, campos obrigatórios ausentes) são
    /// descartados com registro; os padrão voltam no lugar. Público para os testes.
    /// </summary>
    public static (List<Plant3DProfile> Profiles, bool Changed) Merge(Plant3DProfileFile? file)
    {
        List<Plant3DProfile> seeds = Plant3DProfileDefaults.Create();
        if (file?.Profiles is null || file.Profiles.Count == 0)
        {
            return (seeds, true);
        }

        bool changed = false;
        var profiles = new List<Plant3DProfile>();
        foreach (Plant3DProfile? p in file.Profiles)
        {
            if (p is null)
            {
                changed = true;
                continue;
            }

            p.Normalize();
            IReadOnlyList<string> problems = p.Validate();
            if (problems.Count > 0)
            {
                ViridianLog.Warn($"Perfil 3D \"{p.Id}\" ignorado: {string.Join(" ", problems)}");
                changed = true;
                continue;
            }

            if (profiles.Any(o => string.Equals(o.Id, p.Id, StringComparison.OrdinalIgnoreCase)))
            {
                changed = true;
                continue;
            }

            profiles.Add(p);
        }

        foreach (Plant3DProfile seed in seeds)
        {
            int index = profiles.FindIndex(p => string.Equals(p.Id, seed.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                profiles.Add(seed);
                changed = true;
            }
            else if (file.SeedVersion < Plant3DProfileDefaults.Version && !profiles[index].IsUserDefined)
            {
                profiles[index] = seed;
                changed = true;
            }
        }

        return (profiles, changed || file.SeedVersion < Plant3DProfileDefaults.Version);
    }

    private void Write(List<Plant3DProfile> profiles)
    {
        if (_file.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        SafeFile.WriteAllTextAtomic(_file, ViridianJson.Serialize(new Plant3DProfileFile { SeedVersion = Plant3DProfileDefaults.Version, Profiles = profiles }));
    }

    private void TryBackup()
    {
        try
        {
            File.Copy(_file, _file + ".corrompido-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Warn("Cópia de segurança dos perfis 3D", ex);
        }
    }
}
