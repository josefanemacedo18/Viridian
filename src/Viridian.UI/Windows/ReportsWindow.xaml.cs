using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Viridian.Core.Config;
using Viridian.Core.Costs;
using Viridian.Core.Export;
using Viridian.Core.Maintenance;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.UI.Windows;

public enum ReportsAction
{
    None,
    CreateSchedules,
    CreateLegend,
}

/// <summary>Dados para as abas de orçamento e manutenção.</summary>
public sealed class ReportExtras
{
    public Func<string, Species?> Lookup { get; init; } = _ => null;

    public LibraryPaths Paths { get; init; } = new(LibraryPaths.DefaultRoot);

    /// <summary>Configurações do escritório; BDI, referência, mês de plantio e estação seca são editados aqui.</summary>
    public ViridianSettings Settings { get; init; } = new();

    public IReadOnlyList<MaintenanceItem> MaintenanceItems { get; init; } = Array.Empty<MaintenanceItem>();

    /// <summary>Grava o código da tabela de preços na espécie (identificador, código, é serviço). Retorna falso se não gravou.</summary>
    public Func<string, string, bool, bool>? LinkCode { get; init; }
}

/// <summary>Linha do cronograma com os meses marcados, para a grade.</summary>
public sealed class MaintenanceRow
{
    public MaintenanceRow(MaintenanceTask task)
    {
        Task = task;
    }

    public MaintenanceTask Task { get; }

    public string PhaseName => Task.PhaseName;

    public string SpeciesCode => Task.SpeciesCode;

    public string PopularName => Task.PopularName;

    public string Title => Task.Title;

    public string Frequency => Task.Frequency;

    public string Notes => Task.Notes;

    private string Mark(int month) => Task.Months.Contains(month) ? "●" : string.Empty;

    public string M1 => Mark(1);

    public string M2 => Mark(2);

    public string M3 => Mark(3);

    public string M4 => Mark(4);

    public string M5 => Mark(5);

    public string M6 => Mark(6);

    public string M7 => Mark(7);

    public string M8 => Mark(8);

    public string M9 => Mark(9);

    public string M10 => Mark(10);

    public string M11 => Mark(11);

    public string M12 => Mark(12);
}

/// <summary>
/// Relatórios: quantitativos por espécie e por maciço, aquisição, orçamento com tabela de preços importada
/// e cronograma de manutenção, com exportação para Excel.
/// </summary>
public partial class ReportsWindow : ViridianWindow
{
    private readonly IReadOnlyList<SpeciesReportRow> _species;
    private readonly IReadOnlyList<MassingReportRow> _massings;
    private readonly IReadOnlyList<AcquisitionRow> _acquisition;
    private readonly string _projectTitle;
    private readonly ReportExtras _extras;
    private PriceTable? _table;
    private Budget _budget = new();
    private IReadOnlyList<MaintenanceTask> _maintenance = Array.Empty<MaintenanceTask>();
    private bool _loading = true;

    public ReportsWindow(
        string projectTitle,
        IReadOnlyList<SpeciesReportRow> species,
        IReadOnlyList<MassingReportRow> massings,
        IReadOnlyList<AcquisitionRow> acquisition,
        ReportExtras extras,
        int initialTab = 0)
    {
        _projectTitle = projectTitle;
        _species = species;
        _massings = massings;
        _acquisition = acquisition;
        _extras = extras;
        InitializeComponent();

        ProjectText.Text = (string.IsNullOrWhiteSpace(projectTitle) ? string.Empty : projectTitle + "   ·   ")
                           + $"{species.Sum(s => s.Total).ToString("N0", Num.PtBr)} unidades em {species.Count} espécie(s)"
                           + $"   ·   {massings.Count} maciço(s)";
        SpeciesGrid.ItemsSource = species;
        MassingGrid.ItemsSource = massings;
        AcquisitionGrid.ItemsSource = acquisition;
        decimal total = acquisition.Sum(a => a.Total);
        AcquisitionTotal.Text = "Total estimado: " + total.ToString("C2", Num.PtBr)
                                + (acquisition.Any(a => a.UnitPrice == 0) ? "   (há itens sem preço cadastrado)" : string.Empty);

        foreach (DataGrid grid in new[] { SpeciesGrid, MassingGrid, AcquisitionGrid, BudgetGrid, MaintenanceGrid })
        {
            grid.Language = Language;
        }

        _table = PriceTableStore.Load(extras.Paths);
        BdiBox.Text = Num.Format(extras.Settings.Costs.BdiPercent);
        ReferenceBox.Text = extras.Settings.Costs.PriceReference;
        for (int m = 1; m <= 12; m++)
        {
            PlantingMonthBox.Items.Add(new Choice<int>(Num.PtBr.DateTimeFormat.GetMonthName(m), m));
        }

        PlantingMonthBox.SelectedIndex = Math.Clamp(extras.Settings.Maintenance.PlantingMonth, 1, 12) - 1;
        DrySeasonBox.Text = extras.Settings.Maintenance.DrySeason;

        _loading = false;
        RefreshBudget();
        RefreshMaintenance();

        switch (initialTab)
        {
            case 1:
                MassingTab.IsSelected = true;
                break;
            case 2:
                AcquisitionTab.IsSelected = true;
                break;
            case 3:
                BudgetTab.IsSelected = true;
                break;
            case 4:
                MaintenanceTab.IsSelected = true;
                break;
            default:
                SpeciesTab.IsSelected = true;
                break;
        }
    }

