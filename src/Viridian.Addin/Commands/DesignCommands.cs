using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Viridian.Core.Composition;
using Viridian.Core.Detailing;
using Viridian.Core.Export;
using Viridian.Core.Geometry;
using Viridian.Core.Integrity;
using Viridian.Core.Maintenance;
using Viridian.Core.Memorial;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;
using Viridian.Revit;
using Viridian.UI.Windows;

namespace Viridian.Addin.Commands;

/// <summary>Leitura do projeto usada pelas ferramentas de composição e detalhamento.</summary>
internal static class DesignFlows
{
    /// <summary>Exemplares cadastrados (não removidos e fora de maciços) do nível da vista, opcionalmente dentro de um contorno.</summary>
    public static List<(PlantRecord Record, Species Species)> Plants(ViridianContext ctx, View view, Polygon2? inside = null, bool includeMassings = false)
    {
        string level = PlantingService.LevelOf(view)?.Name ?? string.Empty;
        var result = new List<(PlantRecord, Species)>();
        foreach (PlantRecord plant in PlantingService.ReadPlants(ctx.Doc, ctx.Lookup))
        {
            if (!plant.IsRegistered || plant.Status == PlantStatus.Removido || (plant.BelongsToMassing && !includeMassings)
                || (level.Length > 0 && !string.Equals(plant.LevelName, level, StringComparison.Ordinal))
                || (inside is not null && !inside.Contains(plant.Location))
                || ctx.Lookup(plant.SpeciesId) is not Species species)
            {
                continue;
            }

            result.Add((plant, species));
        }

        return result;
    }

    /// <summary>Maciços da vista (ou dentro do contorno informado).</summary>
    public static List<(MassingInfo Info, Species Species)> Massings(ViridianContext ctx, View view, Polygon2? inside = null)
    {
        var result = new List<(MassingInfo, Species)>();
        foreach (MassingInfo m in ReportBuilder.DistinctMassings(MassingService.ReadMassings(ctx.Doc)))
        {
            if (m.Boundary is not { IsEmpty: false } boundary || ctx.Lookup(m.Record.SpeciesId) is not Species species)
            {
                continue;
            }

            bool inView = m.ViewId == view.Id.Value;
            bool inRegion = inside is not null && inside.Contains(boundary.InteriorPoint());
            if (inside is null ? inView : inRegion)
            {
                result.Add((m, species));
            }
        }

        return result;
    }

    /// <summary>Nome livre para uma nova vista: "prefixo 1", "prefixo 2"...</summary>
    public static string NextViewName(Document document, string prefix)
    {
        using var collector = new FilteredElementCollector(document);
        var names = new HashSet<string>(collector.OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < 1000; i++)
        {
            string name = $"{prefix} {i}";
            if (!names.Contains(name))
            {
                return name;
            }
        }

        return prefix + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    /// <summary>Espécies do projeto com quantidade (exemplares e maciços), código e unidade.</summary>
    public static List<(Species Species, SpeciesReportRow Row)> ProjectSpecies(ViridianContext ctx, ProjectSnapshot snapshot)
    {
        var result = new List<(Species, SpeciesReportRow)>();
        foreach (SpeciesReportRow row in ReportBuilder.BuildSpecies(snapshot.Plants, snapshot.Massings))
        {
            if (!string.IsNullOrWhiteSpace(row.SpeciesId) && ctx.Lookup(row.SpeciesId) is Species species && row.Total > 0)
            {
                result.Add((species, row));
            }
        }

        return result;
    }
}

/// <summary>Ateliê de paletas: espécies pela harmonia de cores e pela época de floração.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PaletteCommand : CommandBase
{
    protected override bool RequiresProject => false;

    internal override Result Run(ViridianContext ctx)
    {
        var window = new PaletteWindow(ctx.Catalog, ctx.Paths, ctx.Settings, ctx.HasProject);
        if (!ctx.ShowDialog(window) || window.SelectedSpecies is not Species species)
        {
            return Result.Cancelled;
        }

        ctx.RememberSpecies(species);
        return window.Action switch
        {
            BrowserAction.Insert => PlantingFlows.Insert(ctx, species),
            BrowserAction.Distribute => PlantingFlows.Distribute(ctx, species),
            _ => Result.Cancelled,
        };
    }
}

/// <summary>Calendário de floração do projeto em vista de desenho.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class FloweringCalendarCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
        List<(Species Species, SpeciesReportRow Row)> species = DesignFlows.ProjectSpecies(ctx, snapshot);
        if (species.Count == 0)
        {
            throw new ViridianException("Não há vegetação do catálogo no projeto.");
        }

