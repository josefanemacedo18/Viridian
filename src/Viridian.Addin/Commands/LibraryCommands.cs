using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Viridian.Core.Model;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Biblioteca botânica: pesquisa, cadastro e atalhos para inserir, distribuir ou criar maciços.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class LibraryCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        var browser = new SpeciesBrowserWindow(ctx.Catalog, ctx.Paths, ctx.Settings, allowPlacement: ctx.HasProject);
        if (!ctx.ShowDialog(browser) || browser.SelectedSpecies is null)
        {
            return Result.Cancelled;
        }

        Species species = browser.SelectedSpecies;
        if (browser.Action != BrowserAction.None)
        {
            ctx.RememberSpecies(species);
        }

        return browser.Action switch
        {
            BrowserAction.Insert => PlantingFlows.Insert(ctx, species),
            BrowserAction.Distribute => PlantingFlows.Distribute(ctx, species),
            BrowserAction.Massing => MassingCommands.Create(ctx, species),
            _ => Result.Cancelled,
        };
    }
}

/// <summary>Assistente "Importar símbolo personalizado": verifica a família e a associa a uma espécie.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class ImportFamilyCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        var open = new OpenFileDialog
        {
            Title = "Importar símbolo personalizado",
            Filter = "Famílias do Revit (*.rfa)|*.rfa",
        };
        if (open.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        FamilyInspection inspection = FamilyInspector.Inspect(ctx.UiApp.Application, open.FileName);
        var info = new FamilyCheckInfo
        {
            FilePath = inspection.FilePath,
            CategoryName = inspection.CategoryName,
            IsPlanting = inspection.IsPlanting,
            IsDetailItem = inspection.IsDetailItem,
            TypeNames = inspection.TypeNames,
            Checks = inspection.Checks,
            Warnings = inspection.Warnings,
            Error = inspection.Error,
        };

        var window = new ImportFamilyWindow(info, null, owner =>
        {
            var browser = new SpeciesBrowserWindow(ctx.Catalog, ctx.Paths, ctx.Settings, "Associar a família à espécie") { Owner = owner };
            return browser.ShowDialog() == true ? browser.SelectedSpecies : null;
        });
        if (!ctx.ShowDialog(window) || window.SelectedSpecies is null)
        {
            return Result.Cancelled;
        }

        string folder = info.IsPlanting ? ctx.Paths.CustomFamiliesFolder : ctx.Paths.CustomSymbolsFolder;
        string target = Path.Combine(folder, Path.GetFileName(info.FilePath));
        try
        {
            Directory.CreateDirectory(folder);
            if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(info.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(info.FilePath, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ViridianException("Não foi possível copiar a família para a biblioteca:\n" + ex.Message, ex);
        }

        Species species = window.SelectedSpecies.Clone();
        if (info.IsPlanting)
        {
            species.CustomPlantingFamilyFile = Path.GetFileName(target);
            species.CustomPlantingTypeName = window.SelectedTypeName;
        }
        else
        {
            species.CustomDetailFamilyFile = Path.GetFileName(target);
            species.CustomDetailTypeName = window.SelectedTypeName;
        }

        try
        {
            ctx.Catalog.Save(species);
        }
        catch (InvalidOperationException ex)
        {
            throw new ViridianException(ex.Message, ex);
        }

        if (info.IsPlanting && ctx.HasProject)
        {
            List<ElementId> plants = PlantsOfSpecies(ctx.Doc, species.Id);
            if (plants.Count > 0 && TaskDialog.Show(
                    "Viridian",
                    $"{plants.Count} exemplar(es) de {species.DisplayName} neste projeto usam o símbolo anterior. Atualizar agora para a família personalizada?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes)
            {
                ctx.Grouped("Viridian: aplicar família personalizada", () =>
                {
                    IReadOnlyList<FamilySymbol> symbols = ctx.EnsureVariants(species, 0);
                    return ctx.Planting.Substitute(ctx.Doc, plants, symbols);
                }, _ => true);
            }
        }

        ShowInfo("Família registrada na biblioteca", $"{Path.GetFileName(target)} → {species.DisplayName}");
        return Result.Succeeded;
    }

    private static List<ElementId> PlantsOfSpecies(Document document, string speciesId)
    {
        var symbolIds = new HashSet<ElementId>(SpeciesFamilyService.LinkedSymbols(document)
            .Where(x => string.Equals(x.Link.SpeciesId, speciesId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Symbol.Id));
        using var collector = new FilteredElementCollector(document);
        return collector.OfCategory(BuiltInCategory.OST_Planting).OfClass(typeof(FamilyInstance))
            .Where(e => symbolIds.Contains(e.GetTypeId()) && string.IsNullOrWhiteSpace(VirParams.GetString(e, VirParams.MassingId)))
            .Select(e => e.Id)
            .ToList();
    }
}

/// <summary>Atualiza tipos, maciços, etiquetas e perfis do projeto a partir do catálogo e das configurações.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SyncCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        (SyncSummary Sync, int Massings, LabelUpdateResult Labels) result = ctx.Grouped("Viridian: atualizar dados", () =>
        {
            SyncSummary sync = RunSync(ctx);
            int massings = MassingService.RefreshRecords(ctx.Doc, ctx.Lookup, ctx.Project);
            LabelUpdateResult labels = ctx.Labels.UpdateAll(ctx.Doc);
            ctx.AfterSymbolsLoaded();
            return (sync, massings, labels);
        }, _ => true);

        string text = $"Tipos atualizados: {result.Sync.TypesUpdated}\n"
                      + $"Símbolos regerados: {result.Sync.FamiliesRegenerated}\n"
                      + $"Maciços atualizados: {result.Massings}\n"
                      + $"Etiquetas atualizadas: {result.Labels.Updated}";
        if (result.Sync.MissingSpecies > 0)
        {
            text += $"\n\n{result.Sync.MissingSpecies} tipo(s) apontam para espécies que não estão mais no catálogo.";
        }

        if (result.Labels.Orphans > 0)
        {
            text += $"\n{result.Labels.Orphans} etiqueta(s) perderam o elemento identificado (use Verificar Projeto para limpá-las).";
        }

        if (result.Sync.Problems.Count > 0)
        {
            text += "\n\nProblemas:\n" + string.Join("\n", result.Sync.Problems.Take(10));
        }

        ShowInfo("Dados atualizados", text);
        return Result.Succeeded;
    }

    /// <summary>Sincroniza os tipos com o catálogo, pedindo um modelo de família se for preciso regerar símbolos.</summary>
    internal static SyncSummary RunSync(ViridianContext ctx)
    {
        try
        {
            return ctx.Families.SyncAll(ctx.Doc, ctx.Lookup, ctx.Project);
        }
        catch (FamilyTemplateMissingException)
        {
            // Força a escolha do modelo (EnsureVariants pergunta ao usuário) e tenta de novo.
            Species? any = SpeciesFamilyService.LinkedSymbols(ctx.Doc)
                .Select(x => ctx.Lookup(x.Link.SpeciesId))
                .FirstOrDefault(s => s is not null && !s.HasCustomPlantingFamily);
            if (any is null)
            {
                throw;
            }

            ctx.EnsureVariants(any, 0);
            return ctx.Families.SyncAll(ctx.Doc, ctx.Lookup, ctx.Project);
        }
    }
}

/// <summary>Cadastra uma vegetação própria do escritório: nome, porte, símbolo, cores, foto e dados de plantio.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class NewSpeciesCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        var editor = new SpeciesEditorWindow(ctx.Catalog.NewDraft(PlantCategory.Arbusto), ctx.Paths, ctx.Settings, "Nova vegetação");
        if (!ctx.ShowDialog(editor))
        {
            return Result.Cancelled;
        }

        Species species = editor.Species;
        try
        {
            ctx.Catalog.Save(species);
        }
        catch (InvalidOperationException ex)
        {
            throw new ViridianException(ex.Message);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            throw new ViridianException("Não foi possível gravar o catálogo em disco:\n" + ex.Message);
        }

        if (!ctx.HasProject)
        {
            TaskDialog.Show("Viridian", $"\"{species.DisplayName}\" foi cadastrada na biblioteca. Ela aparece em Buscar Espécie, Inserir, Distribuir e Criar Maciço.");
            return Result.Succeeded;
        }

        var ask = new TaskDialog("Viridian — Nova vegetação")
        {
            MainInstruction = $"\"{species.DisplayName}\" foi cadastrada.",
            MainContent = "Deseja usá-la agora?",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Inserir exemplares");
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Distribuir numa região");
        ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Criar maciço");
        TaskDialogResult choice = ask.Show();
        ctx.RememberSpecies(species);
        return choice switch
        {
            TaskDialogResult.CommandLink1 => PlantingFlows.Insert(ctx, species),
            TaskDialogResult.CommandLink2 => PlantingFlows.Distribute(ctx, species),
            TaskDialogResult.CommandLink3 => MassingCommands.Create(ctx, species),
            _ => Result.Succeeded,
        };
    }
}
