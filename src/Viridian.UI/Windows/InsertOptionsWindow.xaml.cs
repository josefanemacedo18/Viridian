using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

namespace Viridian.UI.Windows;

/// <summary>Dados escolhidos para os exemplares a inserir ou editar.</summary>
public sealed class PlantOptions
{
    /// <summary>Índice da variante gráfica; -1 = sortear entre as variantes (ou manter, na edição).</summary>
    public int VariantIndex { get; set; } = -1;

    public PlantStatus Status { get; set; } = PlantStatus.Novo;

    public string Stage { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>Na edição: indica quais campos devem ser alterados.</summary>
    public bool ChangeStatus { get; set; } = true;

    public bool ChangeStage { get; set; } = true;

    public bool ChangeNotes { get; set; } = true;
}

/// <summary>Opções de inserção de exemplares individuais (também usada para editar exemplares existentes).</summary>
public partial class InsertOptionsWindow : ViridianWindow
{
    private readonly Species _species;
    private readonly bool _editing;
    private readonly ViridianSettings? _settings;

    /// <param name="species">Espécie dos exemplares.</param>
    /// <param name="defaultStage">Texto sugerido para o estágio de implantação.</param>
    /// <param name="editingCount">Quantidade de exemplares em edição; zero quando a janela é usada para inserir.</param>
    /// <param name="current">Valores atuais (edição).</param>
    /// <param name="settings">Configurações (cores da pré-visualização).</param>
    public InsertOptionsWindow(Species species, string defaultStage, int editingCount = 0, PlantOptions? current = null, ViridianSettings? settings = null)
    {
        _species = species;
        _settings = settings;
        _editing = editingCount > 0;
        InitializeComponent();

        HeaderText.Text = _editing
            ? $"Editar {editingCount} exemplar(es) — {species.DisplayName}"
            : "Inserir — " + species.DisplayName;
        if (_editing)
        {
            Title = "Viridian — Editar exemplares";
            OkButton.Content = "Aplicar";
            HintText.Text = "Os campos deixados como \"(manter)\" ou vazios não são alterados.";
        }

        VariantBox.Items.Add(new Choice<int>(_editing ? "(manter)" : "Aleatória", -1));
        for (int i = 0; i < species.EffectiveVariants; i++)
        {
            VariantBox.Items.Add(new Choice<int>("Variante " + (i + 1), i));
        }

        VariantBox.SelectedIndex = 0;
        VariantBox.IsEnabled = !species.HasCustomPlantingFamily && species.EffectiveVariants > 1;

        if (_editing)
        {
            StatusBox.Items.Add(new Choice<PlantStatus?>("(manter)", null));
        }

        foreach (PlantStatus status in Enum.GetValues<PlantStatus>())
        {
            StatusBox.Items.Add(new Choice<PlantStatus?>(Labels.Of(status), status));
        }

        StatusBox.SelectedIndex = 0;
        if (!_editing)
        {
            StageBox.Text = defaultStage;
        }
        else if (current is not null)
        {
            StageBox.Text = current.Stage;
            NotesBox.Text = current.Notes;
        }

        ShowSymbol();
    }

    public PlantOptions Options { get; } = new();

    private void ShowSymbol()
    {
        int variant = Math.Max(0, (VariantBox.SelectedItem as Choice<int>)?.Value ?? 0);
        SymbolView.Show(
            SymbolGenerator.Generate(_species.SymbolStyle, _species.CrownDiameterM, _species.EffectiveSymbolSeed, variant),
            DetailProfile.Detalhado,
            SymbolPaint.For(_species, (_settings ?? new ViridianSettings()).Colors),
            _settings);
    }

    private void VariantBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && SymbolView is not null)
        {
            ShowSymbol();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Options.VariantIndex = (VariantBox.SelectedItem as Choice<int>)?.Value ?? -1;
        PlantStatus? status = (StatusBox.SelectedItem as Choice<PlantStatus?>)?.Value;
        Options.ChangeStatus = status.HasValue;
        Options.Status = status ?? PlantStatus.Novo;
        Options.Stage = StageBox.Text.Trim();
        Options.Notes = NotesBox.Text.Trim();
        Options.ChangeStage = !_editing || Options.Stage.Length > 0;
        Options.ChangeNotes = !_editing || Options.Notes.Length > 0;
        DialogResult = true;
    }
}
