using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Config;
using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;

namespace Viridian.UI.Windows;

public enum ScaleAction
{
    None,
    ApplyView,
    ApplyAll,
}

/// <summary>Gerenciador de escalas: perfil da vista ativa, perfil fixado manualmente e aplicação em lote.</summary>
public partial class ScaleWindow : ViridianWindow
{
    private readonly ViridianSettings _settings;
    private readonly int _scale;
    private bool _loading = true;

    /// <param name="viewName">Nome da vista ativa (vazio quando não é uma planta).</param>
    /// <param name="scale">Denominador da escala da vista.</param>
    /// <param name="currentOverride">Perfil fixado na vista, se houver.</param>
    /// <param name="controlledByTemplate">A visibilidade da vista é controlada por um modelo de vista.</param>
    /// <param name="settings">Configurações do escritório.</param>
    public ScaleWindow(string viewName, int scale, DetailProfile? currentOverride, bool controlledByTemplate, ViridianSettings settings)
    {
        _settings = settings;
        _scale = scale;
        InitializeComponent();

        bool hasView = !string.IsNullOrWhiteSpace(viewName);
        ViewText.Text = hasView ? $"{viewName}   ·   escala 1:{scale}" : "A vista ativa não é uma vista em planta.";
        OverrideBox.Items.Add(new Choice<DetailProfile?>("Automático (pela escala)", null));
        foreach (DetailProfile profile in Enum.GetValues<DetailProfile>())
        {
            OverrideBox.Items.Add(new Choice<DetailProfile?>("Fixar: " + ScaleProfileSet.DisplayName(profile), profile));
        }

        OverrideBox.SelectedIndex = currentOverride.HasValue ? (int)currentOverride.Value + 1 : 0;
        OverrideBox.IsEnabled = hasView;
        TemplateHint.Text = controlledByTemplate
            ? "A visibilidade desta vista é controlada por um modelo de vista: o perfil é aplicado ao modelo, que vale para todas as vistas que o usam."
            : string.Empty;

        var ranges = new List<object>();
        int previous = 0;
        foreach (ScaleProfileRange range in settings.Scales.Ranges.OrderBy(r => r.MaxDenominator))
        {
            ranges.Add(new
            {
                Range = previous == 0 ? $"até 1:{range.MaxDenominator}" : $"1:{previous + 1} a 1:{range.MaxDenominator}",
                Profile = ScaleProfileSet.DisplayName(range.Profile),
                Description = ScaleProfileSet.Description(range.Profile),
            });
            previous = range.MaxDenominator;
        }

        ranges.Add(new
        {
            Range = $"acima de 1:{previous}",
            Profile = ScaleProfileSet.DisplayName(DetailProfile.Minimo),
            Description = ScaleProfileSet.Description(DetailProfile.Minimo),
        });
        RangesList.ItemsSource = ranges;
        AutoBox.IsChecked = settings.AutoApplyScaleProfiles;

        _loading = false;
        UpdatePreview();
    }

    public ScaleAction Action { get; private set; } = ScaleAction.None;

    /// <summary>Perfil fixado escolhido (nulo = automático).</summary>
    public DetailProfile? Override => (OverrideBox.SelectedItem as Choice<DetailProfile?>)?.Value;

    public bool AutoApply => AutoBox.IsChecked == true;

    private void UpdatePreview()
    {
        if (_loading)
        {
            return;
        }

        DetailProfile profile = Override ?? _settings.Scales.Resolve(_scale);
        ProfileText.Text = $"Perfil exibido: {ScaleProfileSet.DisplayName(profile)} — {ScaleProfileSet.Description(profile)}"
                           + (Override.HasValue ? " (fixado manualmente)" : " (definido pela escala)");
        // Exemplo: árvore com floração, para mostrar também as manchas de flor.
        var sample = new Species { Category = PlantCategory.Arvore, SymbolStyle = SymbolStyle.ArvoreArredondada, CrownDiameterM = 8, SymbolSeed = 20260417, FlowerColor = "#E07BB5" };
        SymbolView.Show(SymbolGenerator.Generate(SymbolStyle.ArvoreArredondada, 8, 20260417), profile, SymbolPaint.For(sample, _settings.Colors), _settings);
    }

    private void OverrideBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void ApplyView_Click(object sender, RoutedEventArgs e)
    {
        Action = ScaleAction.ApplyView;
        DialogResult = true;
    }

    private void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        Action = ScaleAction.ApplyAll;
        DialogResult = true;
    }
}
