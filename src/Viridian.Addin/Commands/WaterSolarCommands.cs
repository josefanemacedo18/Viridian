using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Integrity;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Core.Solar;
using Viridian.Core.Water;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Jardim de chuva: dimensionamento, plantas por zona e detalhe em corte com memorial de cálculo.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class RainGardenCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequireModule(ctx.Settings.Modules.RainGarden, "Jardim de chuva");

        // Telhados, pisos e regiões selecionados antes do comando somam a área de contribuição.
        double contributing = 0;
        foreach (Element e in ctx.SelectedElements())
        {
            if (e is RoofBase or Floor or FilledRegion)
            {
                contributing += RevitUnits.SquareFeetToSquareMeters(e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0);
            }
        }

        double garden = 0, width = 2.0;
        if (ctx.UiDocument.ActiveView is ViewPlan { IsTemplate: false, GenLevel: not null })
        {
            var ask = new TaskDialog("Viridian — Jardim de chuva")
            {
                MainInstruction = "Escolher a área do jardim na planta?",
                MainContent = "Com a área escolhida, o Viridian confere se ela retém o volume da chuva. Sem área, calcula a área necessária."
                              + (contributing > 0 ? $"\n\nÁrea de contribuição dos elementos selecionados: {contributing:N1} m²." : string.Empty),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true,
            };
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Escolher ou desenhar o canteiro");
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Só calcular");
            TaskDialogResult choice = ask.Show();
            if (choice == TaskDialogResult.CommandLink1)
            {
                RegionBoundary region = ctx.PickRegion("o jardim de chuva");
                garden = region.Polygon.Area;
                width = Math.Clamp(region.Polygon.OrientedSize().Width, 1.0, 6.0);
            }
            else if (choice != TaskDialogResult.CommandLink2)
            {
                return Result.Cancelled;
            }
        }

        var window = new RainGardenWindow(ctx.Settings.RainGarden, contributing, garden, ctx.Catalog.All, ctx.HasProject);
        bool accepted = ctx.ShowDialog(window);
        if (window.SaveAsDefault)
        {
            ctx.Settings.RainGarden = window.Settings.Clone();
            ctx.SaveSettings();
        }

        if (!accepted || !window.CreateDetail || window.Result is not RainGardenResult result)
        {
            return Result.Succeeded;
        }

        if (garden <= 0 && window.GardenAreaM2 <= 0 && double.IsFinite(result.RequiredAreaM2))
        {
            width = Math.Clamp(Math.Sqrt(result.RequiredAreaM2 / 2), 1.0, 4.0); // jardim de proporção 2:1
        }

        Drawing drawing = RainGardenCalculator.Detail(window.Settings, result, width);
        Bounds2 extent = drawing.Extent();
        double y = extent.MinY - 0.25;
        drawing.Text(new Point2(extent.MinX, y), "MEMORIAL DE CÁLCULO", -1, 1.15);
        y -= 0.16;
        foreach (string line in RainGardenCalculator.Report(window.ContributingAreaM2, result, window.Settings))
        {
            foreach (string part in TextLayout.Wrap(line, 120))
            {
                drawing.Text(new Point2(extent.MinX, y), part, -1, 0.9);
                y -= 0.11;
            }
        }

        int scale = DesignService.FitScale(drawing, 0.45);
        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        ViewDrafting view = ctx.Grouped("Viridian: jardim de chuva", () =>
            Tx.Run(ctx.Doc, "Viridian: jardim de chuva", () =>
                design.DrawingView(ctx.Doc, DesignFlows.NextViewName(ctx.Doc, "Viridian - Jardim de chuva"), drawing, scale), suppressWarnings: true), _ => true);
        ctx.UiDocument.ActiveView = view;
        return Result.Succeeded;
    }
}

