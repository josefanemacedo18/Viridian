using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Coding;
using Viridian.Core.Model;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Codificação automática de exemplares e renumeração dos códigos de espécie no projeto.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CodeCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        IReadOnlyList<FamilyInstance> selected = ctx.SelectedPlants();
        List<FamilyInstance> inView = PlantsInView(doc, ctx.UiDocument.ActiveView);
        List<FamilyInstance> inProject = PlantingService.PlantingInstances(doc, AllPlantIds(doc));

        var window = new CodingWindow(selected.Count, inView.Count, inProject.Count, ctx.Settings);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        CodingOptions options = window.Options;
        switch (options.Action)
        {
            case CodingAction.Instances:
            {
                if (options.Template != ctx.Settings.InstanceCode.Template || options.Digits != ctx.Settings.InstanceCode.Digits)
                {
                    ctx.Settings.InstanceCode = new InstanceCodeFormat { Template = options.Template, Digits = options.Digits };
                    ctx.Settings.InstanceCode.Normalize();
                    ctx.SaveSettings();
                }

                IEnumerable<FamilyInstance> targets = options.Scope switch
                {
                    CodingScope.Selection => selected,
                    CodingScope.View => inView,
                    _ => inProject,
                };
                var service = new PlantingService(ctx.Settings);
                int count = ctx.Grouped("Viridian: codificar exemplares", () =>
                {
                    int coded = service.AssignCodes(doc, targets.Select(t => t.Id), options.Overwrite, i => CategoryOf(ctx, i));
                    ctx.Labels.UpdateAll(doc);
                    return coded;
                }, n => n > 0);
                ShowInfo(count == 0 ? "Nenhum exemplar precisou de código" : $"{count} exemplar(es) codificado(s)");
                return Result.Succeeded;
            }

            case CodingAction.RenumberSpecies:
            case CodingAction.RestoreSpecies:
            {
                bool restore = options.Action == CodingAction.RestoreSpecies;
                ProjectData data = ElementData.ReadProject(doc);
                data.SpeciesCodes.Clear();
                if (!restore)
                {
                    List<Species> used = UsedSpecies(ctx);
                    foreach (IGrouping<PlantCategory, Species> group in used.GroupBy(s => s.Category).OrderBy(g => g.Key))
                    {
                        IEnumerable<Species> ordered = options.Order switch
                        {
                            SpeciesOrder.PopularName => group.OrderBy(s => s.PopularName, StringComparer.CurrentCultureIgnoreCase),
                            SpeciesOrder.CatalogCode => group.OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase),
                            _ => group.OrderBy(s => s.ScientificName, StringComparer.OrdinalIgnoreCase),
                        };
                        int number = 1;
                        foreach (Species species in ordered)
                        {
                            data.SpeciesCodes[species.Id] = CodeFormat.Sequential(Labels.Prefix(species.Category), number++);
                        }
                    }
                }

                ctx.Grouped(restore ? "Viridian: restaurar códigos de espécie" : "Viridian: renumerar espécies", () =>
                {
                    Tx.Run(doc, "Viridian: códigos do projeto", () => ElementData.WriteProject(doc, data));
                    ctx.InvalidateProject();
                    SyncCommand.RunSync(ctx);
                    MassingService.RefreshRecords(doc, ctx.Lookup, ctx.Project);
                    ctx.Labels.UpdateAll(doc);
                    return true;
                }, _ => true);

                ShowInfo(restore ? "Códigos do catálogo restaurados" : $"{data.SpeciesCodes.Count} espécie(s) renumerada(s) neste projeto");
                return Result.Succeeded;
            }

            default:
                return Result.Cancelled;
        }
    }

    internal static PlantCategory CategoryOf(ViridianContext ctx, FamilyInstance instance)
    {
        if (ElementData.TryRead(instance.Symbol, ElementData.KindSpecies, out SpeciesLink link) && ctx.Lookup(link.SpeciesId) is { } species)
        {
            return species.Category;
        }

        string code = VirParams.GetString(instance.Symbol, VirParams.SpeciesCode);
        int dash = code.IndexOf('-', StringComparison.Ordinal);
        return Labels.TryCategoryFromPrefix(dash > 0 ? code[..dash] : code, out PlantCategory category) ? category : PlantCategory.Arvore;
    }

    internal static List<FamilyInstance> PlantsInView(Document doc, View view)
    {
        if (view is null || view.IsTemplate || view is ViewSchedule or ViewSheet)
        {
            return new List<FamilyInstance>();
        }

        try
        {
            using var collector = new FilteredElementCollector(doc, view.Id);
            return PlantingService.PlantingInstances(doc, collector.OfCategory(BuiltInCategory.OST_Planting).OfClass(typeof(FamilyInstance)).ToElementIds());
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            return new List<FamilyInstance>();
        }
    }

    internal static IReadOnlyList<ElementId> AllPlantIds(Document doc)
    {
        using var collector = new FilteredElementCollector(doc);
        return collector.OfCategory(BuiltInCategory.OST_Planting).OfClass(typeof(FamilyInstance)).ToElementIds().ToList();
    }

    private static List<Species> UsedSpecies(ViridianContext ctx)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((FamilySymbol _, SpeciesLink link) in SpeciesFamilyService.LinkedSymbols(ctx.Doc))
        {
            ids.Add(link.SpeciesId);
        }

        foreach (Core.Reports.MassingInfo massing in MassingService.ReadMassings(ctx.Doc))
        {
            ids.Add(massing.Record.SpeciesId);
        }

        return ids.Select(ctx.Lookup).OfType<Species>().ToList();
    }
}

