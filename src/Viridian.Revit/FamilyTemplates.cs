using System.IO;
using Viridian.Library;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Viridian.Revit;

/// <summary>Localiza os modelos de família (.rft) instalados com o Revit.</summary>
public static class FamilyTemplateLocator
{
    /// <summary>Modelos candidatos, do mais ao menos adequado. O primeiro é o configurado, quando existe.</summary>
    public static IReadOnlyList<string> Candidates(RevitApplication app, string? configuredFile) =>
        Candidates(app, configuredFile, TemplateNames.Score);

    /// <summary>Modelos de componente de detalhe 2D instalados com o Revit, do mais ao menos adequado.</summary>
    public static IReadOnlyList<string> DetailCandidates(RevitApplication app) =>
        Candidates(app, null, TemplateNames.DetailScore);

    private static IReadOnlyList<string> Candidates(RevitApplication app, string? configuredFile, Func<string?, int> scorer)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredFile) && File.Exists(configuredFile))
        {
            result.Add(configuredFile);
        }

        string preferredRoot = SafeGet(() => app.FamilyTemplatePath);
        var roots = new List<string>();
        AddRoot(roots, preferredRoot);
        AddRoot(roots, string.IsNullOrEmpty(preferredRoot) ? string.Empty : Path.GetDirectoryName(preferredRoot.TrimEnd('\\', '/')) ?? string.Empty);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string version = SafeGet(() => app.VersionNumber);
        if (programData.Length > 0 && version.Length > 0)
        {
            AddRoot(roots, Path.Combine(programData, "Autodesk", "RVT " + version, "Family Templates"));
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 4,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        var scored = new List<(string File, int Score, bool Preferred)>();
        var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.rft", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Não foi possível ler a pasta de modelos " + root, ex);
                continue;
            }

            foreach (string file in files)
            {
                if (!seen.Add(file))
                {
                    continue;
                }

                int score = scorer(Path.GetFileNameWithoutExtension(file));
                if (score > 0)
                {
                    bool preferred = preferredRoot.Length > 0 && file.StartsWith(preferredRoot, StringComparison.OrdinalIgnoreCase);
                    scored.Add((file, score, preferred));
                }
            }
        }

        result.AddRange(scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Preferred)
            .ThenBy(s => s.File.Length)
            .Select(s => s.File)
            .Take(16));
        return result;
    }

    private static void AddRoot(List<string> roots, string folder)
    {
        if (!string.IsNullOrWhiteSpace(folder)
            && Directory.Exists(folder)
            && !roots.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            roots.Add(folder);
        }
    }

    private static string SafeGet(Func<string> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

/// <summary>Nenhum modelo de família adequado foi encontrado; o usuário precisa indicar um arquivo .rft.</summary>
public sealed class FamilyTemplateMissingException : Exception
{
    public FamilyTemplateMissingException(string message)
        : base(message)
    {
    }
}