/// <summary>Estudo solar de uma região: mapa de horas de sol e plantas fora da luz que pedem.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SolarStudyCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequireModule(ctx.Settings.Modules.Solar, "Estudo solar");
        ViewPlan view = ctx.RequirePlanView();
        SolarSettings settings = ctx.Settings.Solar;
        double latitude = SolarService.Latitude(ctx.Doc, settings, out string place);
        IReadOnlyList<SolarDate> all = Sun.KeyDates(latitude);

        var dialog = new TaskDialog("Viridian — Estudo solar")
        {
            MainInstruction = "Que época analisar?",
            MainContent = $"Latitude {latitude:0.00}° ({place}). As horas de sol direto consideram as copas adultas"
                          + (settings.IncludeBuildings ? ", as edificações e o terreno (também de vínculos)." : ".")
                          + "\nA latitude e o norte vêm de Gerenciar > Localização; ajustes em Configurações > Composição.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Média do ano", "Média do solstício de inverno, do equinócio e do solstício de verão.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, all[0].Name, "Pior caso: sol mais baixo e sombras mais longas.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, all[1].Name);
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, all[2].Name);
        (IReadOnlyList<SolarDate> dates, string label) = dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => ((IReadOnlyList<SolarDate>)all, "Média do ano (inverno, equinócio e verão)"),
            TaskDialogResult.CommandLink2 => (new[] { all[0] }, all[0].Name),
            TaskDialogResult.CommandLink3 => (new[] { all[1] }, all[1].Name),
            TaskDialogResult.CommandLink4 => (new[] { all[2] }, all[2].Name),
            _ => (Array.Empty<SolarDate>(), string.Empty),
        };
        if (dates.Count == 0)
        {
            return Result.Cancelled;
        }

        RegionBoundary region = ctx.PickRegion("o estudo solar");
        List<(PlantRecord Record, Species Species)> inside = DesignFlows.Plants(ctx, view, region.Polygon);
        List<(PlantRecord Record, Species Species)> shading = DesignFlows.Plants(ctx, view);
        double levelZ = RevitUnits.ToMeters(view.GenLevel!.ProjectElevation);

        (ViewPlan map, SolarReport report) = ctx.Grouped("Viridian: estudo solar", () =>
        {
            View3D probe = Tx.Run(ctx.Doc, "Viridian: estudo solar (preparo)", () =>
            {
                View3D v = SolarService.CreateProbeView(ctx.Doc);
                ctx.Doc.Regenerate();
                return v;
            });
            SolarReport analyzed = SolarService.Analyze(ctx.Doc, probe, region.Polygon, levelZ, inside, shading, dates, settings, GrowthStage.Maturidade);
            ViewPlan created = Tx.Run(ctx.Doc, "Viridian: estudo solar", () =>
            {
                ViewPlan heat = SolarService.HeatMap(ctx.Doc, view, region.Polygon, analyzed, label, ctx.Settings.Labels.TextSizeMm);
                ctx.Doc.Delete(probe.Id);
                return heat;
            }, suppressWarnings: true);
            return (created, analyzed);
        }, _ => true);

        ctx.UiDocument.ActiveView = map;

        // Resumo e plantas cuja exigência de luz não combina com o local.
        double average = report.Grid.Count == 0 ? 0 : report.Grid.Average(g => g.Point.Average);
        int full = report.Grid.Count(g => SolarClasses.Classify(g.Point.Average, settings) == LightNeed.SolPleno);
        int shade = report.Grid.Count(g => SolarClasses.Classify(g.Point.Average, settings) == LightNeed.Sombra);
        var issues = new List<IntegrityIssue>
        {
            new()
            {
                Severity = IssueSeverity.Info,
                Kind = IssueKind.Composicao,
                Message = $"{label}: média de {average:0.0} h de sol direto na região ({report.Grid.Count} pontos a cada {report.Cell:0.0} m). "
                          + $"Sol pleno (≥ {settings.FullSunHours:0} h) em {Percent(full, report.Grid.Count)}; sombra (< {settings.ShadeHours:0} h) em {Percent(shade, report.Grid.Count)}. "
                          + $"Mapa na vista \"{map.Name}\". Cálculo em {report.Duration.TotalSeconds:0} s.",
                ElementIds = Array.Empty<long>(),
            },
        };
        foreach (SolarPlant p in report.Plants)
        {
            string? problem = SolarClasses.Mismatch(p.Species, p.Point.Average, p.Point.Hours[0], settings);
            if (problem is not null)
            {
                issues.Add(new IntegrityIssue
                {
                    Severity = IssueSeverity.Aviso,
                    Kind = IssueKind.Composicao,
                    Message = $"{ctx.ProjectCode(p.Species)} {p.Species.PopularName} {problem}",
                    ElementIds = new[] { p.Record.ElementId },
                });
            }
        }

        if (issues.Count == 1)
        {
            IntegrityIssue first = issues[0];
            issues[0] = new IntegrityIssue
            {
                Severity = first.Severity,
                Kind = first.Kind,
                Message = first.Message + (report.Plants.Count == 0 ? " Não há plantas do catálogo na região." : " Todas as plantas da região recebem a luz que pedem."),
                ElementIds = first.ElementIds,
            };
        }

        var window = new IntegrityWindow(issues, ids => ctx.ShowElements(ids)) { Title = "Viridian — Estudo solar" };
        ctx.ShowDialog(window);
        return Result.Succeeded;
    }

    private static string Percent(int part, int total) => total == 0 ? "0%" : $"{100.0 * part / total:0}%";
}
