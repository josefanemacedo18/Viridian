using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

internal static class MassingCommands
{
    /// <summary>Prepara a execução de um maciço, carregando os símbolos necessários (fora de transação).</summary>
    public static MassingExecution BuildExecution(ViridianContext ctx, Species species, MassingRecord record, string? hatchKey = null)
    {
        if (record.SymbolKind == MassingSymbolKind.DetailItem && !species.HasCustomDetailFamily)
        {
            record.SymbolKind = MassingSymbolKind.Planting;
        }

        string key = hatchKey ?? record.HatchPatternKey;
        HatchPattern? hatch = HatchLibrary.Find(key, ctx.Settings.CustomHatches)
                              ?? HatchLibrary.Find(HatchLibrary.KeyFor(species), ctx.Settings.CustomHatches)
                              ?? HatchLibrary.Find(HatchLibrary.DefaultKeyFor(species.Category));

        var execution = new MassingExecution
        {
            Record = record,
            Species = species,
            ProjectSpeciesCode = ctx.ProjectCode(species),
            Hatch = hatch,
        };

        bool needsSymbols = record.Mode != MassingMode.Hachura && !SupplyUnits.IsAreaBased(species.SupplyUnit);
        if (needsSymbols)
        {
            if (record.SymbolKind == MassingSymbolKind.DetailItem)
            {
                execution.DetailSymbol = ctx.Families.EnsureDetailSymbol(ctx.Doc, species);
            }
            else
            {
                execution.PlantingSymbols = ctx.EnsureVariants(species, -1);
            }
        }

        return execution;
    }

    /// <summary>Criação de maciço a partir de uma espécie (usado também pela biblioteca).</summary>
    public static Result Create(ViridianContext ctx, Species species)
    {
        View active = ctx.UiDocument.ActiveView;
        if (!ScaleProfileService.IsApplicable(active) || active.IsTemplate)
        {
            throw new ViridianException("Abra uma vista em planta para criar maciços.");
        }

        RegionBoundary boundary = ctx.PickRegion("o maciço de " + species.PopularName);
        FilledRegion? existing = boundary.ExistingRegion;
        View view = active;
        if (existing is not null)
        {
            if (MassingService.RecordOf(existing) is not null)
            {
                throw new ViridianException("A região selecionada já é um maciço. Use Editar Maciço para alterá-la.");
            }

            view = ctx.Doc.GetElement(existing.OwnerViewId) as View ?? active;
        }

        IReadOnlyList<MassingInfo> all = MassingService.ReadMassings(ctx.Doc);
        var used = new HashSet<string>(all.Select(m => m.Record.Id.Trim()), StringComparer.OrdinalIgnoreCase);
        var draft = new MassingRecord
        {
            Id = MassingCalculator.NextId(used, ctx.Settings.MassingPrefix),
            Seed = Random.Shared.NextInt64(1, long.MaxValue),
            HatchPatternKey = HatchLibrary.ProjectKey(species, ReportBuilder.DistinctMassings(all).Select(m => (m.Record.SpeciesId, m.Record.HatchPatternKey))),
        };

        var window = new MassingWindow(species, boundary.Polygon, draft, true, used, ctx.Settings, species.HasCustomDetailFamily, view is ViewPlan);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        MassingOutcome outcome = ctx.Grouped("Viridian: maciço " + window.Record.Id, () =>
        {
            MassingExecution execution = BuildExecution(ctx, species, window.Record, window.HatchKey);
            return ctx.Massings.Create(ctx.Doc, view, boundary.Polygon, existing, execution);
        }, _ => true);

        ctx.Select(new[] { outcome.Region.Id });
        CommandBase.ShowInfo(
            $"Maciço {outcome.Record.Id} criado",
            $"{species.DisplayName}\n{Viridian.Core.Identification.LabelText.AreaAndQuantity(outcome.Record)}");
        return Result.Succeeded;
    }

    /// <summary>Região de maciço selecionada ou escolhida pelo usuário.</summary>
    public static FilledRegion PickMassing(ViridianContext ctx, string prompt)
    {
        IReadOnlyList<FilledRegion> selected = ctx.SelectedMassings();
        if (selected.Count == 1)
        {
            return selected[0];
        }

        Reference reference = ctx.UiDocument.Selection.PickObject(ObjectType.Element, new FilledRegionFilter(onlyMassings: true), prompt);
        return ctx.Doc.GetElement(reference) as FilledRegion ?? throw new CommandCancelledException();
    }
}

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MassingCreateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        View active = ctx.UiDocument.ActiveView;
        if (!ScaleProfileService.IsApplicable(active) || active.IsTemplate)
        {
            throw new ViridianException("Abra uma vista em planta para criar maciços.");
        }

        return MassingCommands.Create(ctx, ctx.PickSpecies("Maciço vegetal — escolha a espécie"));
    }
}

