using Autodesk.Revit.DB;
using Viridian.Core.Sheets;
using Viridian.Library;

namespace Viridian.Revit;

/// <summary>Opções da prancha automática.</summary>
public sealed class SheetRequest
{
    /// <summary>Tipo de carimbo (folha); <see cref="ElementId.InvalidElementId"/> cria a prancha sem carimbo.</summary>
    public ElementId TitleBlockTypeId { get; set; } = ElementId.InvalidElementId;

    public string Number { get; set; } = string.Empty;

    public string Name { get; set; } = "Planta de paisagismo";

    /// <summary>Reduz a escala da planta, se necessário, para caber na folha.</summary>
    public bool FitScale { get; set; } = true;

    /// <summary>Altura reservada ao carimbo no pé da coluna da direita, em milímetros.</summary>
    public double StampReserveMm { get; set; } = 70;
}

/// <summary>Resultado da criação da prancha.</summary>
public sealed class SheetOutcome
{
    public SheetOutcome(ViewSheet sheet)
    {
        Sheet = sheet;
    }

    public ViewSheet Sheet { get; }

    /// <summary>Escala aplicada à planta quando foi preciso reduzi-la (0 = mantida).</summary>
    public int NewScale { get; set; }

    public List<string> Messages { get; } = new();
}

/// <summary>Cria pranchas de paisagismo com planta, legenda e tabela diagramadas automaticamente.</summary>
public static class SheetService
{
    private const double MmPerFoot = 304.8;

    /// <summary>Tipos de carimbo (folhas) carregados no projeto.</summary>
    public static IReadOnlyList<FamilySymbol> TitleBlocks(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName + " " + s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> SheetNumbers(Document document)
    {
        using var collector = new FilteredElementCollector(document);
        return collector.OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber).ToList();
    }

    /// <summary>
    /// Vista de planta a colocar na prancha: a própria vista, ou uma vista dependente dela quando já está
    /// em outra prancha (a dependente compartilha maciços e etiquetas, sem duplicá-los). Precisa de transação.
    /// </summary>
    public static View PlacePlanView(Document document, ViewSheet sheet, View plan, List<string> messages)
    {
        if (Viewport.CanAddViewToSheet(document, sheet.Id, plan.Id))
        {
            return plan;
        }

        View source = plan.GetPrimaryViewId() != ElementId.InvalidElementId && document.GetElement(plan.GetPrimaryViewId()) is View primary ? primary : plan;
        if (!source.CanViewBeDuplicated(ViewDuplicateOption.AsDependent))
        {
            throw new ViridianException($"A vista \"{plan.Name}\" já está em outra prancha e não pode ser duplicada como dependente. Duplique-a e tente novamente.");
        }

        var dependent = (View)document.GetElement(source.Duplicate(ViewDuplicateOption.AsDependent));
        try
        {
            dependent.Name = UniqueViewName(document, plan.Name + " - " + sheet.SheetNumber);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Mantém o nome automático do Revit.
        }

        if (plan.CropBoxActive && dependent.CropBoxActive)
        {
            try
            {
                dependent.CropBox = plan.CropBox;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Recorte padrão do Revit.
            }
        }

        messages.Add($"A vista \"{plan.Name}\" já estava em outra prancha; foi criada a vista dependente \"{dependent.Name}\".");
        return dependent;
    }

    /// <summary>Cria a prancha. Precisa de transação aberta.</summary>
    public static SheetOutcome Create(Document document, View plan, ViewDrafting? legend, ViewSchedule? schedule, SheetRequest request)
    {
        ViewSheet sheet = ViewSheet.Create(document, request.TitleBlockTypeId);
        var outcome = new SheetOutcome(sheet);
        SetNumberAndName(document, sheet, request, outcome.Messages);
        document.Regenerate();

        PaperRect paper = PaperOf(document, sheet);
        PaperRect usable = SheetLayout.UsableArea(paper);

        View planView = PlacePlanView(document, sheet, plan, outcome.Messages);

        // Legenda e tabela primeiro, para medir o espaço que ocupam.
        Viewport? legendPort = null;
        if (legend is not null)
        {
            if (Viewport.CanAddViewToSheet(document, sheet.Id, legend.Id))
            {
                legendPort = Viewport.Create(document, sheet.Id, legend.Id, ToSheet(usable.MaxX - 50, usable.MaxY - 50));
            }
            else
            {
                outcome.Messages.Add("A legenda já está em outra prancha; esta prancha foi criada sem ela.");
            }
        }

        ScheduleSheetInstance? scheduleInstance = schedule is null
            ? null
            : ScheduleSheetInstance.Create(document, sheet.Id, schedule.Id, ToSheet(usable.MaxX - 150, usable.MaxY - 150));
        document.Regenerate();

        PaperRect? legendBox = legendPort is null ? null : FromOutline(legendPort.GetBoxOutline());
        PaperRect? scheduleBox = scheduleInstance?.get_BoundingBox(sheet) is BoundingBoxXYZ sb ? FromBox(sb) : null;

        (double planWidth, double planHeight) = PaperSize(planView);
        var input = new SheetLayoutInput
        {
            Usable = usable,
            PlanWidthMm = planWidth,
            PlanHeightMm = planHeight,
            LegendWidthMm = legendBox?.Width ?? 0,
            LegendHeightMm = legendBox?.Height ?? 0,
            ScheduleWidthMm = scheduleBox?.Width ?? 0,
            ScheduleHeightMm = scheduleBox?.Height ?? 0,
            StampReserveMm = request.TitleBlockTypeId == ElementId.InvalidElementId ? 0 : request.StampReserveMm,
        };
        SheetLayoutResult layout = SheetLayout.Compute(input);

        if (!layout.PlanFits && request.FitScale && planView.Scale > 0)
        {
            int scale = SheetLayout.SuggestScale(
                planWidth * planView.Scale / 1000, planHeight * planView.Scale / 1000, layout.PlanArea.Width, layout.PlanArea.Height, planView.Scale);
            if (scale > 0 && scale != planView.Scale && TrySetScale(planView, scale, outcome.Messages))
            {
                outcome.NewScale = scale;
                document.Regenerate();
                (planWidth, planHeight) = PaperSize(planView);
                input.PlanWidthMm = planWidth;
                input.PlanHeightMm = planHeight;
                layout = SheetLayout.Compute(input);
            }
        }

        outcome.Messages.AddRange(layout.Warnings);
        if (!planView.CropBoxActive)
        {
            outcome.Messages.Add("A vista não tem região de corte ativa: a planta ocupa a extensão de tudo o que aparece nela. Ative e ajuste a região de corte para controlar o enquadramento.");
        }

        Viewport planPort = Viewport.Create(document, sheet.Id, planView.Id, ToSheet(layout.Plan.CenterX, layout.Plan.CenterY));
        document.Regenerate();
        planPort.SetBoxCenter(ToSheet(layout.Plan.CenterX, layout.Plan.CenterY));

        if (legendPort is not null && layout.Legend is PaperRect legendTarget)
        {
            legendPort.SetBoxCenter(ToSheet(legendTarget.CenterX, legendTarget.CenterY));
        }

        if (scheduleInstance is not null && scheduleBox is PaperRect current && layout.Schedule is PaperRect target)
        {
            // Move pela diferença entre o canto superior esquerdo atual e o desejado.
            XYZ delta = ToSheet(target.MinX - current.MinX, target.MaxY - current.MaxY);
            scheduleInstance.Point = scheduleInstance.Point + delta;
        }

        return outcome;
    }