        FloweringCalendar calendar = FloweringCalendar.Build(species.Select(s => (s.Species, s.Row.Code, (double)s.Row.Total)));
        Drawing drawing = CalendarDrawing.Build(calendar, ctx.Settings.Colors, "Calendário de floração");
        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        ViewDrafting view = ctx.Grouped("Viridian: calendário de floração", () =>
            Tx.Run(ctx.Doc, "Viridian: calendário de floração", () => design.DrawingView(ctx.Doc, "Viridian - Calendário de floração", drawing, 1), suppressWarnings: true), _ => true);
        ctx.UiDocument.ActiveView = view;
        return Result.Succeeded;
    }
}

/// <summary>Verifica a composição de um canteiro: equilíbrio, diversidade, textura, escala, sub-bosque e floração.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class BedCheckCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        RegionBoundary region = ctx.PickRegion("verificar a composição");
        for (int round = 0; round < 50; round++)
        {
            List<(PlantRecord Record, Species Species)> plants = DesignFlows.Plants(ctx, view, region.Polygon);
            List<(MassingInfo Info, Species Species)> massings = DesignFlows.Massings(ctx, view, region.Polygon);
            var bedPlants = plants.GroupBy(p => p.Species.Id)
                .Select(g => new BedPlant(g.First().Species, ctx.ProjectCode(g.First().Species), g.Count(), 0))
                .Concat(massings.GroupBy(m => m.Species.Id).Select(g => new BedPlant(g.First().Species, ctx.ProjectCode(g.First().Species),
                    g.Sum(m => Math.Max(0, m.Info.Record.Quantity)), g.Sum(m => m.Info.Record.AreaM2))))
                .GroupBy(b => b.Species.Id)
                .Select(g => new BedPlant(g.First().Species, g.First().Code, g.Sum(x => x.Count), g.Sum(x => x.AreaM2)))
                .ToList();
            (double width, double length) = region.Polygon.OrientedSize();
            var bed = new Bed(region.Source, bedPlants, region.Polygon.Area, width, length);
            IReadOnlyList<CompositionNote> notes = CompositionRules.Evaluate(bed, ctx.Settings.Composition);

            long[] all = plants.Select(p => p.Record.ElementId).Concat(massings.Select(m => m.Info.ElementId)).ToArray();
            var issues = new List<IntegrityIssue>
            {
                new()
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.Composicao,
                    Message = $"Canteiro ({region.Source}): {bed.AreaM2:N1} m², {width:N1} × {length:N1} m, {bedPlants.Count} espécie(s), "
                              + $"{plants.Count} exemplar(es) e {massings.Count} maciço(s)." + (notes.Count == 0 ? " Nenhuma observação: composição equilibrada." : string.Empty),
                    ElementIds = all,
                },
            };
            foreach (CompositionNote note in notes)
            {
                long[] ids = note.SpeciesId.Length == 0
                    ? all
                    : plants.Where(p => p.Species.Id == note.SpeciesId).Select(p => p.Record.ElementId)
                        .Concat(massings.Where(m => m.Species.Id == note.SpeciesId).Select(m => m.Info.ElementId)).ToArray();
                issues.Add(new IntegrityIssue
                {
                    Severity = note.Severity == CompositionSeverity.Aviso ? IssueSeverity.Aviso : IssueSeverity.Info,
                    Kind = IssueKind.Composicao,
                    Message = note.Rule + ": " + note.Message,
                    ElementIds = ids,
                });
            }

            var window = new IntegrityWindow(issues, ids => ctx.ShowElements(ids)) { Title = "Viridian — Composição do canteiro" };
            if (!ctx.ShowDialog(window) || window.Action != IntegrityAction.Recheck)
            {
                return Result.Succeeded;
            }
        }

        return Result.Succeeded;
    }
}

