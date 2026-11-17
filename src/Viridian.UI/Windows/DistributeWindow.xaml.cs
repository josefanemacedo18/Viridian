using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.UI.Windows;

/// <summary>Resultado da janela de distribuição.</summary>
public sealed class DistributeOutcome
{
    public IReadOnlyList<Point2> Points { get; set; } = Array.Empty<Point2>();

    public bool RandomRotation { get; set; }

    public long Seed { get; set; }

    public PlantOptions Plants { get; set; } = new();
}

/// <summary>Distribuição automática de exemplares em uma região ou ao longo de um eixo, com pré-visualização.</summary>
public partial class DistributeWindow : ViridianWindow
{
    private readonly Species _species;
    private readonly Polygon2? _region;
    private readonly IReadOnlyList<Point2> _path;
    private readonly IReadOnlyList<Point2> _existing;
    private readonly bool _linear;
    private bool _loading = true;
    private long _seed = 1;
    private DistributionResult _result = new();

    /// <param name="species">Espécie a distribuir.</param>
    /// <param name="region">Região de plantio (nula no plantio linear).</param>
    /// <param name="path">Eixo do plantio linear (vazio quando há região).</param>
    /// <param name="existingPlants">Posições dos exemplares já existentes no nível da vista.</param>
    /// <param name="settings">Configurações do escritório.</param>
    /// <param name="defaultStage">Estágio de implantação sugerido.</param>
    public DistributeWindow(Species species, Polygon2? region, IReadOnlyList<Point2>? path, IReadOnlyList<Point2> existingPlants, ViridianSettings settings, string defaultStage)
    {
        _species = species;
        _region = region;
        _path = path ?? Array.Empty<Point2>();
        _existing = existingPlants;
        _linear = _path.Count >= 2;
        InitializeComponent();
        HeaderText.Text = (_linear ? "Plantio linear — " : "Distribuir — ") + species.DisplayName;

        if (_linear)
        {
            PatternBox.Items.Add(new Choice<DistributionPattern>("Linear (ao longo do eixo)", DistributionPattern.Linear));
            RowSpacingLabel.Text = "Deslocamento lateral (m)";
            RowSpacingBox.ToolTip = "Distância do eixo; positivo = à esquerda do sentido do traçado";
            foreach (UIElement element in new UIElement[] { DensityLabel, DensityBox, JitterLabel, JitterBox, AngleLabel, AngleBox, ReseedButton, MarginLabel, MarginBox })
            {
                element.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            PatternBox.Items.Add(new Choice<DistributionPattern>("Triangular", DistributionPattern.Triangular));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Retangular", DistributionPattern.Retangular));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Natural (agrupamento livre)", DistributionPattern.Natural));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Por densidade (un/m²)", DistributionPattern.Densidade));
        }

        PatternBox.SelectedIndex = 0;
        SpacingBox.Text = Num.Format(species.EffectiveSpacingM);
        DensityBox.Text = species.DensityPerM2 > 0 ? Num.Format(species.DensityPerM2) : "1";
        MarginBox.Text = Num.Format(settings.Planting.EdgeMarginM);
        JitterBox.Text = Num.Format(settings.Planting.JitterPercent);
        AngleBox.Text = "0";
        MaxBox.Text = settings.Planting.MaxCount.ToString(Num.PtBr);
        RotationBox.IsChecked = settings.Planting.RandomRotation;
        AvoidBox.IsEnabled = existingPlants.Count > 0;

        VariantBox.Items.Add(new Choice<int>("Aleatória", -1));
        for (int i = 0; i < species.EffectiveVariants; i++)
        {
            VariantBox.Items.Add(new Choice<int>("Variante " + (i + 1), i));
        }

        VariantBox.SelectedIndex = 0;
        VariantBox.IsEnabled = !species.HasCustomPlantingFamily && species.EffectiveVariants > 1;
        foreach (PlantStatus status in Enum.GetValues<PlantStatus>())
        {
            StatusBox.Items.Add(new Choice<PlantStatus>(Labels.Of(status), status));
        }

        StatusBox.SelectedIndex = 0;
        StageBox.Text = defaultStage;

