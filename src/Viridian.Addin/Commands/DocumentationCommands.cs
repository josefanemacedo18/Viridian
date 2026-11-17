using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Integrity;
using Viridian.Core.Reports;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

internal static class DocumentationFlows
{
    public static void CreateLegend(ViridianContext ctx)
    {
        ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
        IReadOnlyList<LegendEntry> entries = DocumentationService.BuildLegendEntries(snapshot.Plants, snapshot.Massings, ctx.Lookup, ctx.Settings);
        ViewDrafting view = ctx.Grouped("Viridian: legenda", () => ctx.Documentation.CreateLegend(ctx.Doc, entries, ProjectReader.ProjectTitle(ctx.Doc)), _ => true);
        ctx.UiDocument.ActiveView = view;
    }

    public static void CreateSchedules(ViridianContext ctx)
    {
        ViewSchedule species = ctx.Grouped("Viridian: tabelas", () =>
        {
            ViewSchedule bySpecies = ctx.Documentation.CreateSpeciesSchedule(ctx.Doc);
            ctx.Documentation.CreatePlantSchedule(ctx.Doc);
            return bySpecies;
        }, _ => true);
        ctx.UiDocument.ActiveView = species;
    }

    public static IReadOnlyList<AcquisitionRow> Acquisition(ViridianContext ctx, ProjectSnapshot snapshot) =>
        ReportBuilder.BuildAcquisition(
            snapshot.Plants,
            snapshot.Massings,
            id => ctx.Lookup(id)?.UnitPrice ?? 0m,
            ctx.Settings.Quantities.IndividualReservePercent);
}

/// <summary>Relatórios de quantitativos com exportação para Excel.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ReportsCommand : CommandBase
{
    protected virtual int InitialTab => 0;

    internal override Result Run(ViridianContext ctx)
    {
        ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
        var window = new ReportsWindow(
            ProjectReader.ProjectTitle(ctx.Doc),
            ReportBuilder.BuildSpecies(snapshot.Plants, snapshot.Massings),
            ReportBuilder.BuildMassings(snapshot.Massings),
            DocumentationFlows.Acquisition(ctx, snapshot),
            InitialTab);
        if (!ctx.ShowDialog(window))
        {
            return Result.Succeeded;
        }

        switch (window.Action)
        {
            case ReportsAction.CreateSchedules:
                DocumentationFlows.CreateSchedules(ctx);
                break;
            case ReportsAction.CreateLegend:
                DocumentationFlows.CreateLegend(ctx);
                break;
        }

        return Result.Succeeded;
    }
}

/// <summary>Abre os relatórios diretamente na lista de aquisição.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class AcquisitionCommand : ReportsCommand
{
    protected override int InitialTab => 2;
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SchedulesCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        DocumentationFlows.CreateSchedules(ctx);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class LegendCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        DocumentationFlows.CreateLegend(ctx);
        return Result.Succeeded;
    }
}

/// <summary>Verificador de integridade e de conflitos de plantio, com correções automáticas.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CheckCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        for (int round = 0; round < 50; round++)
        {
            ctx.InvalidateProject();
            ProjectSnapshot snapshot = ProjectReader.Read(doc, ctx.Lookup);
            IntegrityInput input = ProjectReader.BuildIntegrityInput(doc, snapshot, ctx.Lookup, ctx.Settings, ctx.Families);
            IReadOnlyList<IntegrityIssue> issues = IntegrityChecker.Run(input);

            var window = new IntegrityWindow(issues, ids => ctx.ShowElements(ids));
            if (!ctx.ShowDialog(window))
            {
                return Result.Succeeded;
            }

            switch (window.Action)
            {
                case IntegrityAction.FixCodes:
                    FixCodes(ctx, issues);
                    break;
                case IntegrityAction.FixData:
                    ctx.Grouped("Viridian: atualizar dados", () =>
                    {
                        SyncCommand.RunSync(ctx);
                        MassingService.RefreshRecords(doc, ctx.Lookup, ctx.Project);
                        return ctx.Labels.UpdateAll(doc);
                    }, _ => true);
                    break;
                case IntegrityAction.FixOrphans:
                    ctx.Grouped("Viridian: limpar registros órfãos", () =>
                        MassingService.DetachOrphanMembers(doc) + LabelService.DeleteOrphans(doc), n => n > 0);
                    break;
                case IntegrityAction.Recheck:
                    break;
                default:
                    return Result.Succeeded;
            }
        }

        return Result.Succeeded;
    }

    private static void FixCodes(ViridianContext ctx, IReadOnlyList<IntegrityIssue> issues)
    {
        var missing = issues.Where(i => i.Kind == IssueKind.SemCodigo).SelectMany(i => i.ElementIds).Distinct().ToList();

        // Nos códigos repetidos, o primeiro exemplar mantém o código e os demais são renumerados.
        var duplicated = issues.Where(i => i.Kind == IssueKind.CodigoDuplicado)
            .SelectMany(i => i.ElementIds.OrderBy(id => id).Skip(1))
            .Distinct()
            .ToList();

        ctx.Grouped("Viridian: codificar exemplares", () =>
        {
            int count = ctx.Planting.AssignCodes(ctx.Doc, missing.Select(id => new ElementId(id)), false, i => CodeCommand.CategoryOf(ctx, i));
            count += ctx.Planting.AssignCodes(ctx.Doc, duplicated.Select(id => new ElementId(id)), true, i => CodeCommand.CategoryOf(ctx, i));
            ctx.Labels.UpdateAll(ctx.Doc);
            return count;
        }, n => n > 0);
    }
}
