using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Paving;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Gerador de pisos: biblioteca, edição e aplicação no Revit.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PavingCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequireModule(ctx.Settings.Modules.Paving, "Gerador de pisos");
        var library = new PavingLibrary(ctx.Paths);
        List<ElementId> floors = ctx.HasProject
            ? ctx.SelectedElements().OfType<Floor>().Select(f => f.Id).ToList()
            : new List<ElementId>();
        var window = new PavingWindow(library, new PavingWindowOptions
        {
            HasProject = ctx.HasProject,
            SelectedFloors = floors.Count,
            ProjectTitle = ctx.HasProject ? ProjectReader.ProjectTitle(ctx.Doc) : string.Empty,
            Quantities = ctx.HasProject ? types => PavingService.Quantities(ctx.Doc, types) : null,
        });
        if (!ctx.ShowDialog(window) || window.Result is not PavingType type || window.Action == PavingAction.None)
        {
            return Result.Cancelled;
        }

        if (!ctx.HasProject)
        {
            throw new ViridianException("Abra um projeto para usar o piso no Revit.");
        }

        switch (window.Action)
        {
            case PavingAction.Apply:
            {
                (FloorType floorType, int changed) = ctx.Grouped("Viridian: aplicar piso", () =>
                    Tx.Run(ctx.Doc, "Viridian: aplicar piso", () =>
                    {
                        FloorType created = PavingService.EnsureFloorType(ctx.Doc, type);
                        return (created, PavingService.Apply(ctx.Doc, created, floors));
                    }, suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"Tipo \"{floorType.Name}\" aplicado a {changed} piso(s).\n\nA paginação aparece na superfície dos pisos (o padrão segue as medidas reais). Para alinhar a paginação, use Alinhar (AL) sobre a superfície.");
                break;
            }

            case PavingAction.CreateType:
            {
                FloorType floorType = ctx.Grouped("Viridian: tipo de piso", () =>
                    Tx.Run(ctx.Doc, "Viridian: tipo de piso", () => PavingService.EnsureFloorType(ctx.Doc, type), suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"Tipo de piso \"{floorType.Name}\" criado (ou atualizado) com {type.Layers.Count + 1} camada(s), espessura total de {PavingDetail.Cm(type.TotalThicknessMm)}.\n\nUse Arquitetura > Piso e escolha o tipo, ou selecione pisos existentes e abra o gerador para aplicar.");
                break;
            }

            case PavingAction.CreateRegion:
            {
                FilledRegionType region = ctx.Grouped("Viridian: hachura de piso", () =>
                    Tx.Run(ctx.Doc, "Viridian: hachura de piso", () => PavingService.EnsureRegionType(ctx.Doc, type), suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"Tipo de região preenchida \"{region.Name}\" criado.\n\nEm uma planta, use Anotar > Região > Região preenchida e escolha esse tipo para desenhar o piso com a paginação em escala real.");
                break;
            }

            case PavingAction.Detail:
            {
                ViewDrafting view = ctx.Grouped("Viridian: detalhe de piso", () =>
                    Tx.Run(ctx.Doc, "Viridian: detalhe de piso", () => PavingService.DetailView(ctx.Doc, type, ctx.Settings.Labels.TextSizeMm), suppressWarnings: true), _ => true);
                ctx.UiDocument.ActiveView = view;
                break;
            }
        }

        return Result.Succeeded;
    }
}
