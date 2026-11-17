using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Fluxos de plantio compartilhados pela biblioteca e pelos botões da faixa de opções.</summary>
internal static class PlantingFlows
{
    /// <summary>Inserção individual: cada clique na vista posiciona um exemplar; ESC conclui.</summary>
    public static Result Insert(ViridianContext ctx, Species species)
    {
        ViewPlan view = ctx.RequirePlanView();
        Level level = view.GenLevel;
        var options = new InsertOptionsWindow(species, PlantingService.DefaultStage(species), 0, null, ctx.Settings);
        if (!ctx.ShowDialog(options))
        {
            return Result.Cancelled;
        }

        PlantOptions chosen = options.Options;
        var created = new List<ElementId>();
        ctx.Grouped("Viridian: inserir " + species.PopularName, () =>
        {
            IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(species, chosen.VariantIndex);
            string code = ctx.ProjectCode(species);
            var placement = new PlacementOptions { Status = chosen.Status, Stage = chosen.Stage, Notes = chosen.Notes };
            long seed = Environment.TickCount64;
            while (true)
            {
                XYZ point;
                try
                {
                    point = ctx.UiDocument.Selection.PickPoint(
                        ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest | ObjectSnapTypes.Centers,
                        $"Clique para posicionar {species.DisplayName} — ESC para concluir");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                placement.Seed = seed + created.Count;
                created.AddRange(ctx.Planting.Place(ctx.Doc, level, symbols, species, code, new[] { RevitUnits.ToPoint2(point) }, null, placement));
            }

            return created.Count;
        }, count => count > 0);

        ctx.Select(created);
        return created.Count > 0 ? Result.Succeeded : Result.Cancelled;
    }

    /// <summary>Distribuição automática dentro de uma região.</summary>
    public static Result Distribute(ViridianContext ctx, Species species)
    {
        ViewPlan view = ctx.RequirePlanView();
        RegionBoundary region = ctx.PickRegion("distribuir " + species.PopularName);
        var window = new DistributeWindow(species, region.Polygon, null, ExistingPositions(ctx, view.GenLevel), ctx.Settings, PlantingService.DefaultStage(species), region.Source);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        return Place(ctx, view.GenLevel, species, window.Outcome, "Viridian: distribuir " + species.PopularName);
    }

    /// <summary>
    /// Plantio linear: o eixo pode ser uma linha reta (dois cliques), uma polilinha, um círculo ou linhas,
    /// arcos, paredes e redes existentes. Elementos de eixo já selecionados são usados diretamente.
    /// </summary>
    public static Result Linear(ViridianContext ctx, Species species)
    {
        ViewPlan view = ctx.RequirePlanView();
        AxisSelection axis;
        List<Element> preselected = ctx.SelectedElements().Where(AxisPicker.IsAxisElement).ToList();
        if (preselected.Count > 0)
        {
            axis = AxisPicker.FromElements(preselected);
        }
        else
        {
            AxisSource? source = AskAxisSource();
            if (source is null)
            {
                return Result.Cancelled;
            }

            axis = AxisPicker.Pick(ctx.UiDocument, source.Value);
        }

        if (axis.Paths.Count == 0)
        {
            return Result.Cancelled;
        }

        var window = new DistributeWindow(species, null, axis.Paths, ExistingPositions(ctx, view.GenLevel), ctx.Settings, PlantingService.DefaultStage(species), axis.Description);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        return Place(ctx, view.GenLevel, species, window.Outcome, "Viridian: plantio linear " + species.PopularName);
    }

    private static AxisSource? AskAxisSource()
    {
        var dialog = new TaskDialog("Viridian — Plantio linear")
        {
            MainInstruction = "Como definir o eixo do plantio?",
            MainContent = "As plantas ficam sobre o eixo (ou em fileiras paralelas a ele). Dica: selecione linhas ou paredes antes de iniciar o comando para usá-las diretamente.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Linha reta", "Clique no início e no fim. Conclui sozinho no segundo clique.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Polilinha", "Clique em cada vértice; ESC conclui. Clicar de novo no primeiro ponto fecha o eixo.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Círculo", "Clique no centro e depois em um ponto do contorno.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Linhas ou paredes existentes", "Linhas de detalhe ou de modelo, arcos, splines, paredes, tubos, eletrodutos... Clique em Concluir na barra de opções.");
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => AxisSource.LinhaReta,
            TaskDialogResult.CommandLink2 => AxisSource.Polilinha,
            TaskDialogResult.CommandLink3 => AxisSource.Circulo,
            TaskDialogResult.CommandLink4 => AxisSource.Existentes,
            _ => null,
        };
    }

