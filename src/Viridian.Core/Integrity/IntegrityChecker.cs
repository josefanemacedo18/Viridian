using Viridian.Core.Geometry;
using Viridian.Core.Massing;
using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.Core.Integrity;

public enum IssueSeverity
{
    Info = 0,
    Aviso = 1,
    Erro = 2,
}

public enum IssueKind
{
    SemCodigo,
    CodigoDuplicado,
    FamiliaNaoCadastrada,
    EspecieSemIdentificacao,
    DadosDesatualizados,
    MacicoSemDensidade,
    LimiteInvalido,
    ForaDoCanteiro,
    ElementoDuplicado,
    MacicoDuplicado,
    RegistroOrfao,
    EtiquetaOrfa,
    ConflitoDeCopa,
    EspacamentoInsuficiente,
}

/// <summary>Uma inconsistência encontrada no projeto, com os elementos envolvidos.</summary>
public sealed class IntegrityIssue
{
    public IssueSeverity Severity { get; init; }

    public IssueKind Kind { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<long> ElementIds { get; init; } = Array.Empty<long>();

    public string SeverityName => Severity switch
    {
        IssueSeverity.Erro => "Erro",
        IssueSeverity.Aviso => "Aviso",
        _ => "Informação",
    };

    public string KindName => IntegrityChecker.DisplayName(Kind);
}

/// <summary>Tolerâncias das verificações, configuráveis pelo escritório.</summary>
public sealed class AnalysisSettings
{
    /// <summary>Sobreposição de copas tolerada, em porcentagem da soma dos raios.</summary>
    public double CrownOverlapTolerancePercent { get; set; } = 20;

    /// <summary>Copas menores que este diâmetro não entram na verificação de conflito de copa.</summary>
    public double MinCrownForConflictM { get; set; } = 2.0;

    /// <summary>Redução tolerada em relação ao espaçamento recomendado, em porcentagem.</summary>
    public double SpacingTolerancePercent { get; set; } = 10;

    /// <summary>Distância abaixo da qual dois exemplares da mesma espécie são considerados duplicados, em metros.</summary>
    public double DuplicateToleranceM { get; set; } = 0.02;

    public bool CheckCrownConflicts { get; set; } = true;

    public bool CheckSpacing { get; set; } = true;

    public void Normalize()
    {
        CrownOverlapTolerancePercent = Math.Clamp(Finite(CrownOverlapTolerancePercent, 20), 0, 100);
        SpacingTolerancePercent = Math.Clamp(Finite(SpacingTolerancePercent, 10), 0, 100);
        MinCrownForConflictM = Math.Clamp(Finite(MinCrownForConflictM, 2.0), 0, 60);
        DuplicateToleranceM = Math.Clamp(Finite(DuplicateToleranceM, 0.02), 0.001, 1);
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}

/// <summary>Etiqueta Viridian cujo elemento de origem não existe mais.</summary>
public sealed class OrphanLabel
{
    public long ElementId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>Tipo de família cujos dados no projeto diferem do catálogo.</summary>
public sealed class OutdatedType
{
    public long ElementId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>Dados do projeto submetidos à verificação.</summary>
public sealed class IntegrityInput
{
    public IReadOnlyList<PlantRecord> Plants { get; init; } = Array.Empty<PlantRecord>();

    public IReadOnlyList<MassingInfo> Massings { get; init; } = Array.Empty<MassingInfo>();

    public IReadOnlyList<OrphanLabel> OrphanLabels { get; init; } = Array.Empty<OrphanLabel>();

    public IReadOnlyList<OutdatedType> OutdatedTypes { get; init; } = Array.Empty<OutdatedType>();

    /// <summary>Consulta ao catálogo pelo identificador da espécie.</summary>
    public Func<string, Species?> SpeciesLookup { get; init; } = _ => null;