/// <summary>Detalhes de plantio prontos, com notas de especificação.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class PlantingDetailCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        var dialog = new TaskDialog("Viridian — Detalhe de plantio")
        {
            MainInstruction = "Para quais espécies?",
            MainContent = "O detalhe traz a cova ou o berço em corte, com cotas, e as notas de especificação logo abaixo.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Todas as espécies do projeto", "Um detalhe por tipo de plantio (árvores, palmeiras, arbustos, forrações, gramados), com as notas de cada espécie.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Uma espécie", "Escolha a espécie; o detalhe usa as medidas da muda dela.");
        TaskDialogResult choice = dialog.Show();

        var groups = new List<List<(Species, string)>>();
        if (choice == TaskDialogResult.CommandLink1)
        {
            ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
            List<(Species Species, SpeciesReportRow Row)> species = DesignFlows.ProjectSpecies(ctx, snapshot);
            if (species.Count == 0)
            {
                throw new ViridianException("Não há vegetação do catálogo no projeto.");
            }

            groups.AddRange(species.GroupBy(s => PlantingDetails.KindFor(s.Species.Category)).OrderBy(g => g.Key)
                .Select(g => g.Select(s => (s.Species, s.Row.Code)).ToList()));
        }
        else if (choice == TaskDialogResult.CommandLink2)
        {
            Species species = ctx.PickSpecies("Detalhe de plantio — escolha a espécie", "Criar detalhe");
            groups.Add(new List<(Species, string)> { (species, ctx.ProjectCode(species)) });
        }
        else
        {
            return Result.Cancelled;
        }

        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        List<ViewDrafting> views = ctx.Grouped("Viridian: detalhes de plantio", () =>
            Tx.Run(ctx.Doc, "Viridian: detalhes de plantio", () =>
                groups.Select(g => design.PlantingDetailView(ctx.Doc, g, ctx.Settings.Specifications, ctx.Settings.Colors)).ToList(), suppressWarnings: true), _ => true);
        ctx.UiDocument.ActiveView = views[0];
        if (views.Count > 1)
        {
            TaskDialog.Show("Viridian", "Detalhes criados (vistas de desenho):\n\n" + string.Join("\n", views.Select(v => v.Name)));
        }

        return Result.Succeeded;
    }
}

/// <summary>Corte esquemático da vegetação ao longo de uma linha desenhada na planta.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SectionCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        GrowthStage? stage = AskStage("Corte esquemático", "Em que fase as plantas devem aparecer?");
        if (stage is null)
        {
            return Result.Cancelled;
        }

        IReadOnlyList<Point2> line = RegionPicker.PickPoints(ctx.UiDocument, "Corte: clique no início e no fim da linha de corte", 2, 2);
        if (line.Count < 2)
        {
            return Result.Cancelled;
        }

        var plants = DesignFlows.Plants(ctx, view)
            .Select(p =>
            {
                (double crown, double height) = GrowthModel.Size(p.Species, stage.Value);
                return new SectionPlant(p.Species, ctx.ProjectCode(p.Species), p.Record.Location, height, crown);
            })
            .ToList();
        var massings = DesignFlows.Massings(ctx, view)
            .Select(m => new SectionMassing(m.Species, ctx.ProjectCode(m.Species), m.Info.Boundary!))
            .ToList();
        Drawing drawing = SchematicSection.Build(line[0], line[1], plants, massings, ctx.Settings.Colors, depthM: 3);
        drawing.Text(new Point2(0, drawing.Extent().MaxY + 1.0), $"CORTE ESQUEMÁTICO — {GrowthModel.Name(stage.Value).ToUpperInvariant()}", -1, 1.4);
        int scale = DesignService.FitScale(drawing);
        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        ViewDrafting created = ctx.Grouped("Viridian: corte esquemático", () =>
            Tx.Run(ctx.Doc, "Viridian: corte esquemático", () =>
                design.DrawingView(ctx.Doc, DesignFlows.NextViewName(ctx.Doc, "Viridian - Corte esquemático"), drawing, scale), suppressWarnings: true), _ => true);
        ctx.UiDocument.ActiveView = created;
        return Result.Succeeded;
    }

    internal static GrowthStage? AskStage(string title, string instruction)
    {
        var dialog = new TaskDialog("Viridian — " + title)
        {
            MainInstruction = instruction,
            MainContent = "Alturas e copas estimadas pela taxa de crescimento de cada espécie.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
            AllowCancellation = true,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Na maturidade", "Porte adulto do cadastro.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "No plantio", "Altura da muda.");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Aos 3 anos");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Aos 6 anos");
        return dialog.Show() switch
        {
            TaskDialogResult.CommandLink1 => GrowthStage.Maturidade,
            TaskDialogResult.CommandLink2 => GrowthStage.Plantio,
            TaskDialogResult.CommandLink3 => GrowthStage.TresAnos,
            TaskDialogResult.CommandLink4 => GrowthStage.SeisAnos,
            _ => null,
        };
    }
}

