using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Export;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Core.Sheets;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Prancha automática: planta ativa, legenda e tabela diagramadas sobre o carimbo escolhido.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SheetCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan plan = ctx.RequirePlanView();
        Document doc = ctx.Doc;
        ProjectSnapshot snapshot = ProjectReader.Read(doc, ctx.Lookup);
        IReadOnlyList<LegendEntry> entries = DocumentationService.BuildLegendEntries(snapshot.Plants, snapshot.Massings, ctx.Lookup, ctx.Settings);

        List<(string Label, long Id)> titleBlocks = SheetService.TitleBlocks(doc)
            .Select(t => (t.FamilyName + " : " + t.Name, t.Id.Value))
            .ToList();
        var window = new SheetWindow(
            $"Vista: {plan.Name} · escala 1:{plan.Scale}",
            titleBlocks,
            SheetNumbering.Next(SheetService.SheetNumbers(doc)),
            entries.Count > 0);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        SheetOptions options = window.Options;
        SheetOutcome outcome = ctx.Grouped("Viridian: prancha de paisagismo", () =>
        {
            ViewDrafting? legend = options.IncludeLegend && entries.Count > 0
                ? ctx.Documentation.CreateLegend(doc, entries, ProjectReader.ProjectTitle(doc))
                : null;
            ViewSchedule? schedule = options.IncludeSchedule ? ctx.Documentation.CreateSpeciesSchedule(doc) : null;
            if (options.IncludePhotoBoard)
            {
                IReadOnlyList<PhotoBoardSpecies> board = DocumentationFlows.BoardSpecies(ctx, plan);
                if (board.Count > 0)
                {
                    Tx.Run(doc, "Viridian: quadro de fotos", () => ctx.Photos.CreateBoard(doc, plan, board), suppressWarnings: true);
                }
            }

            var request = new SheetRequest
            {
                TitleBlockTypeId = options.TitleBlockId > 0 ? new ElementId(options.TitleBlockId) : ElementId.InvalidElementId,
                ViridianFrame = options.TitleBlockId == -2,
                NorthDeg = SolarService.EffectiveNorth(doc, ctx.Settings.Solar).Degrees,
                TextMm = ctx.Settings.Labels.TextSizeMm,
                Number = options.Number,
                Name = options.Name,
                FitScale = options.FitScale,
                StampReserveMm = options.StampReserveMm,
            };
            return Tx.Run(doc, "Viridian: montar prancha", () => SheetService.Create(doc, plan, legend, schedule, request));
        }, _ => true);

        ctx.UiDocument.ActiveView = outcome.Sheet;
        string summary = $"Prancha {outcome.Sheet.SheetNumber} - {outcome.Sheet.Name} criada.";
        if (outcome.Messages.Count > 0)
        {
            summary += "\n\n" + string.Join("\n\n", outcome.Messages);
        }

        TaskDialog.Show("Viridian", summary);
        return Result.Succeeded;
    }
}

/// <summary>Exportação para CAD: DWG das vistas e pranchas e biblioteca DXF de blocos de símbolos.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CadExportCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        ElementId active = ctx.UiDocument.ActiveView.Id;
        List<ExportViewItem> items = CadExportService.ExportableViews(doc)
            .Select(v => new ExportViewItem(v.Id.Value, CadExportService.DisplayName(v), v is ViewSheet, v.Id == active))
            .ToList();

        string folder = string.IsNullOrEmpty(doc.PathName)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Viridian CAD")
            : Path.Combine(Path.GetDirectoryName(doc.PathName) ?? string.Empty, "CAD - " + SafeFile.SanitizeName(Path.GetFileNameWithoutExtension(doc.PathName), 60));
        var window = new CadExportWindow(items, folder);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        CadExportOptions options = window.Options;
        var files = new List<string>();
        var problems = new List<string>();
        try
        {
            Directory.CreateDirectory(options.Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ViridianException("Não foi possível criar a pasta de destino:\n" + ex.Message, ex);
        }

        if (options.ViewIds.Count > 0)
        {
            var request = new CadExportRequest
            {
                Folder = options.Folder,
                ViewIds = options.ViewIds.Select(id => new ElementId(id)).ToList(),
                Version = options.Version switch
                {
                    2007 => ACADVersion.R2007,
                    2010 => ACADVersion.R2010,
                    2013 => ACADVersion.R2013,
                    _ => ACADVersion.R2018,
                },
                Meters = options.Meters,
                MergedViews = options.Merged,
                SharedCoordinates = options.SharedCoordinates,
            };
            (IReadOnlyList<string> exported, IReadOnlyList<string> failed) = CadExportService.ExportDwg(doc, request);
            files.AddRange(exported);
            problems.AddRange(failed);
        }

        if (options.Library)
        {
            List<(Species Species, string Code)> species = LibrarySpecies(ctx, options.LibraryWholeCatalog);
            if (species.Count == 0)
            {
                problems.Add("Biblioteca de blocos: não há espécies do catálogo no projeto.");
            }
            else
            {
                string file = Path.Combine(options.Folder, options.LibraryWholeCatalog ? "Viridian - blocos do catálogo.dxf" : "Viridian - blocos do projeto.dxf");
                try
                {
                    SymbolDxf.Build(species, ctx.Settings, options.LibraryFill).Save(file);
                    files.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ViridianLog.Error("Falha ao gravar a biblioteca DXF.", ex);
                    problems.Add("Biblioteca de blocos: " + ex.Message);
                }
            }
        }

        string message = files.Count == 0
            ? "Nenhum arquivo foi gerado."
            : $"{files.Count} arquivo(s) gerado(s) em:\n{options.Folder}";
        if (problems.Count > 0)
        {
            message += "\n\nProblemas:\n" + string.Join("\n", problems);
        }

        var dialog = new TaskDialog("Viridian — Exportar para CAD")
        {
            MainInstruction = files.Count > 0 ? "Exportação concluída" : "Exportação não realizada",
            MainContent = message,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        if (files.Count > 0)
        {
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir a pasta");
        }

        if (dialog.Show() == TaskDialogResult.CommandLink1)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(options.Folder) { UseShellExecute = true });
        }

        return Result.Succeeded;
    }

    /// <summary>Espécies para a biblioteca: as do projeto (com o código do projeto) ou o catálogo inteiro.</summary>
    private static List<(Species, string)> LibrarySpecies(ViridianContext ctx, bool wholeCatalog)
    {
        if (wholeCatalog)
        {
            return ctx.Catalog.All
                .Where(s => !s.HasCustomPlantingFamily)
                .OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
                .Select(s => (s, s.Code))
                .ToList();
        }

        ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
        IEnumerable<string> ids = snapshot.Plants.Where(p => p.IsRegistered).Select(p => p.SpeciesId)
            .Concat(snapshot.Massings.Select(m => m.Record.SpeciesId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return ids
            .Select(ctx.Lookup)
            .OfType<Species>()
            .Where(s => !s.HasCustomPlantingFamily)
            .Select(s => (s, ctx.ProjectCode(s)))
            .OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
