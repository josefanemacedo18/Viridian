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

        var window = new RainGardenWindow(ctx.Settings.RainGarden, contributing, garden, ctx.Catalog.All, ctx.HasProject,
            new Viridian.Library.LooseMaterialLibrary(ctx.Paths).Materials);
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

/// <summary>
/// Estudo solar de uma região: horas de sol direto mês a mês (ou nos solstícios e no equinócio), calendário de luz,
/// mapas em cópias da planta e as plantas que não recebem a luz que pedem, com sugestões de espécies.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SolarStudyCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ctx.RequireModule(ctx.Settings.Modules.Solar, "Estudo solar");
        ViewPlan view = ctx.RequirePlanView();
        ProjectSite project = SolarService.ReadSite(ctx.Doc, ctx.Settings.Solar);
        var window = new SolarStudyWindow(project.Site, project.LooksUndefined, project.TrueNorthDeg, ctx.Settings.Solar);
        if (!ctx.ShowDialog(window) || window.Site is not SolarSite site)
        {
            return Result.Cancelled;
        }

        ctx.Settings.Solar = window.Settings;
        ctx.SaveSettings();
        SolarSettings settings = window.Settings;

        IReadOnlyList<SolarDate> dates = settings.Monthly ? Sun.MonthlyDates() : Sun.KeyDates(site.LatitudeDeg);
        string period = settings.Monthly ? "Mês a mês (dia típico de cada mês)" : "Solstícios e equinócio";
        var maps = new List<(string Label, Func<SolarPoint, double> Hours)>();
        if (settings.MapAnnual)
        {
            maps.Add(("Média do ano", p => p.Average));
        }

        if (settings.Monthly)
        {
            foreach (int m in settings.MapMonths)
            {
                int index = m - 1;
                maps.Add((Sun.MonthNames[index], p => p.Hours[index]));
            }
        }
        else
        {
            maps.Add((dates[0].Name, p => p.Hours[0])); // solstício de inverno: pior caso
        }

        RegionBoundary region = ctx.PickRegion("o estudo solar");
        List<(PlantRecord Record, Species Species)> inside = DesignFlows.Plants(ctx, view, region.Polygon);
        // Fazem sombra todas as plantas da vista, inclusive as de maciços (bosques de árvores).
        List<(PlantRecord Record, Species Species)> shading = DesignFlows.Plants(ctx, view, includeMassings: true);
        double levelZ = RevitUnits.ToMeters(view.GenLevel!.ProjectElevation);

        // Datas das sombras hora a hora: os meses marcados (ou inverno e verão) ou as três datas-chave.
        var shadeDates = new List<SolarDate>();
        if (settings.ShadeSheets || settings.PlanHours.Count > 0)
        {
            shadeDates = settings.Monthly
                ? (settings.MapMonths.Count > 0 ? settings.MapMonths : (site.South ? new List<int> { 6, 12 } : new List<int> { 12, 6 })).Select(m => dates[m - 1]).ToList()
                : dates.ToList();
        }

        string stageText = settings.Stage switch
        {
            GrowthStage.Plantio => "mudas no plantio",
            GrowthStage.TresAnos => "copas aos 3 anos",
            GrowthStage.SeisAnos => "copas aos 6 anos",
            _ => "copas adultas",
        };

        (List<ViewPlan> created, ViewDrafting calendar, SolarReport report, List<LightCalendarRow> rows, List<View> shadeViews) = ctx.Grouped("Viridian: estudo solar", () =>
        {
            View3D probe = Tx.Run(ctx.Doc, "Viridian: estudo solar (preparo)", () =>
            {
                if (window.SaveToProject)
                {
                    SolarService.SaveSite(ctx.Doc, site);
                }

                View3D v = SolarService.CreateProbeView(ctx.Doc);
                ctx.Doc.Regenerate();
                return v;
            });
            SolarReport analyzed = SolarService.Analyze(ctx.Doc, probe, region.Polygon, levelZ, inside, shading, dates, settings, settings.Stage, site, shadeDates);
            List<LightCalendarRow> calendarRows = CalendarRows(ctx, analyzed, settings);
            List<ShadeRow> shadeRows = ShadeRows(ctx, analyzed);
            (List<ViewPlan> views, ViewDrafting drafting, List<View> shades) = Tx.Run(ctx.Doc, "Viridian: estudo solar", () =>
            {
                double text = ctx.Settings.Labels.TextSizeMm;
                var heatMaps = maps.Select(m => SolarService.HeatMap(ctx.Doc, view, region.Polygon, analyzed, m.Label, m.Hours, settings, text)).ToList();
                ViewDrafting cal = SolarService.CalendarView(ctx.Doc, view, analyzed, period, calendarRows, settings, text);
                var shadeViewList = new List<View>();
                foreach ((SolarDate date, List<ShadeFrame> frames) in analyzed.Shades)
                {
                    if (settings.ShadeSheets)
                    {
                        shadeViewList.Add(SolarService.ShadeSheetView(ctx.Doc, view, region.Polygon, analyzed, date, frames, shadeRows, stageText, text));
                    }

                    foreach (ShadeFrame frame in frames.Where(f => settings.PlanHours.Contains(f.ClockHour)))
                    {
                        shadeViewList.Add(SolarService.ShadowPlan(ctx.Doc, view, region.Polygon, analyzed, frame, text));
                    }
                }

                ctx.Doc.Delete(probe.Id);
                return (heatMaps, cal, shadeViewList);
            }, suppressWarnings: true);
            return (views, drafting, analyzed, calendarRows, shades);
        }, _ => true);

        View opened = shadeViews.OfType<ViewDrafting>().FirstOrDefault() as View ?? (created.Count > 0 ? created[0] : calendar);
        ctx.UiDocument.ActiveView = opened;

        // Imagens (fora de transação): calendário, mapas, quadros e plantas com sombras.
        string imagesText = string.Empty;
        if (settings.ExportImages)
        {
            string folder = string.IsNullOrWhiteSpace(ctx.Doc.PathName)
                ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Viridian", "Estudo solar")
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ctx.Doc.PathName)!, "Viridian - Estudo solar");
            var all = new List<View> { calendar };
            all.AddRange(created);
            all.AddRange(shadeViews);
            int exported = SolarService.ExportImages(ctx.Doc, all, folder);
            imagesText = $" {exported} imagem(ns) PNG exportada(s) em {folder}.";
        }

        // Resumo, critérios e plantas cuja exigência de luz não combina com o local.
        double average = report.Grid.Count == 0 ? 0 : report.Grid.Average(g => g.Point.Average);
        int full = report.Grid.Count(g => g.Point.Average >= settings.FullSunHours);
        int shade = report.Grid.Count(g => g.Point.Average < settings.ShadeHours);
        string mapsText = created.Count == 0 ? "nenhum mapa" : string.Join(", ", created.Select(v => $"\"{v.Name}\""));
        int sheets = shadeViews.OfType<ViewDrafting>().Count(), plans = shadeViews.OfType<ViewPlan>().Count();
        string shadesText = shadeViews.Count == 0 ? string.Empty
            : $" Sombras hora a hora ({settings.FromHour}h às {settings.ToHour}h, {stageText}): {sheets} quadro(s) e {plans} planta(s) com as sombras nos horários escolhidos.";
        int deciduous = shading.Select(s => s.Species)
            .DistinctBy(s => s.Id).Count(s => s.Category == PlantCategory.Arvore && s.EffectiveLeafHabit is LeafHabit.Decidua or LeafHabit.Semidecidua);
        if (deciduous > 0)
        {
            shadesText += $" {deciduous} espécie(s) de árvore decídua ou semidecídua: sombra mais rala nos meses sem folhas (confira a Folhagem na ficha).";
        }
        string message = $"{period}. Local: {site.Describe()}. "
                         + $"Média anual de {SolarClasses.Hours(average)} h de sol direto na região ({report.Grid.Count} pontos a cada {report.Cell:0.0} m): "
                         + $"sol pleno (≥ {SolarClasses.Hours(settings.FullSunHours)} h) em {Percent(full, report.Grid.Count)}, "
                         + $"menos de {SolarClasses.Hours(settings.ShadeHours)} h em {Percent(shade, report.Grid.Count)}. "
                         + $"Calendário de luz na vista \"{calendar.Name}\"; mapas: {mapsText}.{shadesText}{imagesText} Cálculo em {report.Duration.TotalSeconds:0} s.";
        if (window.SaveToProject)
        {
            message += " As coordenadas foram gravadas na localização do projeto.";
        }
        else if (site.Source == SiteSource.Digitado)
        {
            message += " As coordenadas digitadas não foram gravadas no projeto.";
        }

        var issues = new List<IntegrityIssue>
        {
            new() { Severity = IssueSeverity.Info, Kind = IssueKind.Composicao, Message = message, ElementIds = Array.Empty<long>() },
        };

        int fine = 0, unknown = 0;
        foreach (SolarPlant p in report.Plants)
        {
            PlantLight? light = PlantLight.Evaluate(p.Species.Light, p.Point.Hours, dates, settings);
            if (light is null)
            {
                unknown++;
                continue;
            }

            if (light.Failing == 0)
            {
                fine++;
                continue;
            }

            string name = $"{ctx.ProjectCode(p.Species)} {p.Species.PopularName}";
            bool majority = light.Verdict == LightFit.NaoAtende;
            string text = $"{name} {light.Describe(settings)}";
            if (majority)
            {
                List<Species> suggestions = SolarSuggestions.For(p.Species, light.Mean, ctx.Catalog.All, settings);
                if (suggestions.Count > 0)
                {
                    text += $" Sugestões do catálogo para {SolarClasses.NeedText(SolarClasses.Classify(light.Mean, settings), settings)}, apenas como sugestão: "
                            + string.Join(", ", suggestions.Select(x => x.PopularName)) + ".";
                }
            }
            else
            {
                text = $"{name} atende na maior parte do ano. " + text[(name.Length + 1)..];
            }

            issues.Add(new IntegrityIssue
            {
                Severity = majority ? IssueSeverity.Aviso : IssueSeverity.Info,
                Kind = IssueKind.Composicao,
                Message = text,
                ElementIds = new[] { p.Record.ElementId },
            });
        }

        string tail = report.Plants.Count == 0 ? " Não há plantas do catálogo na região."
            : $" {fine} de {report.Plants.Count} planta(s) recebem a luz que pedem em todos os {(dates.Count == 12 ? "meses" : "períodos")}"
              + (unknown > 0 ? $"; {unknown} sem a luz informada na ficha." : ".");
        IntegrityIssue first = issues[0];
        issues[0] = new IntegrityIssue { Severity = first.Severity, Kind = first.Kind, Message = first.Message + tail, ElementIds = first.ElementIds };

        var result = new IntegrityWindow(issues, ids => ctx.ShowElements(ids)) { Title = "Viridian — Estudo solar" };
        ctx.ShowDialog(result);
        return Result.Succeeded;
    }

    /// <summary>Linhas da tabela do quadro de sombras: cada espécie da região com os índices dos seus exemplares.</summary>
    private static List<ShadeRow> ShadeRows(ViridianContext ctx, SolarReport report) =>
        Enumerable.Range(0, report.Plants.Count)
            .GroupBy(i => report.Plants[i].Species.Id)
            .Select(g =>
            {
                Species species = report.Plants[g.First()].Species;
                int count = g.Count();
                string need = species.Light switch { LightNeed.SolPleno => " · sol", LightNeed.MeiaSombra => " · meia-sombra", LightNeed.Sombra => " · sombra", _ => string.Empty };
                return new ShadeRow($"{ctx.ProjectCode(species)} {species.PopularName}" + (count > 1 ? $" ({count})" : string.Empty) + need, g.ToList());
            })
            .OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Linhas do calendário de luz: a região (média dos pontos) e cada espécie (média dos seus exemplares).</summary>
    private static List<LightCalendarRow> CalendarRows(ViridianContext ctx, SolarReport report, SolarSettings settings)
    {
        int n = report.Dates.Count;
        var rows = new List<LightCalendarRow>();
        if (report.Grid.Count > 0)
        {
            double[] region = Enumerable.Range(0, n).Select(d => report.Grid.Average(g => g.Point.Hours[d])).ToArray();
            rows.Add(new LightCalendarRow("Região analisada (média)", region, null, 0));
        }

        foreach (IGrouping<string, SolarPlant> group in report.Plants.GroupBy(p => p.Species.Id).OrderBy(g => ctx.ProjectCode(g.First().Species), StringComparer.OrdinalIgnoreCase))
        {
            Species species = group.First().Species;
            double[] hours = Enumerable.Range(0, n).Select(d => group.Average(p => p.Point.Hours[d])).ToArray();
            int count = group.Count();
            string label = $"{ctx.ProjectCode(species)} {species.PopularName}" + (count > 1 ? $" ({count})" : string.Empty)
                           + species.Light switch { LightNeed.SolPleno => " · sol", LightNeed.MeiaSombra => " · meia-sombra", LightNeed.Sombra => " · sombra", _ => string.Empty };
            rows.Add(new LightCalendarRow(label, hours, PlantLight.Evaluate(species.Light, hours, report.Dates, settings), count));
        }

        return rows;
    }

    private static string Percent(int part, int total) => total == 0 ? "0%" : $"{100.0 * part / total:0}%";
}