    private static Result Place(ViridianContext ctx, Level level, Species species, DistributeOutcome outcome, string name)
    {
        IReadOnlyList<ElementId> created = ctx.Grouped(name, () =>
        {
            IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(species, outcome.Plants.VariantIndex);
            IReadOnlyList<double>? rotations = outcome.RandomRotation ? PlantDistributor.RandomRotations(outcome.Points.Count, outcome.Seed) : null;
            var placement = new PlacementOptions
            {
                Status = outcome.Plants.Status,
                Stage = outcome.Plants.Stage,
                Seed = outcome.Seed,
            };
            return ctx.Planting.Place(ctx.Doc, level, symbols, species, ctx.ProjectCode(species), outcome.Points, rotations, placement);
        }, ids => ids.Count > 0);

        ctx.Select(created);
        return Result.Succeeded;
    }

    /// <summary>Posições dos exemplares existentes no nível, para evitar sobreposição na distribuição.</summary>
    private static IReadOnlyList<Point2> ExistingPositions(ViridianContext ctx, Level level) =>
        PlantingService.ReadPlants(ctx.Doc, ctx.Lookup)
            .Where(p => p.Status != PlantStatus.Removido && string.Equals(p.LevelName, level.Name, StringComparison.Ordinal))
            .Select(p => p.Location)
            .ToList();
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class InsertCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequirePlanView();
        return PlantingFlows.Insert(ctx, ctx.PickSpecies("Inserir exemplares — escolha a espécie"));
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class DistributeCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequirePlanView();
        return PlantingFlows.Distribute(ctx, ctx.PickSpecies("Distribuir vegetação — escolha a espécie"));
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class LinearCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequirePlanView();
        return PlantingFlows.Linear(ctx, ctx.PickSpecies("Plantio linear — escolha a espécie"));
    }
}