/// <summary>Simulação de crescimento em 2D: o canteiro no plantio, aos 3 e 6 anos e na maturidade.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class GrowthCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        ViewPlan view = ctx.RequirePlanView();
        RegionBoundary region = ctx.PickRegion("simular o crescimento");
        var plants = DesignFlows.Plants(ctx, view, region.Polygon).Select(p => new GrowthPlant(p.Species, p.Record.Location)).ToList();
        var massings = DesignFlows.Massings(ctx, view, region.Polygon).Select(m => new GrowthMassing(m.Species, m.Info.Boundary!)).ToList();
        if (plants.Count == 0 && massings.Count == 0)
        {
            throw new ViridianException("Não há vegetação do catálogo dentro da região escolhida.");
        }

        Drawing drawing = GrowthBoard.Build(region.Polygon, plants, massings, ctx.Settings.Colors);
        int scale = DesignService.FitScale(drawing, 0.75);
        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        ViewDrafting created = ctx.Grouped("Viridian: simulação de crescimento", () =>
            Tx.Run(ctx.Doc, "Viridian: simulação de crescimento", () =>
                design.DrawingView(ctx.Doc, DesignFlows.NextViewName(ctx.Doc, "Viridian - Crescimento"), drawing, scale), suppressWarnings: true), _ => true);
        ctx.UiDocument.ActiveView = created;
        if (plants.Count > GrowthBoard.MaxPlants)
        {
            TaskDialog.Show("Viridian", $"A região tem {plants.Count} exemplares; a simulação mostra os {GrowthBoard.MaxPlants} primeiros. Escolha uma região menor para ver todos.");
        }

        return Result.Succeeded;
    }
}