        _loading = false;
        Recalculate();
    }

    public DistributeOutcome Outcome { get; } = new();

    private DistributionPattern Pattern => (PatternBox.SelectedItem as Choice<DistributionPattern>)?.Value ?? DistributionPattern.Triangular;

    private DistributionRequest BuildRequest(out string problem)
    {
        problem = string.Empty;
        DistributionPattern pattern = Pattern;
        var request = new DistributionRequest { Pattern = pattern, Seed = _seed, Path = _path };

        if (pattern == DistributionPattern.Densidade)
        {
            if (!Num.TryParse(DensityBox.Text, out double density) || density <= 0)
            {
                problem = "Informe a densidade em unidades por m².";
            }

            request.DensityPerM2 = density;
        }
        else
        {
            if (!Num.TryParse(SpacingBox.Text, out double spacing) || spacing <= 0)
            {
                problem = "Informe o espaçamento em metros.";
            }

            request.SpacingM = spacing;
        }

        double second = Num.ParseOr(RowSpacingBox.Text, 0);
        if (_linear)
        {
            request.PathOffsetM = second;
        }
        else
        {
            request.RowSpacingM = Math.Max(0, second);
            request.EdgeMarginM = Math.Max(0, Num.ParseOr(MarginBox.Text, 0));
            request.JitterPercent = Math.Clamp(Num.ParseOr(JitterBox.Text, 0), 0, 50);
            request.AngleDeg = Num.ParseOr(AngleBox.Text, 0);
        }

        request.MaxCount = Num.TryParseInt(MaxBox.Text, out int max) && max > 0 ? max : 0;

        if (AvoidBox.IsChecked == true && _existing.Count > 0)
        {
            double clearance = pattern == DistributionPattern.Densidade ? PlantDistributor.EffectiveSpacing(request) : request.SpacingM;
            if (clearance > 0)
            {
                request.Obstacles = _existing.Select(p => new Obstacle(p, clearance * 0.999)).ToList();
            }
        }

        return request;
    }

    private void Recalculate()
    {
        if (_loading)
        {
            return;
        }

        DistributionPattern pattern = Pattern;
        bool byDensity = pattern == DistributionPattern.Densidade;
        SpacingBox.IsEnabled = !byDensity;
        DensityBox.IsEnabled = byDensity;
        RowSpacingBox.IsEnabled = _linear || pattern is DistributionPattern.Retangular or DistributionPattern.Triangular;
        JitterBox.IsEnabled = pattern != DistributionPattern.Natural;
        AngleBox.IsEnabled = pattern != DistributionPattern.Natural;
        ReseedButton.IsEnabled = pattern == DistributionPattern.Natural || Num.ParseOr(JitterBox.Text, 0) > 0;

        DistributionRequest request = BuildRequest(out string problem);
        _result = problem.Length > 0 ? new DistributionResult { Message = problem } : PlantDistributor.Distribute(_region, request);

        Preview.Show(_region, _result.Points, _species.CrownDiameterM, false, _path);
        string area = _region is not null && !_region.IsEmpty ? $"Área: {Num.Format(_region.Area, "N2")} m²   ·   " : string.Empty;
        string length = _linear ? $"Eixo: {Num.Format(Geo.PolylineLength(_path), "N2")} m   ·   " : string.Empty;
        SummaryText.Text = area + length + (_result.Exceeded
            ? $"Calculados: {_result.ComputedCount.ToString("N0", Num.PtBr)} exemplares"
            : $"Exemplares: {_result.Points.Count.ToString("N0", Num.PtBr)}");
        MessageText.Text = _result.Message;
        OkButton.IsEnabled = _result.HasPoints && !_result.Exceeded;
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Recalculate();

    private void Reseed_Click(object sender, RoutedEventArgs e)
    {
        _seed = Random.Shared.NextInt64(1, long.MaxValue);
        Recalculate();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_result.HasPoints || _result.Exceeded)
        {
            return;
        }

        Outcome.Points = _result.Points;
        Outcome.RandomRotation = RotationBox.IsChecked == true;
        Outcome.Seed = _seed;
        Outcome.Plants = new PlantOptions
        {
            VariantIndex = (VariantBox.SelectedItem as Choice<int>)?.Value ?? -1,
            Status = (StatusBox.SelectedItem as Choice<PlantStatus>)?.Value ?? PlantStatus.Novo,
            Stage = StageBox.Text.Trim(),
        };
        DialogResult = true;
    }
}
