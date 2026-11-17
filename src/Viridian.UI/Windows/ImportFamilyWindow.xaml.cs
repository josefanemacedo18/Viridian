using System.IO;
using System.Windows;
using Viridian.Core.Model;

namespace Viridian.UI.Windows;

/// <summary>Resumo da verificação de uma família, independente da API do Revit.</summary>
public sealed class FamilyCheckInfo
{
    public string FilePath { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public bool IsPlanting { get; init; }

    public bool IsDetailItem { get; init; }

    public IReadOnlyList<string> TypeNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Checks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string Error { get; init; } = string.Empty;

    public bool IsUsable => string.IsNullOrEmpty(Error) && (IsPlanting || IsDetailItem);
}

/// <summary>Assistente "Importar símbolo personalizado": mostra a verificação e associa a família a uma espécie.</summary>
public partial class ImportFamilyWindow : ViridianWindow
{
    private readonly Func<Window, Species?> _chooseSpecies;

    /// <param name="info">Resultado da verificação da família.</param>
    /// <param name="initialSpecies">Espécie sugerida (pode ser nula).</param>
    /// <param name="chooseSpecies">Abre a biblioteca, sobre a janela informada, para escolher a espécie.</param>
    public ImportFamilyWindow(FamilyCheckInfo info, Species? initialSpecies, Func<Window, Species?> chooseSpecies)
    {
        Info = info;
        _chooseSpecies = chooseSpecies;
        SelectedSpecies = initialSpecies;
        InitializeComponent();

        FileText.Text = Path.GetFileName(info.FilePath);
        CategoryText.Text = "Categoria: " + (string.IsNullOrWhiteSpace(info.CategoryName) ? "—" : info.CategoryName);
        ChecksList.ItemsSource = info.Checks.Select(c => "✓ " + c).ToList();
        WarningsList.ItemsSource = info.Warnings.Select(w => "! " + w).ToList();
        ErrorText.Text = info.Error;
        ErrorText.Visibility = string.IsNullOrEmpty(info.Error) ? Visibility.Collapsed : Visibility.Visible;

        UsageText.Text = info.IsPlanting
            ? "Família de Vegetação: exemplares da espécie (quantitativos, códigos e etiquetas)."
            : info.IsDetailItem ? "Componente de detalhe: símbolo gráfico de maciços (a quantidade fica no registro do maciço)." : "—";

        foreach (string type in info.TypeNames)
        {
            TypeBox.Items.Add(type);
        }

        if (TypeBox.Items.Count > 0)
        {
            TypeBox.SelectedIndex = 0;
        }

        AssociationGroup.IsEnabled = info.IsUsable;
        UpdateSpecies();
    }

    public FamilyCheckInfo Info { get; }

    public Species? SelectedSpecies { get; private set; }

    public string SelectedTypeName => TypeBox.SelectedItem as string ?? string.Empty;

    private void UpdateSpecies()
    {
        SpeciesText.Text = SelectedSpecies is null ? "(escolha a espécie)" : SelectedSpecies.DisplayName + " — " + SelectedSpecies.ScientificName;
        OkButton.IsEnabled = Info.IsUsable && SelectedSpecies is not null;
    }

    private void ChooseSpecies_Click(object sender, RoutedEventArgs e)
    {
        Species? species = _chooseSpecies(this);
        if (species is not null)
        {
            SelectedSpecies = species;
            UpdateSpecies();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Info.IsUsable || SelectedSpecies is null)
        {
            return;
        }

        string current = Info.IsPlanting ? SelectedSpecies.CustomPlantingFamilyFile : SelectedSpecies.CustomDetailFamilyFile;
        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(Path.GetFileName(current), Path.GetFileName(Info.FilePath), StringComparison.OrdinalIgnoreCase)
            && !Confirm($"A espécie já possui a família \"{Path.GetFileName(current)}\". Substituir?"))
        {
            return;
        }

        DialogResult = true;
    }
}