    public AnalysisSettings Settings { get; init; } = new();
}

/// <summary>Verificador de integridade e de conflitos de plantio.</summary>
public static class IntegrityChecker
{
    /// <summary>Quantidade máxima de ocorrências listadas por tipo de verificação.</summary>
    public const int MaxPerKind = 300;

    public static IReadOnlyList<IntegrityIssue> Run(IntegrityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var issues = new List<IntegrityIssue>();
        input.Settings.Normalize();

        CheckCodes(input, issues);
        CheckRegistration(input, issues);
        CheckMassings(input, issues);
        CheckDuplicates(input, issues);
        CheckOrphans(input, issues);
        if (input.Settings.CheckCrownConflicts)
        {
            CheckCrownConflicts(input, issues);
        }

        if (input.Settings.CheckSpacing)
        {
            CheckSpacing(input, issues);
        }

        return issues
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Kind)
            .ToList();
    }

    public static string DisplayName(IssueKind kind) => kind switch
    {
        IssueKind.SemCodigo => "Exemplar sem código",
        IssueKind.CodigoDuplicado => "Código duplicado",
        IssueKind.FamiliaNaoCadastrada => "Família não cadastrada",
        IssueKind.EspecieSemIdentificacao => "Espécie sem identificação botânica",
        IssueKind.DadosDesatualizados => "Dados desatualizados",
        IssueKind.MacicoSemDensidade => "Maciço sem densidade",
        IssueKind.LimiteInvalido => "Limite de região inválido",
        IssueKind.ForaDoCanteiro => "Distribuição fora do canteiro",
        IssueKind.ElementoDuplicado => "Elemento duplicado",
        IssueKind.MacicoDuplicado => "Maciço duplicado",
        IssueKind.RegistroOrfao => "Registro órfão",
        IssueKind.EtiquetaOrfa => "Etiqueta sem elemento",
        IssueKind.ConflitoDeCopa => "Conflito de copa",
        IssueKind.EspacamentoInsuficiente => "Espaçamento insuficiente",
        _ => kind.ToString(),
    };

    // ------------------------------------------------------------------ verificações de dados

