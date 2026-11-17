using System.Windows;
using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Model;

namespace Viridian.UI.Windows;

public enum CodingAction
{
    Instances,
    RenumberSpecies,
    RestoreSpecies,
}

public enum CodingScope
{
    Selection,
    View,
    Project,
}

public enum SpeciesOrder
{
    ScientificName,
    PopularName,
    CatalogCode,
}

public sealed class CodingOptions
{
    public CodingAction Action { get; set; }

    public CodingScope Scope { get; set; } = CodingScope.Project;

    public bool Overwrite { get; set; }

    public string Template { get; set; } = InstanceCodeFormat.DefaultTemplate;

    public int Digits { get; set; } = 4;

    public SpeciesOrder Order { get; set; }
}

/// <summary>Codificação de exemplares e renumeração dos códigos de espécie no projeto.</summary>
public partial class CodingWindow : ViridianWindow
{
    private bool _loading = true;

    public CodingWindow(int selectedCount, int viewCount, int projectCount, ViridianSettings settings)
    {
        InitializeComponent();
        ScopeSelection.Content = $"Exemplares selecionados ({selectedCount})";
        ScopeView.Content = $"Exemplares da vista ativa ({viewCount})";
        ScopeProject.Content = $"Todos os exemplares do projeto ({projectCount})";
        ScopeSelection.IsEnabled = selectedCount > 0;
        ScopeView.IsEnabled = viewCount > 0;
        if (selectedCount > 0)
        {
            ScopeSelection.IsChecked = true;
        }
        else
        {
            ScopeProject.IsChecked = true;
        }

        TemplateBox.Text = settings.InstanceCode.Template;
        for (int i = 1; i <= 8; i++)
        {
            DigitsBox.Items.Add(i);
        }

        DigitsBox.SelectedItem = Math.Clamp(settings.InstanceCode.Digits, 1, 8);
        OrderBox.Items.Add(new Choice<SpeciesOrder>("Nome científico", SpeciesOrder.ScientificName));
        OrderBox.Items.Add(new Choice<SpeciesOrder>("Nome popular", SpeciesOrder.PopularName));
        OrderBox.Items.Add(new Choice<SpeciesOrder>("Código do catálogo", SpeciesOrder.CatalogCode));
        OrderBox.SelectedIndex = 0;

        _loading = false;
        Update();
    }

    public CodingOptions Options { get; } = new();

    private InstanceCodeFormat CurrentFormat() => new()
    {
        Template = TemplateBox.Text.Trim(),
        Digits = DigitsBox.SelectedItem is int digits ? digits : 4,
    };

    private void Update()
    {
        if (_loading)
        {
            return;
        }

        bool instances = InstancesRadio.IsChecked == true;
        InstancesPanel.IsEnabled = instances;
        RenumberPanel.IsEnabled = RenumberRadio.IsChecked == true;
        OkButton.Content = instances ? "Codificar" : "Aplicar";

        InstanceCodeFormat format = CurrentFormat();
        if (format.IsValid)
        {
            ExampleText.Text = "Exemplo: " + format.Format(PlantCategory.Arvore, "AR-001", 12);
            OkButton.IsEnabled = true;
        }
        else
        {
            ExampleText.Text = "O formato precisa conter o marcador {N}.";
            OkButton.IsEnabled = !instances;
        }
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Update();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Options.Action = InstancesRadio.IsChecked == true
            ? CodingAction.Instances
            : RenumberRadio.IsChecked == true ? CodingAction.RenumberSpecies : CodingAction.RestoreSpecies;
        Options.Scope = ScopeSelection.IsChecked == true
            ? CodingScope.Selection
            : ScopeView.IsChecked == true ? CodingScope.View : CodingScope.Project;
        Options.Overwrite = OverwriteBox.IsChecked == true;
        InstanceCodeFormat format = CurrentFormat();
        format.Normalize();
        Options.Template = format.Template;
        Options.Digits = format.Digits;
        Options.Order = (OrderBox.SelectedItem as Choice<SpeciesOrder>)?.Value ?? SpeciesOrder.ScientificName;
        DialogResult = true;
    }
}
