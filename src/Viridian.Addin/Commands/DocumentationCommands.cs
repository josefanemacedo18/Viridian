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

    /// <summary>Espécies que aparecem na vista (exemplares e maciços), com os pontos onde aparecem.</summary>
    public static IReadOnlyList<PhotoBoardSpecies> BoardSpecies(ViridianContext ctx, View view)
    {
        var targets = new Dictionary<string, List<Core.Geometry.Point2>>(StringComparer.OrdinalIgnoreCase);
        var targetIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string speciesId, Core.Geometry.Point2 point, string uniqueId = "")
        {
            if (string.IsNullOrWhiteSpace(speciesId))
            {
                return;
            }

            if (!targets.TryGetValue(speciesId, out List<Core.Geometry.Point2>? list))
            {
                list = new List<Core.Geometry.Point2>();
                targets[speciesId] = list;
            }

            list.Add(point);
            if (!targetIds.TryGetValue(speciesId, out List<string>? ids))
            {
                ids = new List<string>();
                targetIds[speciesId] = ids;
            }

            ids.Add(uniqueId);
        }

        using (var collector = new FilteredElementCollector(ctx.Doc, view.Id))
        {
            foreach (FamilyInstance instance in collector.OfCategory(BuiltInCategory.OST_Planting).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                if (ElementData.TryRead(instance.Symbol, ElementData.KindSpecies, out SpeciesLink link))
                {
                    Add(link.SpeciesId, PlantingService.LocationOf(instance), instance.UniqueId);
                }
            }
        }

        foreach (Core.Reports.MassingInfo massing in MassingService.ReadMassings(ctx.Doc))
        {
            if (massing.ViewId == view.Id.Value && massing.Boundary is { IsEmpty: false } boundary)
            {
                Add(massing.Record.SpeciesId, boundary.InteriorPoint());
            }
        }

        var result = new List<PhotoBoardSpecies>();
        foreach ((string id, List<Core.Geometry.Point2> points) in targets)
        {
            if (ctx.Lookup(id) is Species species)
            {
                result.Add(new PhotoBoardSpecies(species, ctx.ProjectCode(species), ctx.Paths.ResolvePhoto(species.PhotoFile), points)
                {
                    TargetIds = targetIds.TryGetValue(id, out List<string>? ids) ? ids : new List<string>(),
                });
            }
        }

        return result.OrderBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToList();
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
            Purchases = ElementData.ReadProject(ctx.Doc).Purchases,
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

        if (window.PurchasesChanged)
        {
            // Relê os dados do projeto para não sobrescrever outras informações gravadas entretanto.
            PurchaseLedger ledger = window.Purchases;
            Tx.Run(ctx.Doc, "Viridian: compras registradas", () =>
            {
                ProjectData data = ElementData.ReadProject(ctx.Doc);
                data.Purchases = ledger;
                ElementData.WriteProject(ctx.Doc, data);
            });
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

/// <summary>Quadro de fotos ao redor da planta: foto, código e nome de cada espécie, com linha até um exemplar.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PhotoBoardCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        IReadOnlyList<PhotoBoardSpecies> species = DocumentationFlows.BoardSpecies(ctx, view);
        if (species.Count == 0)
        {
            throw new ViridianException("Não há vegetação do catálogo visível nesta vista.");
        }

        int cards = ctx.Grouped("Viridian: quadro de fotos", () =>
            Tx.Run(ctx.Doc, "Viridian: quadro de fotos", () => ctx.Photos.CreateBoard(ctx.Doc, view, species), suppressWarnings: true), _ => true);
        int withoutPhoto = species.Count(s => s.PhotoFile.Length == 0);
        TaskDialog.Show("Viridian", $"Quadro de fotos criado com {cards} espécie(s)."
            + (withoutPhoto > 0 ? $"\n{withoutPhoto} espécie(s) sem fotografia receberam moldura vazia (cadastre a foto na Biblioteca)." : string.Empty)
            + "\n\nPara refazer depois de mudar o plantio, use o comando de novo: o quadro anterior desta vista é substituído.");
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