    private static void CheckCodes(IntegrityInput input, List<IntegrityIssue> issues)
    {
        var collector = new IssueCollector(issues, IssueKind.SemCodigo, IssueSeverity.Aviso);
        foreach (PlantRecord plant in input.Plants.Where(p => string.IsNullOrWhiteSpace(p.ProjectCode)))
        {
            collector.Add($"{Describe(plant)} não possui código de identificação no projeto.", plant.ElementId);
        }

        collector.Finish();

        var duplicates = new IssueCollector(issues, IssueKind.CodigoDuplicado, IssueSeverity.Erro);
        foreach (IGrouping<string, PlantRecord> group in input.Plants
                     .Where(p => !string.IsNullOrWhiteSpace(p.ProjectCode))
                     .GroupBy(p => p.ProjectCode.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            duplicates.Add($"O código {group.Key} está em {group.Count()} exemplares.", group.Select(p => p.ElementId).ToArray());
        }

        duplicates.Finish();
    }

    private static void CheckRegistration(IntegrityInput input, List<IntegrityIssue> issues)
    {
        var unregistered = new IssueCollector(issues, IssueKind.FamiliaNaoCadastrada, IssueSeverity.Aviso);
        foreach (IGrouping<string, PlantRecord> group in input.Plants.Where(p => !p.IsRegistered).GroupBy(p => p.GroupKey))
        {
            PlantRecord first = group.First();
            unregistered.Add(
                $"A família \"{first.FamilyName} : {first.TypeName}\" não está vinculada a uma espécie do catálogo ({group.Count()} exemplar(es)).",
                group.Select(p => p.ElementId).ToArray());
        }

        unregistered.Finish();

        var unidentified = new IssueCollector(issues, IssueKind.EspecieSemIdentificacao, IssueSeverity.Aviso);
        foreach (IGrouping<string, PlantRecord> group in input.Plants.Where(p => p.IsRegistered).GroupBy(p => p.SpeciesId))
        {
            PlantRecord first = group.First();
            Species? species = input.SpeciesLookup(group.Key);
            if (species is null)
            {
                unidentified.Add(
                    $"A espécie \"{first.PopularName}\" ({first.SpeciesCode}) não foi encontrada no catálogo atual.",
                    group.Select(p => p.ElementId).ToArray());
            }
            else if (string.IsNullOrWhiteSpace(species.ScientificName))
            {
                unidentified.Add(
                    $"A espécie \"{species.PopularName}\" ({species.Code}) está sem nome científico no catálogo.",
                    group.Select(p => p.ElementId).ToArray());
            }
        }

        unidentified.Finish();

        var outdated = new IssueCollector(issues, IssueKind.DadosDesatualizados, IssueSeverity.Info);
        foreach (OutdatedType type in input.OutdatedTypes)
        {
            outdated.Add($"O tipo \"{type.Name}\" difere do catálogo ({type.Reason}). Use Atualizar Dados.", type.ElementId);
        }

        outdated.Finish();
    }

    private static void CheckMassings(IntegrityInput input, List<IntegrityIssue> issues)
    {
        var density = new IssueCollector(issues, IssueKind.MacicoSemDensidade, IssueSeverity.Erro);
        var boundary = new IssueCollector(issues, IssueKind.LimiteInvalido, IssueSeverity.Erro);
        var outside = new IssueCollector(issues, IssueKind.ForaDoCanteiro, IssueSeverity.Aviso);
        var duplicated = new IssueCollector(issues, IssueKind.MacicoDuplicado, IssueSeverity.Erro);

        ILookup<string, PlantRecord> symbolsByMassing = input.Plants
            .Where(p => p.BelongsToMassing)
            .ToLookup(p => p.MassingId.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (MassingInfo massing in input.Massings)
        {
            MassingRecord record = massing.Record;
            string name = string.IsNullOrWhiteSpace(record.Id) ? "(sem código)" : record.Id;

            bool areaBased = SupplyUnits.IsAreaBased(record.SupplyUnit);
            if (!areaBased && !(MassingCalculator.EffectiveDensity(record) > 0))
            {
                density.Add($"O maciço {name} não possui densidade nem espaçamento definidos.", massing.ElementId);
            }

            if (massing.Boundary is null || massing.Boundary.IsEmpty || !(massing.Boundary.Area > 1e-6))
            {
                boundary.Add($"O maciço {name} não possui um limite fechado com área válida.", massing.ElementId);
                continue;
            }

            if (massing.Boundary.HasSelfIntersections())
            {
                boundary.Add($"O limite do maciço {name} cruza a si mesmo.", massing.ElementId);
            }

            if (string.IsNullOrWhiteSpace(record.Id))
            {
                continue;
            }

            // Tolerância de 1 cm para não acusar símbolos apoiados na borda.
            var escaped = symbolsByMassing[record.Id.Trim()]
                .Where(p => !massing.Boundary.Contains(p.Location) && massing.Boundary.DistanceToBoundary(p.Location) > 0.01)
                .Select(p => p.ElementId)
                .ToArray();
            bool otherViewHasSameMassing = input.Massings.Any(m => !ReferenceEquals(m, massing)
                && string.Equals(m.Record.Id.Trim(), record.Id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (escaped.Length > 0 && !otherViewHasSameMassing)
            {
                outside.Add($"{escaped.Length} exemplar(es) do maciço {name} estão fora do limite da região.", escaped);
            }
        }

        // O mesmo código em vistas diferentes é aceito (o maciço aparece em mais de uma vista);
        // na mesma vista indica cópia indevida.
        foreach (var group in input.Massings
                     .Where(m => !string.IsNullOrWhiteSpace(m.Record.Id))
                     .GroupBy(m => (Id: m.Record.Id.Trim().ToUpperInvariant(), m.ViewId))
                     .Where(g => g.Count() > 1))
        {
            duplicated.Add(
                $"O código de maciço {group.Key.Id} aparece {group.Count()} vezes na mesma vista.",
                group.Select(m => m.ElementId).ToArray());
        }

        density.Finish();
        boundary.Finish();
        outside.Finish();
        duplicated.Finish();
    }

    private static void CheckDuplicates(IntegrityInput input, List<IntegrityIssue> issues)
    {
        var collector = new IssueCollector(issues, IssueKind.ElementoDuplicado, IssueSeverity.Erro);
        double tolerance = input.Settings.DuplicateToleranceM;
        foreach (IGrouping<string, PlantRecord> group in input.Plants.GroupBy(p => p.GroupKey + "|" + p.LevelName))
        {
            List<PlantRecord> list = group.ToList();
            if (list.Count < 2)
            {
                continue;
            }

            var reported = new HashSet<long>();
            foreach ((PlantRecord a, PlantRecord b) in PairsWithin(list, tolerance))
            {
                if (reported.Add(a.ElementId) | reported.Add(b.ElementId))
                {
                    collector.Add($"{Describe(a)} está duplicado na mesma posição.", a.ElementId, b.ElementId);
                }
            }
        }

        collector.Finish();
    }

    private static void CheckOrphans(IntegrityInput input, List<IntegrityIssue> issues)
    {
        var massingIds = new HashSet<string>(
            input.Massings.Where(m => !string.IsNullOrWhiteSpace(m.Record.Id)).Select(m => m.Record.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var orphans = new IssueCollector(issues, IssueKind.RegistroOrfao, IssueSeverity.Erro);
        foreach (IGrouping<string, PlantRecord> group in input.Plants
                     .Where(p => p.BelongsToMassing && !massingIds.Contains(p.MassingId.Trim()))
                     .GroupBy(p => p.MassingId.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            orphans.Add(
                $"{group.Count()} exemplar(es) referenciam o maciço {group.Key}, cuja região não existe mais.",
                group.Select(p => p.ElementId).ToArray());
        }

        orphans.Finish();

        var labels = new IssueCollector(issues, IssueKind.EtiquetaOrfa, IssueSeverity.Aviso);
        foreach (OrphanLabel label in input.OrphanLabels)
        {
            labels.Add($"A etiqueta \"{Shorten(label.Text)}\" perdeu o elemento que identificava.", label.ElementId);
        }

        labels.Finish();
    }

    // ------------------------------------------------------------------ análise de posicionamento

    private static void CheckCrownConflicts(IntegrityInput input, List<IntegrityIssue> issues)
    {
        AnalysisSettings settings = input.Settings;
        var collector = new IssueCollector(issues, IssueKind.ConflitoDeCopa, IssueSeverity.Aviso);
        double factor = 1.0 - settings.CrownOverlapTolerancePercent / 100.0;

        foreach (IGrouping<string, PlantRecord> level in input.Plants
                     .Where(p => !p.BelongsToMassing && p.Status != PlantStatus.Removido && p.CrownDiameterM >= settings.MinCrownForConflictM)
                     .GroupBy(p => p.LevelName))
        {
            List<PlantRecord> list = level.ToList();
            if (list.Count < 2)
            {
                continue;
            }

            double reach = list.Max(p => p.CrownDiameterM);
            foreach ((PlantRecord a, PlantRecord b) in PairsWithin(list, reach))
            {
                double distance = a.Location.DistanceTo(b.Location);
                double limit = (a.CrownDiameterM + b.CrownDiameterM) / 2 * factor;
                if (distance < limit && distance > settings.DuplicateToleranceM)
                {
                    collector.Add(
                        $"Copas em conflito: {Describe(a)} e {Describe(b)} estão a {distance:0.00} m (mínimo de {limit:0.00} m).",
                        a.ElementId,
                        b.ElementId);
                }
            }
        }

        collector.Finish();
    }

    private static void CheckSpacing(IntegrityInput input, List<IntegrityIssue> issues)
    {
        AnalysisSettings settings = input.Settings;
        var collector = new IssueCollector(issues, IssueKind.EspacamentoInsuficiente, IssueSeverity.Aviso);
        double factor = 1.0 - settings.SpacingTolerancePercent / 100.0;

        foreach (IGrouping<string, PlantRecord> group in input.Plants
                     .Where(p => !p.BelongsToMassing && p.IsRegistered && p.Status != PlantStatus.Removido && p.RecommendedSpacingM > 0)
                     .GroupBy(p => p.SpeciesId + "|" + p.LevelName))
        {
            List<PlantRecord> list = group.ToList();
            if (list.Count < 2)
            {
                continue;
            }

            double limit = list[0].RecommendedSpacingM * factor;
            foreach ((PlantRecord a, PlantRecord b) in PairsWithin(list, limit))
            {
                double distance = a.Location.DistanceTo(b.Location);
                if (distance < limit && distance > settings.DuplicateToleranceM)
                {
                    collector.Add(
                        $"{Describe(a)} e {Describe(b)} estão a {distance:0.00} m; o espaçamento recomendado é {list[0].RecommendedSpacingM:0.00} m.",
                        a.ElementId,
                        b.ElementId);
                }
            }
        }

        collector.Finish();
    }

    /// <summary>Pares de exemplares cuja distância é menor ou igual ao alcance, usando uma malha espacial.</summary>
    private static IEnumerable<(PlantRecord A, PlantRecord B)> PairsWithin(List<PlantRecord> plants, double reach)
    {
        if (!(reach > 0))
        {
            yield break;
        }

        var cells = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < plants.Count; i++)
        {
            (long, long) key = CellOf(plants[i].Location, reach);
            if (!cells.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                cells[key] = bucket;
            }

            bucket.Add(i);
        }

        for (int i = 0; i < plants.Count; i++)
        {
            (long cx, long cy) = CellOf(plants[i].Location, reach);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out List<int>? bucket))
                    {
                        continue;
                    }

                    foreach (int j in bucket)
                    {
                        if (j > i && plants[i].Location.DistanceTo(plants[j].Location) <= reach)
                        {
                            yield return (plants[i], plants[j]);
                        }
                    }
                }
            }
        }
    }

    private static (long, long) CellOf(Point2 p, double size) =>
        ((long)Math.Floor(p.X / size), (long)Math.Floor(p.Y / size));

    private static string Describe(PlantRecord plant)
    {
        string name = plant.IsRegistered ? $"{plant.SpeciesCode} {plant.PopularName}".Trim() : $"{plant.FamilyName} : {plant.TypeName}";
        return string.IsNullOrWhiteSpace(plant.ProjectCode) ? name : $"{name} [{plant.ProjectCode}]";
    }

    private static string Shorten(string text)
    {
        string single = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 40 ? single : single[..40] + "…";
    }

    /// <summary>Acumula ocorrências de um tipo, limitando a quantidade listada.</summary>
    private sealed class IssueCollector
    {
        private readonly List<IntegrityIssue> _target;
        private readonly IssueKind _kind;
        private readonly IssueSeverity _severity;
        private int _count;

        public IssueCollector(List<IntegrityIssue> target, IssueKind kind, IssueSeverity severity)
        {
            _target = target;
            _kind = kind;
            _severity = severity;
        }

        public void Add(string message, params long[] elementIds)
        {
            _count++;
            if (_count <= MaxPerKind)
            {
                _target.Add(new IntegrityIssue { Severity = _severity, Kind = _kind, Message = message, ElementIds = elementIds });
            }
        }

        public void Finish()
        {
            if (_count > MaxPerKind)
            {
                _target.Add(new IntegrityIssue
                {
                    Severity = IssueSeverity.Info,
                    Kind = _kind,
                    Message = $"Outras {_count - MaxPerKind} ocorrências de \"{DisplayName(_kind)}\" foram omitidas da lista.",
                });
            }
        }
    }
}
