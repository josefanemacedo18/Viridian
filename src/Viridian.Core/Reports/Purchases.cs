using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Viridian.Core.Reports;

/// <summary>Quantidade já comprada de um item da lista de aquisição.</summary>
public sealed class PurchaseRecord
{
    /// <summary>Identificador da espécie ou, para famílias não cadastradas, "código|nome".</summary>
    public string Key { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string PopularName { get; set; } = string.Empty;

    public string ScientificName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

/// <summary>Registro de compras do projeto, gravado no próprio arquivo do Revit.</summary>
public sealed class PurchaseLedger
{
    public int SchemaVersion { get; set; } = 1;

    public List<PurchaseRecord> Items { get; set; } = new();

    public DateTime? UpdatedAt { get; set; }

    public bool IsEmpty => Items.Count == 0;

    public PurchaseLedger Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        UpdatedAt = UpdatedAt,
        Items = Items.Select(i => new PurchaseRecord
        {
            Key = i.Key, Code = i.Code, PopularName = i.PopularName, ScientificName = i.ScientificName, Unit = i.Unit, Quantity = i.Quantity,
        }).ToList(),
    };
}

/// <summary>Linha do comparativo entre a lista de aquisição atual e o que já foi comprado.</summary>
public sealed class PurchaseComparisonRow : INotifyPropertyChanged
{
    private int _purchased;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string PopularName { get; init; } = string.Empty;

    public string ScientificName { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    /// <summary>Quantidade de compra exigida pelo projeto atual (com a reserva de reposição).</summary>
    public int Needed { get; init; }

    /// <summary>Verdadeiro quando o item saiu do projeto (espécie substituída ou removida) mas consta das compras.</summary>
    public bool OutOfProject => Needed == 0;

    /// <summary>Quantidade já comprada (editável).</summary>
    public int Purchased
    {
        get => _purchased;
        set
        {
            int v = Math.Max(0, value);
            if (v == _purchased)
            {
                return;
            }

            _purchased = v;
            Notify();
            Notify(nameof(Difference));
            Notify(nameof(Status));
        }
    }

    /// <summary>Comprado menos necessário: negativo falta comprar, positivo sobra.</summary>
    public int Difference => Purchased - Needed;

    public string Status => PurchaseComparison.StatusOf(Needed, Purchased);

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Comparativo da aquisição: o que o projeto pede hoje contra o que já foi comprado.</summary>
public static class PurchaseComparison
{
    public static string KeyOf(AcquisitionRow row) =>
        row.SpeciesId.Length > 0 ? row.SpeciesId : (row.Code + "|" + row.PopularName).Trim();

    public static string StatusOf(int needed, int purchased)
    {
        if (needed == 0)
        {
            return purchased > 0 ? $"Fora do projeto: sobram {purchased}" : "Fora do projeto";
        }

        int diff = purchased - needed;
        return diff switch
        {
            0 => "Atendido",
            < 0 when purchased == 0 => $"A comprar: {needed}",
            < 0 => $"Falta comprar {-diff}",
            _ => $"Sobram {diff}",
        };
    }

    public static List<PurchaseComparisonRow> Build(IReadOnlyList<AcquisitionRow> acquisition, PurchaseLedger? ledger)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var bought = new Dictionary<string, PurchaseRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (PurchaseRecord r in ledger?.Items ?? new List<PurchaseRecord>())
        {
            if (!string.IsNullOrWhiteSpace(r.Key))
            {
                if (bought.TryGetValue(r.Key, out PurchaseRecord? existing))
                {
                    existing.Quantity += Math.Max(0, r.Quantity);
                }
                else
                {
                    bought[r.Key] = new PurchaseRecord
                    {
                        Key = r.Key, Code = r.Code, PopularName = r.PopularName, ScientificName = r.ScientificName, Unit = r.Unit, Quantity = Math.Max(0, r.Quantity),
                    };
                }
            }
        }

        var rows = new List<PurchaseComparisonRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, AcquisitionRow> group in acquisition.GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase))
        {
            AcquisitionRow first = group.First();
            seen.Add(group.Key);
            rows.Add(new PurchaseComparisonRow
            {
                Key = group.Key,
                Code = first.Code,
                PopularName = first.PopularName,
                ScientificName = first.ScientificName,
                Unit = first.Unit,
                Needed = group.Sum(a => a.PurchaseQuantity),
                Purchased = bought.TryGetValue(group.Key, out PurchaseRecord? p) ? p.Quantity : 0,
            });
        }

        foreach (PurchaseRecord p in bought.Values.Where(p => !seen.Contains(p.Key) && p.Quantity > 0))
        {
            rows.Add(new PurchaseComparisonRow
            {
                Key = p.Key, Code = p.Code, PopularName = p.PopularName, ScientificName = p.ScientificName, Unit = p.Unit, Needed = 0, Purchased = p.Quantity,
            });
        }

        return rows.OrderBy(r => r.OutOfProject).ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.PopularName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Registro de compras a partir do comparativo editado (itens sem compra não são gravados).</summary>
    public static PurchaseLedger ToLedger(IEnumerable<PurchaseComparisonRow> rows, DateTime now) => new()
    {
        UpdatedAt = now,
        Items = rows.Where(r => r.Purchased > 0).Select(r => new PurchaseRecord
        {
            Key = r.Key, Code = r.Code, PopularName = r.PopularName, ScientificName = r.ScientificName, Unit = r.Unit, Quantity = r.Purchased,
        }).ToList(),
    };

    /// <summary>Resumo: itens a comprar, unidades em falta e unidades sobrando.</summary>
    public static (int ItemsMissing, int UnitsMissing, int UnitsSurplus) Summary(IEnumerable<PurchaseComparisonRow> rows)
    {
        var list = rows.ToList();
        return (list.Count(r => r.Difference < 0), list.Where(r => r.Difference < 0).Sum(r => -r.Difference), list.Where(r => r.Difference > 0).Sum(r => r.Difference));
    }
}
