using Autodesk.Revit.DB;
using Viridian.Core.Config;
using Viridian.Core.Integrity;
using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.Revit;

/// <summary>Leitura consolidada dos dados de vegetação de um projeto.</summary>
public sealed class ProjectSnapshot
{
    public IReadOnlyList<PlantRecord> Plants { get; init; } = Array.Empty<PlantRecord>();

    public IReadOnlyList<MassingInfo> Massings { get; init; } = Array.Empty<MassingInfo>();

    public ProjectData Project { get; init; } = new();
}

public static class ProjectReader
{
    /// <summary>Lê exemplares e maciços, completando a contagem de símbolos de cada maciço.</summary>
    public static ProjectSnapshot Read(Document document, Func<string, Species?> lookup)
    {
        IReadOnlyList<PlantRecord> plants = PlantingService.ReadPlants(document, lookup);
        IReadOnlyList<MassingInfo> massings = MassingService.ReadMassings(document);
        ILookup<string, PlantRecord> byMassing = plants
            .Where(p => p.BelongsToMassing)
            .ToLookup(p => p.MassingId.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (MassingInfo massing in massings)
        {
            massing.LinkedSymbolCount = byMassing[massing.Record.Id.Trim()].Count();
        }

        return new ProjectSnapshot
        {
            Plants = plants,
            Massings = massings,
            Project = ElementData.ReadProject(document),
        };
    }

    /// <summary>Monta os dados submetidos ao verificador de integridade.</summary>
    public static IntegrityInput BuildIntegrityInput(
        Document document,
        ProjectSnapshot snapshot,
        Func<string, Species?> lookup,
        ViridianSettings settings,
        SpeciesFamilyService families)
    {
        List<OutdatedType> outdated = families.FindOutdated(document, lookup, snapshot.Project)
            .Select(x => new OutdatedType
            {
                ElementId = x.Symbol.Id.Value,
                Name = x.Symbol.FamilyName + " : " + x.Symbol.Name,
                Reason = x.Reason,
            })
            .ToList();

        return new IntegrityInput
        {
            Plants = snapshot.Plants,
            Massings = snapshot.Massings,
            OrphanLabels = LabelService.FindOrphans(document),
            OutdatedTypes = outdated,
            SpeciesLookup = lookup,
            Settings = settings.Analysis,
        };
    }

    /// <summary>Título do projeto para cabeçalhos de relatórios e legendas.</summary>
    public static string ProjectTitle(Document document)
    {
        try
        {
            ProjectInfo? info = document.ProjectInformation;
            string name = info?.Name ?? string.Empty;
            string number = info?.Number ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(number))
            {
                return number.Trim() + " — " + name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Sem informações de projeto: usa o nome do arquivo.
        }

        return document.Title ?? string.Empty;
    }
}