/// <summary>Altera os parâmetros de um maciço e o recalcula a partir do limite atual.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MassingEditCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        FilledRegion region = MassingCommands.PickMassing(ctx, "Selecione o maciço a editar");
        MassingRecord record = MassingService.RecordOf(region) ?? throw new ViridianException("A região selecionada não é um maciço do Viridian.");
        Species species = ctx.Lookup(record.SpeciesId)
            ?? throw new ViridianException($"A espécie do maciço {record.Id} ({record.SpeciesCode} {record.PopularName}) não está no catálogo. Use Substituir para escolher outra.");

        Polygon2 polygon = BoundaryReader.FromLoops(region.GetBoundaries());
        if (polygon.IsEmpty)
        {
            throw new ViridianException("A região do maciço não possui um limite fechado válido.");
        }

        View? view = ctx.Doc.GetElement(region.OwnerViewId) as View;
        var used = new HashSet<string>(
            MassingService.ReadMassings(ctx.Doc).Select(m => m.Record.Id.Trim()).Where(id => !string.Equals(id, record.Id.Trim(), StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        string previousId = record.Id;

        var window = new MassingWindow(species, polygon, record.Clone(), false, used, ctx.Settings, species.HasCustomDetailFamily, view is ViewPlan);
        if (!ctx.ShowDialog(window))
        {
            return Result.Cancelled;
        }

        MassingOutcome outcome = ctx.Grouped("Viridian: editar maciço " + window.Record.Id, () =>
        {
            if (!string.Equals(previousId, window.Record.Id, StringComparison.OrdinalIgnoreCase))
            {
                Tx.Run(ctx.Doc, "Viridian: renomear maciço", () => MassingService.RemoveSymbols(ctx.Doc, previousId));
            }

            MassingExecution execution = MassingCommands.BuildExecution(ctx, species, window.Record, window.HatchKey);
            MassingOutcome result = ctx.Massings.Recalculate(ctx.Doc, region, execution);
            ctx.Labels.UpdateAll(ctx.Doc);
            return result;
        }, _ => true);

        ctx.Select(new[] { outcome.Region.Id });
        return Result.Succeeded;
    }
}

/// <summary>Recalcula área, quantidade e símbolos dos maciços depois de alterações nos limites.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MassingRecalculateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        List<FilledRegion> regions = ctx.SelectedMassings().ToList();
        bool all = regions.Count == 0;
        if (all)
        {
            regions = ElementData.ElementsOfKind(ctx.Doc, ElementData.KindMassing).OfType<FilledRegion>().ToList();
        }

        if (regions.Count == 0)
        {
            throw new ViridianException("O projeto não possui maciços do Viridian.");
        }

        var problems = new List<string>();
        int done = ctx.Grouped("Viridian: recalcular maciços", () =>
        {
            int count = 0;
            foreach (FilledRegion region in regions)
            {
                MassingRecord? record = MassingService.RecordOf(region);
                if (record is null)
                {
                    continue;
                }

                Species? species = ctx.Lookup(record.SpeciesId);
                if (species is null)
                {
                    problems.Add($"{record.Id}: espécie {record.SpeciesCode} não encontrada no catálogo.");
                    continue;
                }

                try
                {
                    MassingExecution execution = MassingCommands.BuildExecution(ctx, species, record);
                    ctx.Massings.Recalculate(ctx.Doc, region, execution);
                    count++;
                }
                catch (ViridianException ex)
                {
                    problems.Add($"{record.Id}: {ex.Message}");
                }
            }

            ctx.Labels.UpdateAll(ctx.Doc);
            return count;
        }, count => count > 0);

        string scope = all ? "do projeto" : "selecionados";
        ShowInfo($"{done} maciço(s) {scope} recalculado(s)", problems.Count == 0 ? string.Empty : "Não recalculados:\n" + string.Join("\n", problems.Take(15)));
        return Result.Succeeded;
    }
}

