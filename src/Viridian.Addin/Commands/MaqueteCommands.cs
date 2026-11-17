using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Detailing;
using Viridian.Core.Model3D;
using Viridian.Revit;

namespace Viridian.Addin.Commands;

/// <summary>Fluxo comum da maquete: gerar na transação, abrir a vista e resumir o resultado.</summary>
internal static class MaqueteFlow
{
    public static Result Generate(ViridianContext ctx, MaqueteState state, string title)
    {
        MaqueteResult result = ctx.Grouped("Viridian: " + title, () =>
            Tx.Run(ctx.Doc, "Viridian: " + title, () =>
                MaqueteService.Build(ctx.Doc, state, ctx.Lookup, ctx.ProjectCode, ctx.Settings), suppressWarnings: true), _ => true);

        try
        {
            ctx.UiDocument.ActiveView = result.View;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            Viridian.Library.ViridianLog.Warn("Não foi possível abrir a vista da maquete.", ex);
        }

        var lines = new List<string>
        {
            $"Fase: {GrowthModel.Name(state.Stage)} · {(state.IsWholeProject ? "projeto inteiro" : "região escolhida")}",
            $"{result.Plants} exemplar(es) e {result.Beds} maciço(s) em volume.",
        };
        if (result.OnTerrain > 0)
        {
            lines.Add($"{result.OnTerrain} assentado(s) sobre o terreno ou sobre lajes.");
        }

        if (result.Skipped > 0)
        {
            lines.Add($"{result.Skipped} exemplar(es) ficaram de fora pelo limite de {ctx.Settings.Model3D.MaxPlants} (Configurações > Composição). Use uma região menor ou aumente o limite.");
        }

        if (result.Failed > 0)
        {
            lines.Add($"{result.Failed} elemento(s) não puderam ser criados; o detalhe está no registro de erros (Viridian > Sobre).");
        }

        if (!result.HiddenInPlans)
        {
            lines.Add("Atenção: o Revit não aceitou ocultar a maquete nas plantas. Desligue a categoria Entorno nas vistas em planta, se necessário.");
        }

        if (result.Plants + result.Beds == 0)
        {
            lines.Add("Não há vegetação do catálogo no recorte escolhido.");
        }

        lines.Add(string.Empty);
        lines.Add("A maquete não acompanha as mudanças sozinha: depois de alterar o plantio, use Atualizar Maquete.");
        TaskDialog.Show("Viridian — Maquete 3D", string.Join("\n", lines));
        return Result.Succeeded;
    }
}

/// <summary>Gera a maquete 3D do projeto inteiro ou de uma região.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        bool inPlan = ctx.UiDocument.ActiveView is ViewPlan { IsTemplate: false, GenLevel: not null };
        bool exists = MaqueteService.Shapes(ctx.Doc).Count > 0;
        var dialog = new TaskDialog("Viridian — Maquete 3D")
        {
            MainInstruction = "O que a maquete deve mostrar?",
            MainContent = (exists ? "A maquete atual será substituída.\n" : string.Empty)
                          + "A vegetação aparece em volume na vista \"" + MaqueteService.ViewName + "\". As plantas baixas não mudam.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Projeto inteiro", "Toda a vegetação do catálogo, em todos os níveis.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Uma região da planta",
            inPlan ? "Escolha ou desenhe o contorno na planta ativa; a vista 3D é recortada em torno dele." : "Disponível com uma vista em planta aberta.");
        TaskDialogResult choice = dialog.Show();
        if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
        {
            return Result.Cancelled;
        }

        var state = new MaqueteState();
        if (choice == TaskDialogResult.CommandLink2)
        {
            ViewPlan view = ctx.RequirePlanView();
            RegionBoundary region = ctx.PickRegion("a maquete 3D");
            state.Region = MaqueteState.FromPolygon(region.Polygon);
            state.LevelName = PlantingService.LevelOf(view)?.Name ?? string.Empty;
        }

        GrowthStage? stage = SectionCommand.AskStage("Maquete 3D", "Em que fase a vegetação deve aparecer?");
        if (stage is null)
        {
            return Result.Cancelled;
        }

        state.Stage = stage.Value;
        return MaqueteFlow.Generate(ctx, state, "maquete 3D");
    }
}

/// <summary>Refaz a maquete com o mesmo recorte e a mesma fase.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteUpdateCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        MaqueteState state = MaqueteService.ReadState(MaqueteService.FindView(ctx.Doc))
                             ?? throw new ViridianException("Ainda não há maquete neste projeto. Use Maquete 3D para criá-la.");
        return MaqueteFlow.Generate(ctx, state, "atualizar maquete 3D");
    }
}

/// <summary>Remove a maquete 3D (formas, materiais e vista), sem tocar na vegetação 2D.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MaqueteRemoveCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        int count = MaqueteService.Shapes(ctx.Doc).Count;
        View3D? view = MaqueteService.FindView(ctx.Doc);
        if (count == 0 && view is null)
        {
            TaskDialog.Show("Viridian", "Não há maquete 3D neste projeto.");
            return Result.Cancelled;
        }

        var confirm = new TaskDialog("Viridian — Remover maquete")
        {
            MainInstruction = "Remover a maquete 3D?",
            MainContent = $"{count} forma(s) da maquete e os materiais criados para ela serão apagados. A vista \"{view?.Name ?? MaqueteService.ViewName}\" também, se não estiver em uma prancha.\nA vegetação 2D, os maciços e as tabelas não são alterados.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (confirm.Show() != TaskDialogResult.Yes)
        {
            return Result.Cancelled;
        }

        // A vista ativa não pode ser apagada: troca para outra vista aberta antes.
        if (view is not null && ctx.UiDocument.ActiveView?.Id == view.Id)
        {
            View? other = ctx.UiDocument.GetOpenUIViews().Select(u => ctx.Doc.GetElement(u.ViewId)).OfType<View>().FirstOrDefault(v => v.Id != view.Id)
                          ?? new FilteredElementCollector(ctx.Doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
            if (other is not null)
            {
                try
                {
                    ctx.UiDocument.ActiveView = other;
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    Viridian.Library.ViridianLog.Warn("Não foi possível trocar de vista antes de remover a maquete.", ex);
                }
            }
        }

        (int shapes, bool viewDeleted) = ctx.Grouped("Viridian: remover maquete 3D", () =>
            Tx.Run(ctx.Doc, "Viridian: remover maquete 3D", () => MaqueteService.Remove(ctx.Doc), suppressWarnings: true), _ => true);
        TaskDialog.Show("Viridian", $"Maquete removida: {shapes} forma(s)." + (viewDeleted ? " A vista também foi apagada." : " A vista foi mantida (está em uma prancha ou aberta) e pode ser apagada no Navegador de projeto."));
        return Result.Succeeded;
    }
}