    public ReportsAction Action { get; private set; } = ReportsAction.None;

    /// <summary>Indica que BDI, referência, mês de plantio ou estação seca mudaram e devem ser gravados.</summary>
    public bool SettingsChanged { get; private set; }

    /// <summary>Indica que o cadastro de alguma espécie foi alterado (códigos de preço vinculados).</summary>
    public bool CatalogChanged { get; private set; }

    // ------------------------------------------------------------------ orçamento

    private void RefreshBudget()
    {
        if (_loading)
        {
            return;
        }

        double bdi = Num.TryParse(BdiBox.Text, out double value) && value is >= 0 and <= 200 ? value : _extras.Settings.Costs.BdiPercent;
        _budget = BudgetBuilder.Build(_acquisition, _extras.Lookup, _table, bdi);
        BudgetGrid.ItemsSource = _budget.Rows;
        TableText.Text = _table is null
            ? "Nenhuma tabela de preços importada: o orçamento usa o preço unitário do cadastro das espécies."
            : $"Tabela: {_table.Name} · {_table.Items.Count.ToString("N0", Num.PtBr)} itens · importada em {_table.ImportedAt:dd/MM/yyyy}";
        RemoveTableButton.IsEnabled = _table is not null;
        BudgetTotal.Text = $"Fornecimento: {_budget.SupplyTotal.ToString("C2", Num.PtBr)}   ·   Serviços: {_budget.ServiceTotal.ToString("C2", Num.PtBr)}"
                           + $"   ·   Total com BDI: {_budget.Total.ToString("C2", Num.PtBr)}"
                           + (_budget.MissingPrices > 0 ? $"\n{_budget.MissingPrices} item(ns) sem preço" : string.Empty);
        UpdateLinkButtons();
    }

    private void Budget_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (Num.TryParse(BdiBox.Text, out double bdi) && bdi is >= 0 and <= 200 && Math.Abs(bdi - _extras.Settings.Costs.BdiPercent) > 1e-9)
        {
            _extras.Settings.Costs.BdiPercent = bdi;
            SettingsChanged = true;
        }

        if (!string.Equals(ReferenceBox.Text.Trim(), _extras.Settings.Costs.PriceReference, StringComparison.Ordinal))
        {
            _extras.Settings.Costs.PriceReference = ReferenceBox.Text.Trim();
            SettingsChanged = true;
        }