/// <summary>Troca a espécie de exemplares e maciços preservando posições, códigos e dados.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SubstituteCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        List<FamilyInstance> plants = ctx.SelectedPlants().ToList();
        List<FilledRegion> massings = ctx.SelectedMassings().ToList();
        if (plants.Count == 0 && massings.Count == 0)
        {
            IList<Reference> picked = ctx.UiDocument.Selection.PickObjects(
                ObjectType.Element, new PlantOrMassingFilter(), "Selecione os exemplares ou maciços a substituir e clique em Concluir");
            List<Element> elements = picked.Select(r => ctx.Doc.GetElement(r)).Where(e => e is not null).ToList();
            plants = PlantingService.PlantingInstances(ctx.Doc, elements.Select(e => e.Id));
            massings = elements.OfType<FilledRegion>().Where(r => ElementData.KindOf(r) == ElementData.KindMassing).ToList();
        }

        // Exemplares de maciços são substituídos junto com o próprio maciço.
        var massingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FamilyInstance plant in plants)
        {
            string id = VirParams.GetString(plant, VirParams.MassingId).Trim();
            if (id.Length > 0)
            {
                massingIds.Add(id);
            }
        }

        if (massingIds.Count > 0)
        {
            foreach (MassingInfo info in MassingService.ReadMassings(ctx.Doc).Where(m => massingIds.Contains(m.Record.Id.Trim())))
            {
                if (ctx.Doc.GetElement(new ElementId(info.ElementId)) is FilledRegion region && massings.All(r => r.Id != region.Id))
                {
                    massings.Add(region);
                }
            }
        }

        var existingMassingIds = new HashSet<string>(MassingService.ReadMassings(ctx.Doc).Select(m => m.Record.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        plants = plants.Where(p =>
        {
            string id = VirParams.GetString(p, VirParams.MassingId).Trim();
            return id.Length == 0 || !existingMassingIds.Contains(id);
        }).ToList();

        if (plants.Count == 0 && massings.Count == 0)
        {
            throw new ViridianException("Nenhum exemplar de vegetação ou maciço foi selecionado.");
        }

        Species species = ctx.PickSpecies($"Substituir {plants.Count} exemplar(es) e {massings.Count} maciço(s) por");
        (int Plants, int Massings) done = ctx.Grouped("Viridian: substituir espécie", () =>
        {
            int changedPlants = 0;
            if (plants.Count > 0)
            {
                IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(species, -1);
                changedPlants = ctx.Planting.Substitute(ctx.Doc, plants.Select(p => p.Id), symbols);
            }

            int changedMassings = 0;
            foreach (FilledRegion region in massings)
            {
                MassingRecord? record = MassingService.RecordOf(region);
                if (record is null)
                {
                    continue;
                }

                MassingExecution execution = MassingCommands.BuildExecution(ctx, species, record);
                ctx.Massings.Recalculate(ctx.Doc, region, execution);
                changedMassings++;
            }

            ctx.Labels.UpdateAll(ctx.Doc);
            return (changedPlants, changedMassings);
        }, _ => true);

        ShowInfo("Substituição concluída", $"{done.Plants} exemplar(es) e {done.Massings} maciço(s) passaram a ser {species.DisplayName}. Códigos e posições foram preservados.");
        return Result.Succeeded;
    }

    private sealed class PlantOrMassingFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) =>
            new PlantingInstanceFilter().AllowElement(elem) || (elem is FilledRegion && ElementData.KindOf(elem) == ElementData.KindMassing);

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}

/// <summary>Edita situação, estágio, observações e variante gráfica de exemplares.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class EditPlantsCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        IReadOnlyList<FamilyInstance> plants = ctx.SelectedOrPickedPlants("Selecione os exemplares a editar e clique em Concluir");
        if (plants.Count == 0)
        {
            return Result.Cancelled;
        }

        List<string> speciesIds = plants
            .Select(p => ElementData.TryRead(p.Symbol, ElementData.KindSpecies, out SpeciesLink link) ? link.SpeciesId : string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Species? single = speciesIds.Count == 1 ? ctx.Lookup(speciesIds[0]) : null;
        Species display = single ?? new Species { PopularName = "várias espécies", SymbolVariants = 1, CrownDiameterM = 1 };

        var current = new PlantOptions
        {
            Stage = Common(plants, VirParams.Stage),
            Notes = Common(plants, VirParams.Notes),
        };
        var window = new InsertOptionsWindow(display, string.Empty, plants.Count, current, ctx.Settings);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        PlantOptions chosen = window.Options;
        ctx.Grouped("Viridian: editar exemplares", () =>
        {
            ctx.Planting.UpdateData(
                ctx.Doc,
                plants.Select(p => p.Id),
                chosen.ChangeStatus ? chosen.Status : null,
                chosen.ChangeStage ? chosen.Stage : null,
                chosen.ChangeNotes ? chosen.Notes : null);
            if (single is not null && chosen.VariantIndex >= 0 && !single.HasCustomPlantingFamily)
            {
                IReadOnlyList<FamilySymbol> variants = ctx.EnsureVariants(single, -1);
                ctx.Planting.ChangeVariant(ctx.Doc, plants.Select(p => p.Id), variants, chosen.VariantIndex);
            }

            return true;
        }, _ => true);

        return Result.Succeeded;
    }

    private static string Common(IReadOnlyList<FamilyInstance> plants, ParamDef def)
    {
        List<string> values = plants.Select(p => VirParams.GetString(p, def)).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : string.Empty;
    }
}
