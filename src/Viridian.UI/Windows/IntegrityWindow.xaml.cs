using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Viridian.Core.Integrity;

namespace Viridian.UI.Windows;

public enum IntegrityAction
{
    None,
    Recheck,
    FixCodes,
    FixData,
    FixOrphans,
}

/// <summary>Lista de inconsistências do projeto, com localização dos elementos e correções automáticas.</summary>
public partial class IntegrityWindow : ViridianWindow
{
    private readonly IReadOnlyList<IntegrityIssue> _issues;
    private readonly Action<IReadOnlyList<long>> _showElements;

    /// <param name="issues">Ocorrências encontradas.</param>
    /// <param name="showElements">Seleciona e mostra elementos no Revit.</param>
    public IntegrityWindow(IReadOnlyList<IntegrityIssue> issues, Action<IReadOnlyList<long>> showElements)
    {
        _issues = issues;
        _showElements = showElements;
        InitializeComponent();

        int errors = issues.Count(i => i.Severity == IssueSeverity.Erro);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Aviso);
        int infos = issues.Count(i => i.Severity == IssueSeverity.Info);
        SummaryText.Text = issues.Count == 0
            ? "Nenhuma inconsistência encontrada."
            : $"{issues.Count} ocorrência(s): {errors} erro(s), {warnings} aviso(s), {infos} informação(ões).";

        FilterBox.Items.Add(new Choice<IssueKind?>("Todas as verificações", null));
        foreach (IssueKind kind in issues.Select(i => i.Kind).Distinct().OrderBy(k => k))
        {
            FilterBox.Items.Add(new Choice<IssueKind?>($"{IntegrityChecker.DisplayName(kind)} ({issues.Count(i => i.Kind == kind)})", kind));
        }

        FilterBox.SelectedIndex = 0;
        FixCodesButton.IsEnabled = issues.Any(i => i.Kind is IssueKind.SemCodigo or IssueKind.CodigoDuplicado);
        FixDataButton.IsEnabled = issues.Any(i => i.Kind is IssueKind.DadosDesatualizados or IssueKind.EtiquetaOrfa);
        FixOrphansButton.IsEnabled = issues.Any(i => i.Kind is IssueKind.RegistroOrfao or IssueKind.EtiquetaOrfa);
        UpdateButtons();
    }

    public IntegrityAction Action { get; private set; } = IntegrityAction.None;

    private IntegrityIssue? Selected => IssuesGrid.SelectedItem as IntegrityIssue;

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        IssueKind? kind = (FilterBox.SelectedItem as Choice<IssueKind?>)?.Value;
        IssuesGrid.ItemsSource = kind is null ? _issues : _issues.Where(i => i.Kind == kind).ToList();
        UpdateButtons();
    }

    private void IssuesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons() => ShowButton.IsEnabled = Selected is { ElementCount: > 0 };

    private void ShowSelected()
    {
        if (Selected is { ElementCount: > 0 } issue)
        {
            _showElements(issue.ElementIds);
        }
    }

    private void IssuesGrid_DoubleClick(object sender, MouseButtonEventArgs e) => ShowSelected();

    private void Show_Click(object sender, RoutedEventArgs e) => ShowSelected();

    private void Finish(IntegrityAction action)
    {
        Action = action;
        DialogResult = true;
    }

    private void FixCodes_Click(object sender, RoutedEventArgs e) => Finish(IntegrityAction.FixCodes);

    private void FixData_Click(object sender, RoutedEventArgs e) => Finish(IntegrityAction.FixData);

    private void FixOrphans_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm("Limpar registros órfãos?\n\n• Exemplares que apontam para um maciço inexistente deixam de pertencer a ele (passam a ser exemplares isolados).\n• Etiquetas cujo elemento foi excluído são removidas."))
        {
            Finish(IntegrityAction.FixOrphans);
        }
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => Finish(IntegrityAction.Recheck);
}