        RefreshBudget();
    }

    private void ImportTable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar tabela de preços",
            Filter = "Planilhas (*.xlsx;*.csv)|*.xlsx;*.csv|Pasta de trabalho do Excel (*.xlsx)|*.xlsx|Texto separado por ponto e vírgula (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        PriceTableReadResult result = PriceTableReader.Read(dialog.FileName);
        if (result.Table is null)
        {
            Warn("Não foi possível importar a tabela:\n" + result.Error
                 + "\n\nDica: no caso do SINAPI, abra a planilha de insumos ou de composições e salve como .xlsx; a primeira planilha do arquivo é a lida.");
            return;
        }

        try
        {
            PriceTableStore.Save(_extras.Paths, result.Table);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar a tabela de preços.", ex);
            Warn("A tabela foi lida, mas não pôde ser gravada na biblioteca:\n" + ex.Message);
        }

        _table = result.Table;
        RefreshBudget();
        Inform($"Tabela importada: {result.Table.Items.Count.ToString("N0", Num.PtBr)} itens."
               + (result.Warnings.Count > 0 ? "\n\n" + string.Join("\n", result.Warnings) : string.Empty)
               + "\n\nVincule os códigos às espécies com \"Vincular insumo\" e \"Vincular serviço\" (ou no cadastro da espécie).");
    }

    private void RemoveTable_Click(object sender, RoutedEventArgs e)
    {
        if (_table is null || !Confirm("Remover a tabela de preços importada?\n\nOs códigos vinculados às espécies são mantidos."))
        {
            return;
        }

        try
        {
            PriceTableStore.Delete(_extras.Paths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível apagar a tabela:\n" + ex.Message);
            return;
        }

        _table = null;
        RefreshBudget();
    }

    private void BudgetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateLinkButtons();

    private void UpdateLinkButtons()
    {
        bool can = _table is not null && _extras.LinkCode is not null && BudgetGrid.SelectedItem is BudgetRow { SpeciesId.Length: > 0 };
        LinkSupplyButton.IsEnabled = can;
        LinkServiceButton.IsEnabled = can;
        string reason = _table is null ? "Importe uma tabela de preços primeiro." : "Selecione uma linha de espécie cadastrada.";
        LinkSupplyButton.ToolTip = can ? "Procura na tabela o item de fornecimento da muda e grava o código no cadastro da espécie" : reason;
        LinkServiceButton.ToolTip = can ? "Procura na tabela a composição de plantio e grava o código no cadastro da espécie" : reason;
    }

    private void LinkSupply_Click(object sender, RoutedEventArgs e) => Link(false);

    private void LinkService_Click(object sender, RoutedEventArgs e) => Link(true);

    private void Link(bool service)
    {
        if (_table is null || _extras.LinkCode is null || BudgetGrid.SelectedItem is not BudgetRow { SpeciesId.Length: > 0 } row)
        {
            return;
        }

        Species? species = _extras.Lookup(row.SpeciesId);
        if (species is null)
        {
            return;
        }

        string header = (service ? "Serviço de plantio para " : "Fornecimento de ") + species.DisplayName;
        string search = service ? "plantio" : "muda " + species.PopularName.Split(' ', '-')[0];
        var window = new PriceSearchWindow(_table, header, search) { Owner = this };
        if (window.ShowDialog() != true || window.Selected is null)
        {
            return;
        }

        if (_extras.LinkCode(species.Id, window.Selected.Code, service))
        {
            CatalogChanged = true;
            RefreshBudget();
        }
    }

    // ------------------------------------------------------------------ manutenção

    private void RefreshMaintenance()
    {
        if (_loading)
        {
            return;
        }

        _maintenance = MaintenancePlanner.Build(_extras.MaintenanceItems, _extras.Settings.Maintenance);
        MaintenanceGrid.ItemsSource = _maintenance.Select(t => new MaintenanceRow(t)).ToList();
    }

    private void Maintenance_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        MaintenanceSettings m = _extras.Settings.Maintenance;
        int month = (PlantingMonthBox.SelectedItem as Choice<int>)?.Value ?? m.PlantingMonth;
        if (month != m.PlantingMonth)
        {
            m.PlantingMonth = month;
            SettingsChanged = true;
        }

        if (!MonthSet.Parse(DrySeasonBox.Text).IsEmpty && !string.Equals(DrySeasonBox.Text.Trim(), m.DrySeason, StringComparison.Ordinal))
        {
            m.DrySeason = DrySeasonBox.Text.Trim();
            SettingsChanged = true;
        }

        RefreshMaintenance();
    }

    // ------------------------------------------------------------------ ações

    private void Schedules_Click(object sender, RoutedEventArgs e)
    {
        Action = ReportsAction.CreateSchedules;
        DialogResult = true;
    }

    private void Legend_Click(object sender, RoutedEventArgs e)
    {
        Action = ReportsAction.CreateLegend;
        DialogResult = true;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        string baseName = SafeFile.SanitizeName(string.IsNullOrWhiteSpace(_projectTitle) ? "Projeto" : _projectTitle, 60);
        var dialog = new SaveFileDialog
        {
            Title = "Exportar quantitativos, orçamento e manutenção",
            Filter = "Pasta de trabalho do Excel (*.xlsx)|*.xlsx",
            FileName = $"Vegetação - {baseName} - {DateTime.Now:yyyy-MM-dd}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            XlsxWriter.Write(dialog.FileName, ReportSheets.Build(
                _projectTitle, _species, _massings, _acquisition, DateTime.Now, _budget, _maintenance, _extras.Settings.Costs.PriceReference));
            if (Confirm("Planilha exportada:\n" + dialog.FileName + "\n\nAbrir agora?"))
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ViridianLog.Error("Falha ao exportar planilha.", ex);
            Warn("Não foi possível gravar ou abrir a planilha (verifique se ela está aberta no Excel):\n" + ex.Message);
        }
    }
}
