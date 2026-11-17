using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Config;
using Viridian.Core.Integrity;
using Viridian.Core.Maintenance;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;
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

    /// <summary>Espécies do projeto com as quantidades que interessam ao cronograma de manutenção.</summary>
    public static IReadOnlyList<MaintenanceItem> MaintenanceItems(
        ViridianContext ctx, IReadOnlyList<SpeciesReportRow> species, IReadOnlyList<AcquisitionRow> acquisition)
    {
        var reserves = acquisition
            .Where(a => a.SpeciesId.Length > 0)
            .GroupBy(a => a.SpeciesId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Reserve), StringComparer.OrdinalIgnoreCase);
        var items = new List<MaintenanceItem>();
        foreach (SpeciesReportRow row in species)
        {
            Species? s = string.IsNullOrWhiteSpace(row.SpeciesId) ? null : ctx.Lookup(row.SpeciesId);
            if (s is null)
            {
                continue; // famílias não cadastradas não têm dados para o cronograma
            }

            items.Add(new MaintenanceItem
            {
                Species = s,
                SpeciesCode = row.Code,
                NewCount = row.New,
                KeptCount = row.Existing + row.Transplanted,
                AreaM2 = row.MassingAreaM2,
                Reserve = reserves.GetValueOrDefault(row.SpeciesId),
            });
        }

        return items;
    }

    /// <summary>Grava no cadastro da espécie o código de insumo ou de serviço da tabela de preços.</summary>
    public static bool LinkPriceCode(ViridianContext ctx, string speciesId, string code, bool service)
    {
        Species? current = ctx.Catalog.Find(speciesId);
        if (current is null)
        {
            return false;
        }

        Species copy = ViridianJson.Deserialize<Species>(ViridianJson.Serialize(current)) ?? current;
        if (service)
        {
            copy.ServiceItemCode = code.Trim();
        }
        else
        {
            copy.PriceItemCode = code.Trim();
        }

        try
        {
            ctx.Catalog.Save(copy);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar o código de preço da espécie.", ex);
            TaskDialog.Show("Viridian", "Não foi possível gravar o código no cadastro:\n" + ex.Message);
            return false;
        }
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
        IReadOnlyList<SpeciesReportRow> species = ReportBuilder.BuildSpecies(snapshot.Plants, snapshot.Massings);
        IReadOnlyList<AcquisitionRow> acquisition = DocumentationFlows.Acquisition(ctx, snapshot);
        var extras = new ReportExtras
        {
            Lookup = ctx.Lookup,
            Paths = ctx.Paths,
            Settings = ctx.Settings,
            MaintenanceItems = DocumentationFlows.MaintenanceItems(ctx, species, acquisition),
            LinkCode = (id, code, service) => DocumentationFlows.LinkPriceCode(ctx, id, code, service),
        };
        var window = new ReportsWindow(
            ProjectReader.ProjectTitle(ctx.Doc),
            species,
            ReportBuilder.BuildMassings(snapshot.Massings),
            acquisition,
            extras,
            InitialTab);
        bool accepted = ctx.ShowDialog(window);
        if (window.SettingsChanged)
        {
            ctx.SaveSettings();
        }

        if (!accepted)
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

/// <summary>Abre os relatórios no orçamento (tabela de preços, BDI e totais).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class BudgetCommand : ReportsCommand
{
    protected override int InitialTab => 3;
}

/// <summary>Abre os relatórios no cronograma de manutenção.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaintenanceCommand : ReportsCommand
{
    protected override int InitialTab => 4;
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

/// <summary>Verifica somente as interferências do plantio com redes, postes, edificações e pisos (projeto e vínculos).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class InterferenceCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        for (int round = 0; round < 50; round++)
        {
            ProjectSnapshot snapshot = ProjectReader.Read(doc, ctx.Lookup);
            List<Core.Interference.InterferencePlant> plants = InterferenceReader.Plants(doc, snapshot.Plants, ctx.Lookup);
            Core.Interference.InterferenceSettings settings = ctx.Settings.Interference;
            InterferenceSources sources = InterferenceReader.Read(doc, settings);
            IReadOnlyList<Core.Interference.InterferenceFinding> findings =
                Core.Interference.InterferenceChecker.Check(plants, sources.Segments, sources.Points, sources.Pavements, settings);

            var issues = new List<IntegrityIssue>
            {
                new()
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.Interferencia,
                    Message = $"{plants.Count} exemplar(es) verificados contra {sources.Segments.Count} trecho(s) de redes e paredes, "
                              + $"{sources.Points.Count} poste(s) e ponto(s) elétricos e {sources.Pavements.Count} piso(s). "
                              + "Modelos lidos: " + string.Join(", ", sources.Models) + "."
                              + (sources.Unloaded.Count > 0 ? " Vínculos não carregados (fora da verificação): " + string.Join(", ", sources.Unloaded) + "." : string.Empty),
                },
            };
            issues.AddRange(IntegrityChecker.Run(new IntegrityInput { Interferences = findings, Settings = new AnalysisSettings { CheckCrownConflicts = false, CheckSpacing = false } })
                .Where(i => i.Kind == IssueKind.Interferencia));

            var window = new IntegrityWindow(issues, ids => ctx.ShowElements(ids)) { Title = "Viridian — Interferências" };
            if (!ctx.ShowDialog(window) || window.Action != IntegrityAction.Recheck)
            {
                return Result.Succeeded;
            }
        }

        return Result.Succeeded;
    }
}
