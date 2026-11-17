using System.IO;
using Autodesk.Revit.DB;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Opções da exportação para DWG.</summary>
public sealed class CadExportRequest
{
    public string Folder { get; set; } = string.Empty;

    public IReadOnlyList<ElementId> ViewIds { get; set; } = Array.Empty<ElementId>();

    public ACADVersion Version { get; set; } = ACADVersion.R2018;

    /// <summary>Unidade do desenho: metros (verdadeiro) ou milímetros.</summary>
    public bool Meters { get; set; } = true;

    /// <summary>Usa as coordenadas compartilhadas do projeto (georreferenciamento).</summary>
    public bool SharedCoordinates { get; set; }

    /// <summary>Junta as vistas da prancha em um único arquivo, sem referências externas.</summary>
    public bool MergedViews { get; set; } = true;
}

/// <summary>Exportação de vistas e pranchas para DWG com as cores e os preenchimentos da vista.</summary>
public static class CadExportService
{
    /// <summary>Vistas que podem ser exportadas: pranchas, plantas e vistas de desenho.</summary>
    public static IReadOnlyList<View> ExportableViews(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate && v.CanBePrinted && v is ViewSheet or ViewPlan or ViewDrafting)
            .OrderBy(v => v is ViewSheet ? 0 : v is ViewPlan ? 1 : 2)
            .ThenBy(v => v is ViewSheet s ? s.SheetNumber : v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string DisplayName(View view) => view switch
    {
        ViewSheet sheet => $"Prancha {sheet.SheetNumber} - {sheet.Name}",
        ViewPlan => "Planta: " + view.Name,
        _ => "Desenho: " + view.Name,
    };

    /// <summary>Exporta cada vista em um arquivo próprio. Não pode haver transação aberta.</summary>
    /// <returns>Arquivos gravados e problemas encontrados.</returns>
    public static (IReadOnlyList<string> Files, IReadOnlyList<string> Problems) ExportDwg(Document document, CadExportRequest request)
    {
        if (document.IsModifiable)
        {
            throw new InvalidOperationException("A exportação não pode ser feita com uma transação aberta.");
        }

        Directory.CreateDirectory(request.Folder);
        var options = new DWGExportOptions
        {
            FileVersion = request.Version,
            MergedViews = request.MergedViews,
            SharedCoords = request.SharedCoordinates,
            TargetUnit = request.Meters ? ExportUnit.Meter : ExportUnit.Millimeter,
            Colors = ExportColorMode.TrueColorPerView,
            PropOverrides = PropOverrideMode.ByEntity,
            HideScopeBox = true,
            HideReferencePlane = true,
            HideUnreferenceViewTags = true,
        };

        var files = new List<string>();
        var problems = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ElementId id in request.ViewIds)
        {
            if (document.GetElement(id) is not View view)
            {
                continue;
            }

            string baseName = SafeFile.SanitizeName(view is ViewSheet sheet ? $"{sheet.SheetNumber} - {sheet.Name}" : view.Name, 90);
            string name = baseName;
            for (int i = 2; !used.Add(name); i++)
            {
                name = $"{baseName} ({i})";
            }

            try
            {
                if (document.Export(request.Folder, name, new List<ElementId> { id }, options))
                {
                    files.Add(Path.Combine(request.Folder, name + ".dwg"));
                }
                else
                {
                    problems.Add(DisplayName(view) + ": o Revit não exportou a vista.");
                }
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException or IOException or UnauthorizedAccessException)
            {
                ViridianLog.Error("Falha ao exportar " + view.Name, ex);
                problems.Add(DisplayName(view) + ": " + ex.Message);
            }
        }

        return (files, problems);
    }
}
