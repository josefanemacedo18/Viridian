using System.Globalization;
using Viridian.Core.Export;
using Viridian.Core.Costs;
using Viridian.Core.Maintenance;
using Viridian.Core.Model;

namespace Viridian.Core.Reports;

/// <summary>Montagem das abas da planilha de quantitativos.</summary>
public static class ReportSheets
{
    public static IReadOnlyList<XlsxSheet> Build(
        string projectTitle,
        IReadOnlyList<SpeciesReportRow> species,
        IReadOnlyList<MassingReportRow> massings,
        IReadOnlyList<AcquisitionRow> acquisition,
        DateTime generatedAt,
        Budget? budget = null,
        IReadOnlyList<MaintenanceTask>? maintenance = null,
        string priceReference = "")
    {
        string subtitle = (string.IsNullOrWhiteSpace(projectTitle) ? string.Empty : projectTitle + " · ")
                          + "Gerado pelo Viridian em " + generatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));

        var bySpecies = new XlsxSheet("Por espécie");
        bySpecies.TitleLines.Add("Relatório de vegetação por espécie");
        bySpecies.TitleLines.Add(subtitle);
        bySpecies.Columns.AddRange(new[]
        {
            new XlsxColumn("Código", 10), new XlsxColumn("Nome popular", 26), new XlsxColumn("Nome científico", 32),
            new XlsxColumn("Categoria", 12), new XlsxColumn("Novos", 9, XlsxFormat.Integer), new XlsxColumn("Existentes", 10, XlsxFormat.Integer),
            new XlsxColumn("Transplantados", 13, XlsxFormat.Integer), new XlsxColumn("Removidos", 10, XlsxFormat.Integer),
            new XlsxColumn("Em maciços", 11, XlsxFormat.Integer), new XlsxColumn("Total", 10, XlsxFormat.Integer), new XlsxColumn("Unidade", 10),
        });
        foreach (SpeciesReportRow r in species)
        {
            bySpecies.Rows.Add(new XlsxRow(r.Code, r.PopularName, r.ScientificName, r.CategoryName, r.New, r.Existing, r.Transplanted, r.Removed, r.InMassings, r.Total, r.Unit));
        }

        bySpecies.Rows.Add(new XlsxRow("Total", null, null, null, species.Sum(r => r.New), species.Sum(r => r.Existing),
            species.Sum(r => r.Transplanted), species.Sum(r => r.Removed), species.Sum(r => r.InMassings), species.Sum(r => r.Total), null) { Bold = true });

        var byMassing = new XlsxSheet("Maciços");
        byMassing.TitleLines.Add("Relatório de maciços vegetais");
        byMassing.TitleLines.Add(subtitle);
        byMassing.Columns.AddRange(new[]
        {
            new XlsxColumn("Maciço", 10), new XlsxColumn("Espécie", 10), new XlsxColumn("Nome popular", 24), new XlsxColumn("Nome científico", 28),
            new XlsxColumn("Área (m²)", 11, XlsxFormat.Decimal), new XlsxColumn("Densidade (un/m²)", 13, XlsxFormat.Decimal),
            new XlsxColumn("Espaçamento (m)", 13, XlsxFormat.Decimal), new XlsxColumn("Quantidade", 11, XlsxFormat.Integer),
            new XlsxColumn("Unidade", 10), new XlsxColumn("Reserva (%)", 10, XlsxFormat.Decimal), new XlsxColumn("Representação", 22), new XlsxColumn("Vista", 22),
        });
        foreach (MassingReportRow r in massings)
        {
            byMassing.Rows.Add(new XlsxRow(r.Id, r.SpeciesCode, r.PopularName, r.ScientificName, Math.Round(r.AreaM2, 2), Math.Round(r.DensityPerM2, 2),
                Math.Round(r.SpacingM, 3), r.Quantity, r.Unit, r.ReservePercent, r.Mode, r.ViewName));
        }

        byMassing.Rows.Add(new XlsxRow("Total", null, null, null, Math.Round(massings.Sum(r => r.AreaM2), 2), null, null, massings.Sum(r => r.Quantity), null, null, null, null) { Bold = true });

        var purchase = new XlsxSheet("Aquisição");
        purchase.TitleLines.Add("Lista de aquisição de mudas e insumos vegetais");
        purchase.TitleLines.Add(subtitle);
        purchase.Columns.AddRange(new[]
        {
            new XlsxColumn("Código", 10), new XlsxColumn("Nome popular", 26), new XlsxColumn("Nome científico", 32),
            new XlsxColumn("Quantidade de projeto", 14, XlsxFormat.Integer), new XlsxColumn("Reserva de reposição", 14, XlsxFormat.Integer),
            new XlsxColumn("Quantidade de compra", 14, XlsxFormat.Integer), new XlsxColumn("Unidade", 10),
            new XlsxColumn("Preço unitário", 14, XlsxFormat.Currency), new XlsxColumn("Total", 16, XlsxFormat.Currency),
        });
        foreach (AcquisitionRow r in acquisition)
        {
            purchase.Rows.Add(new XlsxRow(r.Code, r.PopularName, r.ScientificName, r.Quantity, r.Reserve, r.PurchaseQuantity, r.Unit, r.UnitPrice, r.Total));
        }

        purchase.Rows.Add(new XlsxRow("Total", null, null, acquisition.Sum(r => r.Quantity), acquisition.Sum(r => r.Reserve),
            acquisition.Sum(r => r.PurchaseQuantity), null, null, acquisition.Sum(r => r.Total)) { Bold = true });

        var sheets = new List<XlsxSheet> { bySpecies, byMassing, purchase };
        if (budget is not null && budget.Rows.Count > 0)
        {
            sheets.Add(BudgetSheet(budget, subtitle, priceReference));
        }

        if (maintenance is not null && maintenance.Count > 0)
        {
            sheets.Add(MaintenanceSheet(maintenance, subtitle));
        }

        return sheets;
    }

    private static XlsxSheet BudgetSheet(Budget budget, string subtitle, string reference)
    {
        var sheet = new XlsxSheet("Orçamento");
        sheet.TitleLines.Add("Orçamento de paisagismo — fornecimento e plantio");
        sheet.TitleLines.Add(subtitle);
        sheet.TitleLines.Add(
            (string.IsNullOrWhiteSpace(budget.TableName) ? "Preços do cadastro de espécies" : "Tabela de preços: " + budget.TableName)
            + (string.IsNullOrWhiteSpace(reference) ? string.Empty : " (" + reference + ")")
            + $" · BDI {budget.BdiPercent.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))}%");
        sheet.Columns.AddRange(new[]
        {
            new XlsxColumn("Item", 7), new XlsxColumn("Código", 12), new XlsxColumn("Fonte", 10), new XlsxColumn("Descrição", 52),
            new XlsxColumn("Unidade", 9), new XlsxColumn("Quantidade", 11, XlsxFormat.Decimal), new XlsxColumn("Custo unitário", 14, XlsxFormat.Currency),
            new XlsxColumn("Preço unitário com BDI", 16, XlsxFormat.Currency), new XlsxColumn("Total", 16, XlsxFormat.Currency), new XlsxColumn("Observação", 40),
        });

        foreach (IGrouping<string, BudgetRow> group in budget.Rows.GroupBy(r => r.Group))
        {
            sheet.Rows.Add(new XlsxRow(group.First().Item.Split('.')[0], null, null, group.Key.ToUpperInvariant(), null, null, null, null, group.Sum(r => r.Total), null) { Bold = true });
            foreach (BudgetRow r in group)
            {
                sheet.Rows.Add(new XlsxRow(r.Item, r.PriceCode.Length > 0 ? r.PriceCode : r.SpeciesCode, r.SourceName, r.Description, r.Unit, r.Quantity,
                    r.UnitCost, r.UnitPrice, r.Total, r.Note));
            }
        }

        sheet.Rows.Add(new XlsxRow(null, null, null, "TOTAL GERAL", null, null, null, null, budget.Total, budget.MissingPrices > 0 ? $"{budget.MissingPrices} item(ns) sem preço" : null) { Bold = true });
        return sheet;
    }

    private static XlsxSheet MaintenanceSheet(IReadOnlyList<MaintenanceTask> tasks, string subtitle)
    {
        var sheet = new XlsxSheet("Manutenção");
        sheet.TitleLines.Add("Cronograma de manutenção da vegetação");
        sheet.TitleLines.Add(subtitle);
        sheet.Columns.AddRange(new[]
        {
            new XlsxColumn("Fase", 18), new XlsxColumn("Atividade", 12), new XlsxColumn("Espécie", 9), new XlsxColumn("Nome popular", 22),
            new XlsxColumn("Serviço", 36), new XlsxColumn("Frequência", 24),
        });
        foreach (string month in MonthSet.Abbreviations)
        {
            sheet.Columns.Add(new XlsxColumn(month, 5));
        }

        sheet.Columns.Add(new XlsxColumn("Observações", 60));
        foreach (MaintenanceTask t in tasks)
        {
            var cells = new List<object?> { t.PhaseName, t.ActivityName, t.SpeciesCode, t.PopularName, t.Title, t.Frequency };
            for (int m = 1; m <= 12; m++)
            {
                cells.Add(t.Months.Contains(m) ? "●" : null);
            }

            cells.Add(t.Notes);
            sheet.Rows.Add(new XlsxRow(cells.ToArray()));
        }

        return sheet;
    }
}
