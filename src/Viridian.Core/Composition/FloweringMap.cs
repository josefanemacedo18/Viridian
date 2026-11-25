using Viridian.Core.Model;

namespace Viridian.Core.Composition;

/// <summary>Uma espécie dentro de um grupo do painel de floração, com os elementos que a representam na planta.</summary>
public sealed record FloweringMember(Species Species, IReadOnlyList<long> ElementIds)
{
    public MonthSet Months => MonthSet.Parse(Species.Flowering);

    /// <summary>Floração vistosa (com cor de flor cadastrada): é a que conta para a continuidade visual.</summary>
    public bool Showy => !string.IsNullOrWhiteSpace(Species.FlowerColor);
}

/// <summary>
/// Linha do painel de floração na planta: um maciço, as plantas isoladas de uma espécie ou o resumo da vista.
/// </summary>
public sealed class FloweringGroup
{
    public FloweringGroup(string title, string detail, IReadOnlyList<FloweringMember> members, bool isSummary = false)
    {
        Title = title;
        Detail = detail;
        Members = members;
        IsSummary = isSummary;
    }

    public string Title { get; }

    public string Detail { get; }

    public bool IsSummary { get; }

    public IReadOnlyList<FloweringMember> Members { get; }

    /// <summary>Meses com floração vistosa.</summary>
    public MonthSet Showy => Members.Where(m => m.Showy).Aggregate(MonthSet.None, (set, m) => set.Union(m.Months));

    /// <summary>Meses com qualquer floração cadastrada (inclusive discreta).</summary>
    public MonthSet Any => Members.Aggregate(MonthSet.None, (set, m) => set.Union(m.Months));

    /// <summary>Nenhuma espécie do grupo tem meses de floração reconhecíveis.</summary>
    public bool Unknown => Members.All(m => m.Months.IsEmpty);

    /// <summary>Meses sem flor vistosa (vazio quando a floração é desconhecida).</summary>
    public MonthSet Gaps => Unknown ? MonthSet.None : new MonthSet(~Showy.Mask & MonthSet.AllMask);

    /// <summary>Cores das flores vistosas em um mês (1 a 12), sem repetição.</summary>
    public IReadOnlyList<string> ColorsIn(int month) => Members
        .Where(m => m.Showy && m.Months.Contains(month))
        .Select(m => m.Species.FlowerColor.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Espécies floridas em um mês (vistosas primeiro).</summary>
    public IReadOnlyList<Species> FloweringIn(int month) => Members
        .Where(m => m.Months.Contains(month))
        .OrderByDescending(m => m.Showy)
        .Select(m => m.Species)
        .ToList();

    /// <summary>Elementos das espécies floridas no mês (para selecionar ou isolar na vista).</summary>
    public IReadOnlyList<long> ElementsFloweringIn(int month) => Members
        .Where(m => m.Months.Contains(month))
        .SelectMany(m => m.ElementIds)
        .Distinct()
        .ToList();

    public IReadOnlyList<long> AllElements => Members.SelectMany(m => m.ElementIds).Distinct().ToList();

    /// <summary>Texto curto dos meses sem flor, por exemplo "sem flor em jun–ago".</summary>
    public string GapText =>
        Unknown ? "floração não cadastrada"
        : Gaps.IsEmpty ? "flor o ano todo"
        : Gaps.Count == 12 ? (Any.IsEmpty ? "sem flor vistosa" : "só floração discreta")
        : "sem flor em " + Gaps;
}

/// <summary>Monta o painel de floração da vista: uma linha por maciço, uma por espécie isolada e o resumo.</summary>
public static class FloweringMap
{
    /// <param name="massings">Maciços: título (código), espécie e elementos (região e símbolos).</param>
    /// <param name="plants">Exemplares isolados: espécie e elemento.</param>
    public static IReadOnlyList<FloweringGroup> Build(
        IEnumerable<(string Title, Species Species, IReadOnlyList<long> ElementIds)> massings,
        IEnumerable<(Species Species, long ElementId)> plants,
        Func<Species, string>? codeOf = null)
    {
        ArgumentNullException.ThrowIfNull(massings);
        ArgumentNullException.ThrowIfNull(plants);
        codeOf ??= s => s.Code;
        var groups = new List<FloweringGroup>();
        foreach ((string title, Species species, IReadOnlyList<long> ids) in massings.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(new FloweringGroup(title + " · " + species.PopularName, "maciço · " + codeOf(species),
                new[] { new FloweringMember(species, ids) }));
        }

        foreach (IGrouping<string, (Species Species, long ElementId)> g in plants
                     .GroupBy(p => p.Species.Id, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => codeOf(g.First().Species), StringComparer.OrdinalIgnoreCase))
        {
            Species species = g.First().Species;
            int n = g.Count();
            groups.Add(new FloweringGroup(codeOf(species) + " · " + species.PopularName, n == 1 ? "1 exemplar" : n + " exemplares",
                new[] { new FloweringMember(species, g.Select(p => p.ElementId).ToList()) }));
        }

        if (groups.Count == 0)
        {
            return groups;
        }

        // Resumo da vista: todas as espécies juntas, para conferir a continuidade da floração no conjunto.
        List<FloweringMember> all = groups
            .SelectMany(g => g.Members)
            .GroupBy(m => m.Species.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FloweringMember(g.First().Species, g.SelectMany(m => m.ElementIds).Distinct().ToList()))
            .ToList();
        int count = all.Count;
        groups.Insert(0, new FloweringGroup("Toda a vista", count == 1 ? "1 espécie" : count + " espécies", all, isSummary: true));
        return groups;
    }
}