/// <summary>Memorial descritivo ilustrado de paisagismo, em Word editável.</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class MemorialCommand : CommandBase
{
    internal override Result Run(ViridianContext ctx)
    {
        Document doc = ctx.Doc;
        ProjectSnapshot snapshot = ProjectReader.Read(doc, ctx.Lookup);
        List<(Species Species, SpeciesReportRow Row)> species = DesignFlows.ProjectSpecies(ctx, snapshot);
        if (species.Count == 0)
        {
            throw new ViridianException("Não há vegetação do catálogo no projeto para compor o memorial.");
        }

        ProjectInfo? info = doc.ProjectInformation;
        string title = ProjectReader.ProjectTitle(doc);
        var dialog = new SaveFileDialog
        {
            Title = "Salvar memorial descritivo",
            Filter = "Documento do Word (*.docx)|*.docx",
            FileName = $"Memorial de paisagismo - {SafeFile.SanitizeName(string.IsNullOrWhiteSpace(title) ? "Projeto" : title, 60)}.docx",
            AddExtension = true,
            DefaultExt = ".docx",
            OverwritePrompt = true,
            InitialDirectory = string.IsNullOrEmpty(doc.PathName) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(doc.PathName),
        };
        if (dialog.ShowDialog() != true)
        {
            return Result.Cancelled;
        }

        IReadOnlyList<SpeciesReportRow> rows = species.Select(s => s.Row).ToList();
        IReadOnlyList<AcquisitionRow> acquisition = DocumentationFlows.Acquisition(ctx, snapshot);
        var input = new MemorialInput
        {
            Project = new MemorialProject
            {
                Name = Safe(() => info?.Name),
                Number = Safe(() => info?.Number),
                Client = Safe(() => info?.ClientName),
                Address = Safe(() => info?.Address),
                Author = Safe(() => info?.Author),
                Date = DateTime.Today,
            },
            Species = species.Select(s => new MemorialSpecies(
                s.Species,
                s.Row.Code,
                SupplyUnits.IsAreaBased(s.Row.Unit) && s.Row.MassingAreaM2 > 0 ? s.Row.MassingAreaM2 : s.Row.Total,
                s.Row.Unit,
                ctx.Paths.ResolvePhoto(s.Species.PhotoFile))).ToList(),
            Maintenance = MaintenancePlanner.Build(DocumentationFlows.MaintenanceItems(ctx, rows, acquisition), ctx.Settings.Maintenance),
            Specifications = ctx.Settings.Specifications,
            Colors = ctx.Settings.Colors,
            MassingAreaM2 = rows.Sum(r => r.MassingAreaM2),
        };

        try
        {
            DocxWriter.Write(dialog.FileName, MemorialBuilder.Build(input), "Memorial descritivo de paisagismo");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ViridianException("Não foi possível gravar o memorial (verifique se o arquivo está aberto no Word):\n" + ex.Message, ex);
        }

        var done = new TaskDialog("Viridian — Memorial")
        {
            MainInstruction = "Memorial gerado",
            MainContent = dialog.FileName + "\n\nO documento é editável no Word: revise os textos, inclua o seu carimbo e exporte em PDF quando quiser.",
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        done.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir no Word");
        if (done.Show() == TaskDialogResult.CommandLink1)
        {
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }

        return Result.Succeeded;
    }

    private static string Safe(Func<string?> read)
    {
        try
        {
            return read()?.Trim() ?? string.Empty;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return string.Empty;
        }
    }
}

/// <summary>Quadro de especificação da vegetação para a prancha (vista de desenho em escala 1:1).</summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class SpecificationBoardCommand : CommandBase
{
    public const string ViewName = "Viridian - Quadro de especificação";

    internal override Result Run(ViridianContext ctx)
    {
        ProjectSnapshot snapshot = ProjectReader.Read(ctx.Doc, ctx.Lookup);
        List<(Species Species, SpeciesReportRow Row)> species = DesignFlows.ProjectSpecies(ctx, snapshot);
        if (species.Count == 0)
        {
            throw new ViridianException("Não há vegetação do catálogo no projeto.");
        }

        var items = species.Select(s => new SpecificationItem(s.Species, s.Row.Code, s.Row.Total, s.Row.Unit)).ToList();
        Drawing drawing = SpecificationBoard.Build(items, ctx.Settings.Specifications, ctx.Settings.Labels.TextSizeMm);
        var design = new DesignService(ctx.Settings.Labels.TextSizeMm);
        ViewSheet? sheet = ctx.UiDocument.ActiveView as ViewSheet;
        (ViewDrafting view, bool placed) = ctx.Grouped("Viridian: quadro de especificação", () =>
            Tx.Run(ctx.Doc, "Viridian: quadro de especificação", () =>
            {
                ViewDrafting created = design.DrawingView(ctx.Doc, ViewName, drawing, 1);
                bool onSheet = false;
                if (sheet is not null && Viewport.CanAddViewToSheet(ctx.Doc, sheet.Id, created.Id))
                {
                    // Colocado no centro da prancha; o usuário arrasta para a posição definitiva.
                    BoundingBoxUV outline = sheet.Outline;
                    var center = new XYZ((outline.Min.U + outline.Max.U) / 2, (outline.Min.V + outline.Max.V) / 2, 0);
                    Viewport.Create(ctx.Doc, sheet.Id, created.Id, center);
                    onSheet = true;
                }

                return (created, onSheet);
            }, suppressWarnings: true), _ => true);

        if (placed)
        {
            TaskDialog.Show("Viridian", $"Quadro de especificação colocado no centro da prancha \"{sheet!.SheetNumber} - {sheet.Name}\". Arraste-o para a posição desejada.");
        }
        else
        {
            ctx.UiDocument.ActiveView = view;
            TaskDialog.Show("Viridian",
                $"Quadro criado na vista de desenho \"{ViewName}\" ({items.Count} espécie(s)).\n\n" +
                (sheet is not null
                    ? "O quadro já está em outra prancha; o conteúdo foi atualizado lá."
                    : "Para colocá-lo na prancha, abra a prancha e rode o comando de novo, ou arraste a vista do Navegador de projeto para a prancha."));
        }

        return Result.Succeeded;
    }
}
