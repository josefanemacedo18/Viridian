using System.Globalization;

namespace Viridian.Core.Sheets;

/// <summary>Retângulo no papel, em milímetros.</summary>
public readonly record struct PaperRect(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    public double CenterX => (MinX + MaxX) / 2;

    public double CenterY => (MinY + MaxY) / 2;

    public static PaperRect FromCenter(double cx, double cy, double width, double height) =>
        new(cx - width / 2, cy - height / 2, cx + width / 2, cy + height / 2);

    public PaperRect Inset(double left, double bottom, double right, double top) =>
        new(MinX + left, MinY + bottom, MaxX - right, MaxY - top);

    public bool Contains(PaperRect other, double tolerance = 0.01) =>
        other.MinX >= MinX - tolerance && other.MinY >= MinY - tolerance && other.MaxX <= MaxX + tolerance && other.MaxY <= MaxY + tolerance;

    public bool Overlaps(PaperRect other) =>
        other.MinX < MaxX - 0.01 && other.MaxX > MinX + 0.01 && other.MinY < MaxY - 0.01 && other.MaxY > MinY + 0.01;
}

/// <summary>Medidas, no papel, do que vai para a prancha.</summary>
public sealed class SheetLayoutInput
{
    /// <summary>Área útil da folha (dentro das margens).</summary>
    public PaperRect Usable { get; set; }

    public double PlanWidthMm { get; set; }

    public double PlanHeightMm { get; set; }

    /// <summary>Largura e altura da legenda (zero quando não há legenda).</summary>
    public double LegendWidthMm { get; set; }

    public double LegendHeightMm { get; set; }

    /// <summary>Largura e altura da tabela (zero quando não há tabela).</summary>
    public double ScheduleWidthMm { get; set; }

    public double ScheduleHeightMm { get; set; }

    /// <summary>Altura reservada ao carimbo no pé da coluna lateral.</summary>
    public double StampReserveMm { get; set; } = 70;

    /// <summary>Espaço entre os elementos.</summary>
    public double GapMm { get; set; } = 8;

    /// <summary>Largura mínima da coluna lateral (folha com coluna desenhada); zero = a da legenda e da tabela.</summary>
    public double MinColumnWidthMm { get; set; }

    /// <summary>Espaço reservado no alto da coluna (faixa de título da folha), acima da legenda.</summary>
    public double ColumnTopInsetMm { get; set; }
}

/// <summary>Posições calculadas na prancha.</summary>
public sealed class SheetLayoutResult
{
    public PaperRect Plan { get; init; }

    public PaperRect? Legend { get; init; }

    public PaperRect? Schedule { get; init; }

    /// <summary>Área disponível para a planta (à esquerda da coluna lateral).</summary>
    public PaperRect PlanArea { get; init; }

    public bool PlanFits { get; init; }

    public bool ColumnFits { get; init; }

    /// <summary>Largura da coluna lateral usada (mm); zero quando não há coluna.</summary>
    public double ColumnWidthMm { get; init; }

    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Diagramação automática da prancha de paisagismo: a planta ocupa a área à esquerda e a coluna
/// da direita recebe a legenda (no alto) e a tabela de quantitativos (abaixo), deixando livre o canto
/// inferior direito para o carimbo.
/// </summary>
public static class SheetLayout
{
    /// <summary>Escalas usuais de projeto de paisagismo (denominadores).</summary>
    public static readonly IReadOnlyList<int> StandardScales = new[] { 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000, 1250, 1500, 2000, 2500, 5000 };

    /// <summary>Margens da folha conforme a NBR 10068: 25 mm à esquerda (encadernação) e 10 mm nas demais.</summary>
    public static PaperRect UsableArea(PaperRect sheet) => sheet.Inset(25, 10, 10, 10);