/// <summary>Etiquetas de exemplares e maciços na vista ativa.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class LabelCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        View view = ctx.UiDocument.ActiveView;
        if (!ScaleProfileService.IsApplicable(view) || view.IsTemplate)
        {
            throw new ViridianException("Abra uma vista em planta para etiquetar a vegetação.");
        }

        // Somente exemplares visíveis na vista ativa podem receber etiqueta nela.
        List<FamilyInstance> visible = CodeCommand.PlantsInView(doc, view);
        var visibleIds = new HashSet<ElementId>(visible.Select(p => p.Id));
        IReadOnlyList<FamilyInstance> selectedPlants = ctx.SelectedPlants().Where(p => visibleIds.Contains(p.Id)).ToList();
        IReadOnlyList<FilledRegion> selectedMassings = ctx.SelectedMassings().Where(r => r.OwnerViewId == view.Id).ToList();
        List<FamilyInstance> viewPlants = visible
            .Where(p => string.IsNullOrWhiteSpace(VirParams.GetString(p, VirParams.MassingId)))
            .ToList();
        List<FilledRegion> viewMassings = ElementData.ElementsOfKind(doc, ElementData.KindMassing)
            .OfType<FilledRegion>()
            .Where(r => r.OwnerViewId == view.Id)
            .ToList();

        List<(string, long)> tagTypes = LabelService.PlantingTagTypes(doc)
            .Select(t => (t.FamilyName + " : " + t.Name, t.Id.Value))
            .ToList();
        var window = new LabelWindow(selectedPlants.Count, selectedMassings.Count, viewPlants.Count, viewMassings.Count, tagTypes, ctx.Settings);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        LabelOptions options = window.Options;
        IReadOnlyList<FamilyInstance> plants = options.Plants ? (options.SelectionOnly ? selectedPlants : viewPlants) : Array.Empty<FamilyInstance>();
        IReadOnlyList<FilledRegion> massings = options.Massings ? (options.SelectionOnly ? selectedMassings : viewMassings) : Array.Empty<FilledRegion>();
        var request = new LabelRequest
        {
            Model = options.Model,
            Leader = options.Leader,
            GroupBySpecies = options.GroupBySpecies,
            NativeTagTypeId = options.NativeTagTypeId > 0 ? new ElementId(options.NativeTagTypeId) : ElementId.InvalidElementId,
        };

        int created = ctx.Grouped("Viridian: etiquetar", () =>
            ctx.Labels.LabelPlants(doc, view, plants, request) + ctx.Labels.LabelMassings(doc, view, massings, request), n => n > 0);
        ShowInfo($"{created} etiqueta(s) criada(s)");
        return Result.Succeeded;
    }
}

/// <summary>Atualiza o texto das etiquetas Viridian a partir dos dados atuais do modelo.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class UpdateLabelsCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        LabelUpdateResult result = ctx.Grouped("Viridian: atualizar etiquetas", () => ctx.Labels.UpdateAll(ctx.Doc), r => r.Updated > 0);
        string orphans = result.Orphans > 0 ? $"\n{result.Orphans} etiqueta(s) perderam o elemento identificado (use Verificar Projeto para limpá-las)." : string.Empty;
        ShowInfo("Etiquetas atualizadas", $"Atualizadas: {result.Updated}\nSem alteração: {result.Unchanged}{orphans}");
        return Result.Succeeded;
    }
}