    private static void SetNumberAndName(Document document, ViewSheet sheet, SheetRequest request, List<string> messages)
    {
        string number = request.Number.Trim();
        if (number.Length > 0)
        {
            List<string> others;
            using (var collector = new FilteredElementCollector(document))
            {
                others = collector.OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => s.Id != sheet.Id).Select(s => s.SheetNumber).ToList();
            }

            if (others.Any(n => string.Equals(n, number, StringComparison.OrdinalIgnoreCase)))
            {
                number = SheetNumbering.Next(others, PrefixOf(number));
                messages.Add($"O número {request.Number} já existe; a prancha recebeu o número {number}.");
            }

            try
            {
                sheet.SheetNumber = number;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                messages.Add("O número informado não foi aceito pelo Revit; foi mantido o número automático.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            sheet.Name = request.Name.Trim();
        }
    }

    private static string PrefixOf(string number)
    {
        int end = number.Length;
        while (end > 0 && char.IsDigit(number[end - 1]))
        {
            end--;
        }

        return number[..end];
    }

    private static bool TrySetScale(View view, int scale, List<string> messages)
    {
        try
        {
            if (view.ViewTemplateId != ElementId.InvalidElementId)
            {
                messages.Add($"A escala da vista é controlada pelo modelo de vista; a planta não foi reduzida para 1:{scale}.");
                return false;
            }

            int old = view.Scale;
            view.Scale = scale;
            messages.Add($"A escala da planta foi ajustada de 1:{old} para 1:{scale} para caber na folha.");
            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            messages.Add("Não foi possível alterar a escala da planta: " + ex.Message);
            return false;
        }
    }

    /// <summary>Retângulo da folha: o carimbo, quando existe; senão uma A1 deitada.</summary>
    private static PaperRect PaperOf(Document document, ViewSheet sheet)
    {
        using var collector = new FilteredElementCollector(document, sheet.Id);
        Element? titleBlock = collector.OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstElement();
        if (titleBlock?.get_BoundingBox(sheet) is BoundingBoxXYZ box)
        {
            PaperRect rect = FromBox(box);
            if (rect.Width > 50 && rect.Height > 50)
            {
                return rect;
            }
        }

        return new PaperRect(0, 0, 841, 594);
    }

    /// <summary>Tamanho da vista no papel, em milímetros (já considera a escala e a região de corte).</summary>
    private static (double Width, double Height) PaperSize(View view)
    {
        BoundingBoxUV outline = view.Outline;
        return ((outline.Max.U - outline.Min.U) * MmPerFoot, (outline.Max.V - outline.Min.V) * MmPerFoot);
    }

    private static XYZ ToSheet(double xMm, double yMm) => new(xMm / MmPerFoot, yMm / MmPerFoot, 0);

    private static PaperRect FromBox(BoundingBoxXYZ box) =>
        new(box.Min.X * MmPerFoot, box.Min.Y * MmPerFoot, box.Max.X * MmPerFoot, box.Max.Y * MmPerFoot);

    private static PaperRect FromOutline(Outline outline) =>
        new(outline.MinimumPoint.X * MmPerFoot, outline.MinimumPoint.Y * MmPerFoot, outline.MaximumPoint.X * MmPerFoot, outline.MaximumPoint.Y * MmPerFoot);

    private static string UniqueViewName(Document document, string name)
    {
        using var collector = new FilteredElementCollector(document);
        var names = new HashSet<string>(collector.OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        string candidate = name;
        for (int i = 2; names.Contains(candidate); i++)
        {
            candidate = $"{name} ({i})";
        }

        return candidate;
    }
}
