using System.Windows;
using Viridian.Core.Config;
using Viridian.Core.Identification;
using Viridian.Core.Massing;

namespace Viridian.UI.Windows;

public sealed class LabelOptions
{
    /// <summary>Verdadeiro para etiquetar apenas os elementos selecionados; falso para toda a vista ativa.</summary>
    public bool SelectionOnly { get; set; }

    public bool Plants { get; set; }

    public bool Massings { get; set; }

    public LabelModel Model { get; set; } = LabelModel.CodigoSimples;

    public bool Leader { get; set; }

    public bool GroupBySpecies { get; set; }

    /// <summary>Identificador do tipo de tag nativa; zero para usar etiquetas de texto.</summary>
    public long NativeTagTypeId { get; set; }
}

/// <summary>Opções de identificação de exemplares e maciços.</summary>
public partial class LabelWindow : ViridianWindow
{
    private readonly int _selectedPlants;
    private readonly int _selectedMassings;
    private readonly int _viewPlants;
    private readonly int _viewMassings;
    private bool _loading = true;

    /// <param name="selectedPlants">Exemplares na seleção atual.</param>
    /// <param name="selectedMassings">Maciços na seleção atual.</param>
    /// <param name="viewPlants">Exemplares visíveis na vista ativa.</param>
    /// <param name="viewMassings">Maciços da vista ativa.</param>
    /// <param name="tagTypes">Tipos de identificador de vegetação carregados no projeto (nome e identificador).</param>
    /// <param name="settings">Configurações do escritório.</param>
    public LabelWindow(int selectedPlants, int selectedMassings, int viewPlants, int viewMassings, IReadOnlyList<(string Name, long Id)> tagTypes, ViridianSettings settings)
    {
        _selectedPlants = selectedPlants;
        _selectedMassings = selectedMassings;
        _viewPlants = viewPlants;
        _viewMassings = viewMassings;
        InitializeComponent();

        SelectionRadio.Content = $"Elementos selecionados ({selectedPlants} exemplar(es), {selectedMassings} maciço(s))";
        ViewRadio.Content = $"Toda a vista ativa ({viewPlants} exemplar(es), {viewMassings} maciço(s))";
        bool hasSelection = selectedPlants + selectedMassings > 0;
        SelectionRadio.IsEnabled = hasSelection;
        SelectionRadio.IsChecked = hasSelection;
        ViewRadio.IsChecked = !hasSelection;

        foreach (LabelModel model in LabelText.Models)
        {
            ModelBox.Items.Add(new Choice<LabelModel>(LabelText.DisplayName(model), model));
        }

        ModelBox.SelectedIndex = 0;
        LeaderBox.IsChecked = settings.Labels.LeaderByDefault;
        PlantsBox.IsChecked = true;
        MassingsBox.IsChecked = true;

        foreach ((string name, long id) in tagTypes)
        {
            TagTypeBox.Items.Add(new Choice<long>(name, id));
        }

        if (tagTypes.Count > 0)
        {
            TagTypeBox.SelectedIndex = 0;
            TagHint.Text = "O identificador nativo exibe os parâmetros definidos na família de tag (o Viridian grava o código da espécie em \"Marca de tipo\" e o do exemplar em \"Marca\").";
        }
        else
        {
            TagRadio.IsEnabled = false;
            TagTypeBox.IsEnabled = false;
            TagHint.Text = "Nenhuma família de identificador de vegetação carregada no projeto. Carregue uma (Inserir > Carregar família) para usar esta opção.";
        }

        _loading = false;
        Update();
    }

    public LabelOptions Options { get; } = new();

    private void Update()
    {
        if (_loading)
        {
            return;
        }

        bool selection = SelectionRadio.IsChecked == true;
        int plants = selection ? _selectedPlants : _viewPlants;
        int massings = selection ? _selectedMassings : _viewMassings;
        PlantsBox.IsEnabled = plants > 0;
        MassingsBox.IsEnabled = massings > 0;

        LabelModel model = (ModelBox.SelectedItem as Choice<LabelModel>)?.Value ?? LabelModel.CodigoSimples;
        bool grouped = GroupBox.IsChecked == true;
        var sample = new MassingRecord
        {
            Id = "MC-001", SpeciesCode = "FO-001", PopularName = "Grama-amendoim", ScientificName = "Arachis repens",
            AreaM2 = 45.3, Quantity = 725, SupplyUnit = "muda",
        };
        string plantExample = LabelText.ForPlants(model, "AR-001", "Ipê-amarelo", "Handroanthus chrysotrichus", grouped ? 5 : 1, "AR.0001");
        string massingExample = LabelText.ForMassing(model, sample);
        ExampleText.Text = "Exemplar: " + plantExample.Replace("\n", " / ", StringComparison.Ordinal)
                           + "\nMaciço: " + massingExample.Replace("\n", " / ", StringComparison.Ordinal);

        bool forced = LabelText.ForcesLeader(model);
        LeaderBox.IsEnabled = !forced;
        if (forced)
        {
            LeaderBox.IsChecked = true;
        }

        bool native = TagRadio.IsChecked == true;
        TagTypeBox.IsEnabled = native && TagTypeBox.Items.Count > 0;
        GroupBox.IsEnabled = !native;

        bool anyPlants = PlantsBox.IsEnabled && PlantsBox.IsChecked == true;
        bool anyMassings = MassingsBox.IsEnabled && MassingsBox.IsChecked == true;
        OkButton.IsEnabled = anyPlants || anyMassings;
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Update();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        bool native = TagRadio.IsChecked == true && TagTypeBox.SelectedItem is Choice<long>;
        Options.SelectionOnly = SelectionRadio.IsChecked == true;
        Options.Plants = PlantsBox.IsEnabled && PlantsBox.IsChecked == true;
        Options.Massings = MassingsBox.IsEnabled && MassingsBox.IsChecked == true;
        Options.Model = (ModelBox.SelectedItem as Choice<LabelModel>)?.Value ?? LabelModel.CodigoSimples;
        Options.Leader = LeaderBox.IsChecked == true;
        Options.GroupBySpecies = !native && GroupBox.IsChecked == true;
        Options.NativeTagTypeId = native ? ((Choice<long>)TagTypeBox.SelectedItem).Value : 0;
        DialogResult = true;
    }
}