    public static SheetLayoutResult Compute(SheetLayoutInput input)
    {
        PaperRect usable = input.Usable;
        double gap = Math.Max(0, input.GapMm);
        bool hasLegend = input.LegendWidthMm > 0 && input.LegendHeightMm > 0;
        bool hasSchedule = input.ScheduleWidthMm > 0 && input.ScheduleHeightMm > 0;
        double columnWidth = Math.Max(Math.Max(0, input.MinColumnWidthMm), Math.Max(hasLegend ? input.LegendWidthMm : 0, hasSchedule ? input.ScheduleWidthMm : 0));

        PaperRect planArea = columnWidth > 0
            ? new PaperRect(usable.MinX, usable.MinY, usable.MaxX - columnWidth - gap, usable.MaxY)
            : usable;
        bool planFits = input.PlanWidthMm <= planArea.Width + 0.01 && input.PlanHeightMm <= planArea.Height + 0.01;
        PaperRect plan = PaperRect.FromCenter(planArea.CenterX, planArea.CenterY, input.PlanWidthMm, input.PlanHeightMm);

        double columnLeft = usable.MaxX - columnWidth;
        double top = usable.MaxY - Math.Max(0, input.ColumnTopInsetMm);
        PaperRect? legend = null;
        PaperRect? schedule = null;
        if (hasLegend)
        {
            legend = new PaperRect(columnLeft, top - input.LegendHeightMm, columnLeft + input.LegendWidthMm, top);
            top -= input.LegendHeightMm + gap;
        }

        if (hasSchedule)
        {
            schedule = new PaperRect(columnLeft, top - input.ScheduleHeightMm, columnLeft + input.ScheduleWidthMm, top);
            top -= input.ScheduleHeightMm;
        }

        bool columnFits = columnWidth == 0 || top >= usable.MinY + Math.Max(0, input.StampReserveMm) - 0.01;
        var result = new SheetLayoutResult
        {
            Plan = plan,
            Legend = legend,
            Schedule = schedule,
            PlanArea = planArea,
            PlanFits = planFits,
            ColumnFits = columnFits,
            ColumnWidthMm = columnWidth,
        };

        CultureInfo pt = CultureInfo.GetCultureInfo("pt-BR");
        if (!planFits)
        {
            result.Warnings.Add(string.Format(pt,
                "A planta ({0:0} × {1:0} mm no papel) não cabe na área disponível ({2:0} × {3:0} mm). Reduza a escala, ajuste a região de corte ou use uma folha maior.",
                input.PlanWidthMm, input.PlanHeightMm, planArea.Width, planArea.Height));
        }

        if (!columnFits)
        {
            result.Warnings.Add("A legenda e a tabela avançam sobre a área reservada ao carimbo. Use uma folha maior ou reduza a quantidade de espécies por prancha.");
        }

        return result;
    }

    /// <summary>
    /// Menor escala usual (maior denominador apenas quando necessário) em que um desenho com as medidas reais
    /// informadas cabe na área disponível. Retorna 0 quando nem a maior escala usual serve.
    /// </summary>
    public static int SuggestScale(double modelWidthM, double modelHeightM, double availableWidthMm, double availableHeightMm, int currentScale = 0)
    {
        if (!(modelWidthM > 0) || !(modelHeightM > 0) || !(availableWidthMm > 0) || !(availableHeightMm > 0))
        {
            return currentScale;
        }

        if (currentScale > 0 && Fits(currentScale))
        {
            return currentScale; // já cabe: mantém a escala escolhida
        }

        foreach (int scale in StandardScales)
        {
            if (scale > currentScale && Fits(scale))
            {
                return scale;
            }
        }

        return 0;

        bool Fits(int scale) => modelWidthM * 1000 / scale <= availableWidthMm && modelHeightM * 1000 / scale <= availableHeightMm;
    }
}

/// <summary>Numeração das pranchas de paisagismo.</summary>
public static class SheetNumbering
{
    /// <summary>Próximo número livre com o prefixo informado, por exemplo "PAI-01", "PAI-02"...</summary>
    public static string Next(IEnumerable<string> existing, string prefix = "PAI-", int digits = 2)
    {
        var used = new HashSet<string>(existing.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < 10000; i++)
        {
            string candidate = prefix + i.ToString(new string('0', Math.Clamp(digits, 1, 4)), CultureInfo.InvariantCulture);
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return prefix + Guid.NewGuid().ToString("N")[..6];
    }
}
