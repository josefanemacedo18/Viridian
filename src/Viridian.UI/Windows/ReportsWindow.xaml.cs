using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Viridian.Core.Export;
using Viridian.Core.Reports;
using Viridian.Library;

namespace Viridian.UI.Windows;

public enum ReportsAction
{
    None,
    CreateSchedules,
    CreateLegend,
}

/// <summary>Relatórios de quantitativos: por espécie, por maciço e de aquisição, com exportação para Excel.</summary>
public partial class ReportsWindow : ViridianWindow
{
    private readonly IReadOnlyList<SpeciesReportRow> _species;
    private readonly IReadOnlyList<MassingReportRow> _massings;
    private readonly IReadOnlyList<AcquisitionRow> _acquisition;
    private readonly string _projectTitle;

    public ReportsWindow(
        string projectTitle,
        IReadOnlyList<SpeciesReportRow> species,
        IReadOnlyList<MassingReportRow> massings,
        IReadOnlyList<AcquisitionRow> acquisition,
        int initialTab = 0)
    {
        _projectTitle = projectTitle;
        _species = species;
        _massings = massings;
        _acquisition = acquisition;
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

        SpeciesGrid.Language = Language;
        MassingGrid.Language = Language;
        AcquisitionGrid.Language = Language;
        switch (initialTab)
        {
            case 1:
                MassingTab.IsSelected = true;
                break;
            case 2:
                AcquisitionTab.IsSelected = true;
                break;
            default:
                SpeciesTab.IsSelected = true;
                break;
        }
    }

    public ReportsAction Action { get; private set; } = ReportsAction.None;

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
            Title = "Exportar quantitativos",
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
            XlsxWriter.Write(dialog.FileName, ReportSheets.Build(_projectTitle, _species, _massings, _acquisition, DateTime.Now));
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
