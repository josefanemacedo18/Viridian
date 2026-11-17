using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>Linha do calendário: uma espécie, a quantidade no projeto e os meses de floração.</summary>
public sealed record CalendarRow(Species Species, string Code, double Quantity, MonthSet Months, bool Showy)
{
    /// <summary>Floração sem informação de meses reconhecível no cadastro.</summary>
    public bool Unknown => Months.IsEmpty;
}

/// <summary>Calendário de floração do projeto: o que está florido em cada mês e os meses sem flores.</summary>
public sealed class FloweringCalendar
{
    private FloweringCalendar(IReadOnlyList<CalendarRow> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<CalendarRow> Rows { get; }

    /// <summary>Espécies com floração em cada mês (índice 0 = janeiro).</summary>
    public int[] SpeciesPerMonth => Enumerable.Range(1, 12).Select(m => Rows.Count(r => r.Months.Contains(m))).ToArray();

    /// <summary>Espécies de floração vistosa em cada mês.</summary>
    public int[] ShowyPerMonth => Enumerable.Range(1, 12).Select(m => Rows.Count(r => r.Showy && r.Months.Contains(m))).ToArray();

    /// <summary>Meses sem nenhuma floração vistosa entre as espécies com meses conhecidos.</summary>
    public MonthSet ShowyGaps
    {
        get
        {
            var set = MonthSet.None;
            int[] showy = ShowyPerMonth;
            for (int m = 1; m <= 12; m++)
            {
                if (showy[m - 1] == 0)
                {
                    set = set.With(m);
                }
            }

            return set;
        }
    }

    public IReadOnlyList<CalendarRow> Unknown => Rows.Where(r => r.Unknown).ToList();

    /// <summary>Monta o calendário. Espécies sem flor vistosa entram, mas não contam para os meses "floridos".</summary>
    public static FloweringCalendar Build(IEnumerable<(Species Species, string Code, double Quantity)> items) =>
        new(items
            .GroupBy(i => i.Species.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First() with { Quantity = g.Sum(x => x.Quantity) })
            .Select(i => new CalendarRow(i.Species, i.Code, i.Quantity, MonthSet.Parse(i.Species.Flowering), !string.IsNullOrWhiteSpace(i.Species.FlowerColor)))
            .OrderBy(r => FirstMonth(r.Months))
            .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());

    private static int FirstMonth(MonthSet months)
    {
        for (int m = 1; m <= 12; m++)
        {
            if (months.Contains(m))
            {
                return m;
            }
        }

        return 13;
    }
}
