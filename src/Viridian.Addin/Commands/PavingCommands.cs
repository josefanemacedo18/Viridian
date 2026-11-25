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
        var stones = new LooseMaterialLibrary(ctx.Paths);
        List<ElementId> floors = ctx.HasProject
            ? ctx.SelectedElements().OfType<Floor>().Select(f => f.Id).ToList()
            : new List<ElementId>();
        var window = new PavingWindow(library, new PavingWindowOptions
        {
            HasProject = ctx.HasProject,
            SelectedFloors = floors.Count,
            ProjectTitle = ctx.HasProject ? ProjectReader.ProjectTitle(ctx.Doc) : string.Empty,
            // Pisos de pedras e coberturas também entram no quantitativo.
            Quantities = ctx.HasProject ? types => PavingService.Quantities(ctx.Doc, types.Concat(stones.PavingTypes()).ToList()) : null,
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

/// <summary>Pedras e coberturas: biblioteca, cobertura de canteiros, bordas de caminho, pisos soltos e camadas drenantes.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class StoneCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequireModule(ctx.Settings.Modules.Paving, "Pedras e coberturas (módulo Gerador de pisos)");
        var library = new LooseMaterialLibrary(ctx.Paths);
        List<ElementId> floors = ctx.HasProject
            ? ctx.SelectedElements().OfType<Floor>().Select(f => f.Id).ToList()
            : new List<ElementId>();
        var window = new StoneWindow(library, ctx.HasProject, floors.Count);
        if (!ctx.ShowDialog(window) || window.Paving is not PavingType type || window.Material is not LooseMaterial material || window.Action == StoneAction.None)
        {
            return Result.Cancelled;
        }

        if (!ctx.HasProject)
        {
            throw new ViridianException("Abra um projeto para usar o material no Revit.");
        }

        string useText = LooseLabels.Of(window.Use).ToLowerInvariant();

        // Textura do material (Realista e renderização), gerada a partir dos dados da pedra e gravada na biblioteca.
        (string File, double SizeM, Core.Colors.Rgb Average)? texture = null;
        if (window.Action is StoneAction.Apply or StoneAction.FillRegion or StoneAction.Border or StoneAction.CreateType)
        {
            try
            {
                texture = AppearanceTextures.StoneTexture(ctx.Paths, material);
            }
            catch (ViridianException ex)
            {
                ViridianLog.Warn("Textura da pedra não gerada: " + ex.Message);
            }
        }

        FloorType StoneType()
        {
            FloorType created = PavingService.EnsureFloorType(ctx.Doc, type);
            if (texture is { } t)
            {
                string? note = AppearanceTextures.ApplyStone(ctx.Doc, PavingService.EnsureMaterial(ctx.Doc, type), t.File, t.SizeM, t.Average);
                if (note is not null)
                {
                    ViridianLog.Warn($"Textura de \"{type.Name}\": {note}");
                }
            }

            return created;
        }

        switch (window.Action)
        {
            case StoneAction.Apply:
            {
                (FloorType floorType, int changed) = ctx.Grouped("Viridian: aplicar pedra", () =>
                    Tx.Run(ctx.Doc, "Viridian: aplicar pedra", () =>
                    {
                        FloorType created = StoneType();
                        return (created, PavingService.Apply(ctx.Doc, created, floors));
                    }, suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"\"{floorType.Name}\" aplicado a {changed} piso(s), como {useText}.");
                break;
            }

            case StoneAction.FillRegion:
            {
                ViewPlan view = ctx.RequirePlanView();
                Level level = view.GenLevel ?? throw new ViridianException("A vista ativa não tem nível associado.");
                RegionBoundary region = ctx.PickRegion($"a {useText} de {material.Name.ToLowerInvariant()}");
                Floor floor = ctx.Grouped("Viridian: cobrir região", () =>
                    Tx.Run(ctx.Doc, "Viridian: cobrir região", () =>
                    {
                        FloorType created = StoneType();
                        return StoneService.CreateFloor(ctx.Doc, created, region.Polygon, level);
                    }, suppressWarnings: true), _ => true);
                (double volume, double? weight, int? bags) = LooseMaterialUse.Purchase(material, window.Use, region.Polygon.Area);
                ctx.UiDocument.Selection.SetElementIds(new[] { floor.Id });
                TaskDialog.Show("Viridian", $"{material.Name} ({useText}) cobrindo {region.Polygon.Area:0.00} m² ({region.Source}).\n\n"
                                            + Purchase(material, volume, weight, bags)
                                            + "\n\nO piso foi criado no nível da vista; ajuste o deslocamento se o canteiro estiver em outra cota. Para pisos já desenhados, selecione-os e use Aplicar.");
                break;
            }

            case StoneAction.Border:
            {
                ViewPlan view = ctx.RequirePlanView();
                Level level = view.GenLevel ?? throw new ViridianException("A vista ativa não tem nível associado.");
                (IReadOnlyList<Core.Distribution.PlantingPath> paths, Core.Geometry.Polygon2? around, string source) = PickBorderAxis(ctx);
                if (paths.Count == 0)
                {
                    return Result.Cancelled;
                }

                (List<Floor> created, int failed) = ctx.Grouped("Viridian: borda de caminho", () =>
                    Tx.Run(ctx.Doc, "Viridian: borda de caminho", () =>
                    {
                        FloorType floorType = StoneType();
                        return StoneService.CreateBorders(ctx.Doc, floorType, paths, window.BorderWidthM, window.BorderSide, level, around);
                    }, suppressWarnings: true), r => r.Created.Count > 0);
                if (created.Count == 0)
                {
                    throw new ViridianException("Nenhuma faixa de borda pôde ser criada. Verifique se as linhas formam um caminho contínuo e se a largura não é grande demais para as curvas.");
                }

                double area = created.Sum(f => RevitUnits.SquareFeetToSquareMeters(f.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0));
                (double volume, double? weight, int? bags) = LooseMaterialUse.Purchase(material, window.Use, area);
                ctx.UiDocument.Selection.SetElementIds(created.Select(f => f.Id).ToList());
                TaskDialog.Show("Viridian", $"{created.Count} faixa(s) de {material.Name} com {window.BorderWidthM * 100:0} cm ao longo de {source}"
                                            + (failed > 0 ? $" ({failed} trecho(s) recusado(s): veja o registro)" : string.Empty) + $".\nÁrea: {area:0.00} m².\n\n"
                                            + Purchase(material, volume, weight, bags) + "\n\nSe a faixa saiu do lado errado, desfaça (Ctrl+Z) e escolha o outro lado.");
                break;
            }

            case StoneAction.CreateType:
            {
                FloorType floorType = ctx.Grouped("Viridian: tipo de piso", () =>
                    Tx.Run(ctx.Doc, "Viridian: tipo de piso", StoneType, suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"Tipo de piso \"{floorType.Name}\" criado (ou atualizado), com espessura total de {PavingDetail.Cm(type.TotalThicknessMm)}"
                                            + (texture is null ? "." : ", com a textura da pedra no estilo Realista.") + "\n\nUse Arquitetura > Piso e escolha o tipo.");
                break;
            }

            case StoneAction.CreateRegion:
            {
                FilledRegionType region = ctx.Grouped("Viridian: hachura de pedra", () =>
                    Tx.Run(ctx.Doc, "Viridian: hachura de pedra", () => PavingService.EnsureRegionType(ctx.Doc, type), suppressWarnings: true), _ => true);
                TaskDialog.Show("Viridian", $"Tipo de região preenchida \"{region.Name}\" criado.\n\nEm uma planta, use Anotar > Região > Região preenchida e escolha esse tipo.");
                break;
            }

            case StoneAction.Detail:
            {
                ViewDrafting view = ctx.Grouped("Viridian: detalhe de pedra", () =>
                    Tx.Run(ctx.Doc, "Viridian: detalhe de pedra", () => PavingService.DetailView(ctx.Doc, type, ctx.Settings.Labels.TextSizeMm), suppressWarnings: true), _ => true);
                ctx.UiDocument.ActiveView = view;
                break;
            }
        }

        return Result.Succeeded;
    }

    private static string Purchase(LooseMaterial m, double volume, double? weight, int? bags)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        string text = $"Compra estimada: {volume.ToString("0.00", pt)} m³ (com {m.WastePercent:0}% de perda e compactação)";
        if (weight is double w)
        {
            text += $", {w.ToString("0.00", pt)} t";
        }

        if (bags is int b)
        {
            text += m.BagKg > 0 ? $", {b} sacos de {m.BagKg.ToString("0.#", pt)} kg" : $", {b} sacos de {m.BagLiters.ToString("0.#", pt)} L";
        }

        return text + ". Valores de referência: confirme com o fornecedor.";
    }

    /// <summary>
    /// Eixo da borda: um piso ou região selecionado (a faixa contorna o caminho) ou linhas, arcos e paredes ao longo da
    /// borda. Sem seleção, pergunta qual dos dois.
    /// </summary>
    private static (IReadOnlyList<Core.Distribution.PlantingPath> Paths, Core.Geometry.Polygon2? Around, string Source) PickBorderAxis(ViridianContext ctx)
    {
        List<Element> selected = ctx.SelectedElements().ToList();
        Element? outline = selected.FirstOrDefault(e => e is Floor or FilledRegion);
        if (outline is not null)
        {
            RegionBoundary boundary = BoundaryReader.Read(outline);
            return (StoneService.RingsAsPaths(boundary.Polygon), boundary.Polygon, boundary.Source);
        }

        AxisSelection fromSelection = AxisPicker.FromElements(selected);
        if (fromSelection.Paths.Count > 0)
        {
            return (fromSelection.Paths, null, fromSelection.Description);
        }

        var dialog = new TaskDialog("Viridian — Borda de caminho")
        {
            MainInstruction = "Ao longo de quê?",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Contorno de um caminho ou canteiro", "Escolha um piso ou uma região preenchida: a faixa acompanha todo o contorno.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Linhas ao longo da borda", "Escolha linhas, arcos ou paredes já desenhados.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Desenhar a borda", "Clique os vértices da borda; ESC conclui.");
        switch (dialog.Show())
        {
            case TaskDialogResult.CommandLink1:
            {
                Element element;
                try
                {
                    Reference picked = ctx.UiDocument.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element, new OutlineFilter(), "Escolha o piso ou a região do caminho");
                    element = ctx.Doc.GetElement(picked);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return (Array.Empty<Core.Distribution.PlantingPath>(), null, string.Empty);
                }

                RegionBoundary boundary = BoundaryReader.Read(element);
                return (StoneService.RingsAsPaths(boundary.Polygon), boundary.Polygon, boundary.Source);
            }

            case TaskDialogResult.CommandLink2:
            {
                AxisSelection axis = AxisPicker.Pick(ctx.UiDocument, AxisSource.Existentes);
                return (axis.Paths, null, axis.Description);
            }

            case TaskDialogResult.CommandLink3:
            {
                AxisSelection axis = AxisPicker.Pick(ctx.UiDocument, AxisSource.Polilinha);
                return (axis.Paths, null, axis.Description);
            }

            default:
                return (Array.Empty<Core.Distribution.PlantingPath>(), null, string.Empty);
        }
    }

    private sealed class OutlineFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor or FilledRegion;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