/// <summary>
/// Ajusta os maciços ao contorno editado sem refazer o plantio: os exemplares que continuam dentro ficam onde estão,
/// com códigos e dados; só a parte acrescentada recebe exemplares novos. Os que ficaram fora são indicados (padrão)
/// ou removidos.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MassingAdjustCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        List<FilledRegion> regions = ctx.SelectedMassings().ToList();
        bool all = regions.Count == 0;
        if (all)
        {
            regions = ElementData.ElementsOfKind(ctx.Doc, ElementData.KindMassing).OfType<FilledRegion>().ToList();
        }

        if (regions.Count == 0)
        {
            throw new ViridianException("O projeto não possui maciços do Viridian.");
        }

        // Pergunta só quando há exemplares fora do novo contorno.
        int outsideTotal = regions.Sum(r => MassingService.RecordOf(r) is MassingRecord rec ? MassingService.CountOutside(ctx.Doc, r, rec) : 0);
        OutsidePlants outside = OutsidePlants.Avisar;
        if (outsideTotal > 0)
        {
            var ask = new TaskDialog("Viridian — Ajustar contorno")
            {
                MainInstruction = $"{outsideTotal} exemplar(es) ficaram fora do novo contorno",
                MainContent = "Os exemplares que continuam dentro do maciço não serão movidos. O que fazer com os que ficaram fora?",
            };
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Manter e selecionar para eu decidir (recomendado)",
                "Eles continuam contados no maciço e ficam selecionados ao final: mova, apague ou deixe como estão.");
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Remover os que ficaram fora",
                "Apaga apenas os exemplares fora do contorno. Desfazer (Ctrl+Z) recupera.");
            ask.CommonButtons = TaskDialogCommonButtons.Cancel;
            TaskDialogResult answer = ask.Show();
            if (answer is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2))
            {
                return Result.Cancelled;
            }

            outside = answer == TaskDialogResult.CommandLink2 ? OutsidePlants.Remover : OutsidePlants.Avisar;
        }

        var problems = new List<string>();
        var lines = new List<string>();
        var toSelect = new List<ElementId>();
        var added = new List<ElementId>();
        int unchanged = 0, legacy = 0;
        int done = ctx.Grouped("Viridian: ajustar contorno dos maciços", () =>
        {
            int count = 0;
            foreach (FilledRegion region in regions)
            {
                MassingRecord? record = MassingService.RecordOf(region);
                if (record is null)
                {
                    continue;
                }

                Species? species = ctx.Lookup(record.SpeciesId);
                if (species is null)
                {
                    problems.Add($"{record.Id}: espécie {record.SpeciesCode} não encontrada no catálogo.");
                    continue;
                }

                try
                {
                    MassingExecution execution = MassingCommands.BuildExecution(ctx, species, record);
                    MassingAdjustOutcome result = ctx.Massings.Adjust(ctx.Doc, region, execution, outside);
                    if (result.Unchanged)
                    {
                        unchanged++;
                        continue;
                    }

                    count++;
                    legacy += result.WithoutPrevious ? 1 : 0;
                    toSelect.AddRange(result.OutsideKept);
                    added.AddRange(result.AddedIds);
                    lines.Add($"{record.Id} ({species.PopularName}): {result.Record.AreaM2.ToString("0.00", Viridian.UI.Num.PtBr)} m², "
                              + $"{result.Kept} mantido(s), {result.Added} novo(s)"
                              + (result.Removed > 0 ? $", {result.Removed} removido(s)" : string.Empty)
                              + (result.OutsideKept.Count > 0 ? $", {result.OutsideKept.Count} fora do contorno" : string.Empty));
                }
                catch (ViridianException ex)
                {
                    problems.Add($"{record.Id}: {ex.Message}");
                }
            }

            if (count > 0)
            {
                ctx.Labels.UpdateAll(ctx.Doc);
            }

            return count;
        }, count => count > 0);

        // Seleciona o que precisa de atenção: os que ficaram fora; senão, os novos.
        List<ElementId> selection = toSelect.Count > 0 ? toSelect : added;
        if (selection.Count > 0)
        {
            ctx.UiDocument.Selection.SetElementIds(selection);
        }

        if (done == 0 && problems.Count == 0)
        {
            ShowInfo(all ? "Nenhum maciço teve o contorno alterado" : "O contorno dos maciços selecionados não mudou",
                "Edite o limite da região (Editar limite) e rode o comando de novo. Para refazer toda a distribuição, use Recalcular.");
            return Result.Succeeded;
        }

        var text = new List<string>(lines.Take(15));
        if (toSelect.Count > 0)
        {
            text.Add($"\n{toSelect.Count} exemplar(es) fora do contorno estão selecionados.");
        }
        else if (added.Count > 0)
        {
            text.Add($"\nOs {added.Count} exemplar(es) novos estão selecionados.");
        }

        if (legacy > 0)
        {
            text.Add($"\n{legacy} maciço(s) criados em versão anterior: a área nova foi preenchida pelos vãos longe dos exemplares existentes. Daqui em diante o contorno fica gravado e só a parte acrescentada recebe plantas.");
        }

        if (problems.Count > 0)
        {
            text.Add("\nNão ajustados:\n" + string.Join("\n", problems.Take(10)));
        }

        ShowInfo($"{done} maciço(s) ajustado(s) ao contorno" + (unchanged > 0 ? $" · {unchanged} sem mudança" : string.Empty), string.Join("\n", text));
        return Result.Succeeded;
    }
}
