using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Colors;
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

/// <summary>
/// Distribuição automática de exemplares em uma região (malhas, natural, densidade, bordadura, anéis,
/// agrupamentos, degradê) ou ao longo de eixos (plantio linear), com pré-visualização em tempo real.
/// </summary>
public partial class DistributeWindow : ViridianWindow
{
    private static readonly IReadOnlyDictionary<DistributionPattern, string> Hints = new Dictionary<DistributionPattern, string>
    {
        [DistributionPattern.Linear] = "Plantas sobre o eixo, em uma ou mais fileiras paralelas (alamedas, cercas vivas, renques).",
        [DistributionPattern.Triangular] = "Malha em quincôncio: cada planta fica no vértice de um triângulo. Cobre o canteiro por igual.",
        [DistributionPattern.Retangular] = "Malha ortogonal em linhas e colunas, para plantios formais e pomares.",
        [DistributionPattern.Natural] = "Disposição livre, sem alinhamentos, respeitando o espaçamento mínimo.",
        [DistributionPattern.Densidade] = "Quantidade por metro quadrado, típica de forrações e herbáceas.",
        [DistributionPattern.Bordadura] = "Fileiras que acompanham o contorno do canteiro, inclusive as curvas e os recortes.",
        [DistributionPattern.Circular] = "Anéis concêntricos a partir do centro do canteiro.",
        [DistributionPattern.Agrupamentos] = "Manchas naturalistas com 3, 5 ou 7 plantas, separadas por clareiras.",
        [DistributionPattern.Gradiente] = "Disposição natural que vai rareando de um lado para o outro (degradê).",
    };

    private readonly Species _species;
    private readonly Polygon2? _region;
    private readonly IReadOnlyList<PlantingPath> _paths;
    private readonly IReadOnlyList<Point2> _existing;
    private readonly ViridianSettings _settings;
    private readonly bool _linear;
    private bool _loading = true;
    private long _seed = 1;
    private DistributionResult _result = new();

    /// <param name="species">Espécie a distribuir.</param>
    /// <param name="region">Região de plantio (nula no plantio linear).</param>
    /// <param name="paths">Eixos do plantio linear (vazio quando há região).</param>
    /// <param name="existingPlants">Posições dos exemplares já existentes no nível da vista.</param>
    /// <param name="settings">Configurações do escritório.</param>
    /// <param name="defaultStage">Estágio de implantação sugerido.</param>
    /// <param name="source">Descrição de onde veio a região ou o eixo (exibida no cabeçalho).</param>
    public DistributeWindow(
        Species species,
        Polygon2? region,
        IReadOnlyList<PlantingPath>? paths,
        IReadOnlyList<Point2> existingPlants,
        ViridianSettings settings,
        string defaultStage,
        string source = "")
    {
        _species = species;
        _region = region;
        _paths = (paths ?? Array.Empty<PlantingPath>()).Where(p => p.Points.Count >= 2).ToList();
        _existing = existingPlants;
        _settings = settings;
        _linear = _paths.Count > 0;
        InitializeComponent();
        HeaderText.Text = (_linear ? "Plantio linear — " : "Distribuir — ") + species.DisplayName;
        SourceText.Text = source.Length > 0 ? (_linear ? "Eixo: " : "Região: ") + source : string.Empty;

        if (_linear)
        {
            PatternBox.Items.Add(new Choice<DistributionPattern>("Ao longo do eixo", DistributionPattern.Linear));
            PatternBox.IsEnabled = false;
        }
        else
        {
            PatternBox.Items.Add(new Choice<DistributionPattern>("Triangular (quincôncio)", DistributionPattern.Triangular));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Retangular", DistributionPattern.Retangular));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Natural (disposição livre)", DistributionPattern.Natural));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Por densidade (un/m²)", DistributionPattern.Densidade));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Bordadura (acompanha o contorno)", DistributionPattern.Bordadura));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Anéis concêntricos", DistributionPattern.Circular));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Agrupamentos (manchas ímpares)", DistributionPattern.Agrupamentos));
            PatternBox.Items.Add(new Choice<DistributionPattern>("Degradê (rareando)", DistributionPattern.Gradiente));
        }

        LinearModeBox.Items.Add(new Choice<LinearMode>("Espaçamento", LinearMode.Espacamento));
        LinearModeBox.Items.Add(new Choice<LinearMode>("Quantidade", LinearMode.Quantidade));
        AlignmentBox.Items.Add(new Choice<LinearAlignment>("Plantar nas duas pontas (ajusta o espaçamento)", LinearAlignment.Ajustado));
        AlignmentBox.Items.Add(new Choice<LinearAlignment>("Começar no início do eixo", LinearAlignment.Inicio));
        AlignmentBox.Items.Add(new Choice<LinearAlignment>("Centralizar (sobra igual nas pontas)", LinearAlignment.Centralizado));
        ArrangementBox.Items.Add(new Choice<RowArrangement>("Paralelas (alinhadas)", RowArrangement.Paralelas));
        ArrangementBox.Items.Add(new Choice<RowArrangement>("Alternadas (zigue-zague)", RowArrangement.Alternadas));
        int maxRows = _linear ? 5 : 8;
        for (int i = 1; i <= maxRows; i++)
        {
            RowsBox.Items.Add(i);
        }

        double spacing = species.EffectiveSpacingM;
        PatternBox.SelectedIndex = 0;
        LinearModeBox.SelectedIndex = 0;
        AlignmentBox.SelectedIndex = 0;
        ArrangementBox.SelectedIndex = 0;
        RowsBox.SelectedIndex = 0;
        SpacingBox.Text = Num.Format(spacing);
        double length = _paths.Sum(p => p.Length);
        CountBox.Text = Math.Max(2, (int)Math.Round(length / Math.Max(spacing, 0.1)) + 1).ToString(Num.PtBr);
        DensityBox.Text = species.DensityPerM2 > 0 ? Num.Format(species.DensityPerM2) : "1";
        MarginBox.Text = Num.Format(settings.Planting.EdgeMarginM);
        JitterBox.Text = Num.Format(settings.Planting.JitterPercent);
        AngleBox.Text = "0";
        GroupMinBox.Text = "3";
        GroupMaxBox.Text = "7";
        GradientEndBox.Text = "15";
        MaxBox.Text = settings.Planting.MaxCount.ToString(Num.PtBr);
        RotationBox.IsChecked = settings.Planting.RandomRotation;
        AvoidBox.IsEnabled = existingPlants.Count > 0;

        bool singlePath = _paths.Count == 1;
        ClosedBox.IsChecked = singlePath && _paths[0].Closed;
        ClosedBox.IsEnabled = singlePath && !_paths[0].Closed && _paths[0].Points.Count >= 3;

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
        Loaded += (_, _) => (_linear ? SpacingBox : (Control)PatternBox).Focus();
    }

    public DistributeOutcome Outcome { get; } = new();

    private DistributionPattern Pattern => (PatternBox.SelectedItem as Choice<DistributionPattern>)?.Value ?? DistributionPattern.Triangular;

    private LinearMode Mode => (LinearModeBox.SelectedItem as Choice<LinearMode>)?.Value ?? LinearMode.Espacamento;

    private int Rows => RowsBox.SelectedItem is int rows ? rows : 1;

    private bool IsClosed => _paths.Count == 1 ? ClosedBox.IsChecked == true : _paths.All(p => p.Closed);

    private static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Mostra somente as opções que valem para o padrão escolhido.</summary>
    private void UpdateFields()
    {
        DistributionPattern p = Pattern;
        bool linear = p == DistributionPattern.Linear;
        bool byCount = linear && Mode == LinearMode.Quantidade;
        bool lattice = p is DistributionPattern.Triangular or DistributionPattern.Retangular or DistributionPattern.Densidade;
        bool rowed = linear || p == DistributionPattern.Bordadura;

        Show(LinearModeRow, linear);
        Show(SpacingRow, !byCount && p != DistributionPattern.Densidade);
        Show(CountRow, byCount);
        Show(AlignmentRow, linear && !IsClosed);
        Show(DensityRow, p == DistributionPattern.Densidade);
        Show(RowsRow, rowed);
        Show(ArrangementRow, (rowed && Rows > 1) || p == DistributionPattern.Circular);
        Show(RowSpacingRow, (rowed && Rows > 1) || p is DistributionPattern.Triangular or DistributionPattern.Retangular or DistributionPattern.Circular);
        Show(OffsetRow, linear);
        Show(GroupRow, p == DistributionPattern.Agrupamentos);
        Show(GapRow, p == DistributionPattern.Agrupamentos);
        Show(GradientRow, p == DistributionPattern.Gradiente);
        Show(MarginRow, !linear);
        Show(JitterRow, lattice);
        Show(AngleRow, lattice || p is DistributionPattern.Gradiente or DistributionPattern.Circular);
        Show(ClosedBox, linear && _paths.Count == 1);

        RowSpacingLabel.Content = p switch
        {
            DistributionPattern.Circular => "Entre anéis (m)",
            DistributionPattern.Triangular or DistributionPattern.Retangular => "Entre linhas (m)",
            _ => "Entre fileiras (m)",
        };
        AngleLabel.Content = p switch
        {
            DistributionPattern.Gradiente => "Direção do degradê (°)",
            DistributionPattern.Circular => "Rotação dos anéis (°)",
            _ => "Ângulo da malha (°)",
        };
        AngleBox.ToolTip = p == DistributionPattern.Gradiente
            ? "Sentido em que as plantas rareiam: 0° = para a direita (leste), 90° = para cima (norte)"
            : "Rotação, em graus";
        PatternHint.Text = Hints.TryGetValue(p, out string? hint) ? hint : string.Empty;
        ReseedButton.Visibility = p is DistributionPattern.Natural or DistributionPattern.Agrupamentos or DistributionPattern.Gradiente
                                  || (lattice && Num.ParseOr(JitterBox.Text, 0) > 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private DistributionRequest BuildRequest(out string problem)
    {
        string first = string.Empty;
        DistributionPattern pattern = Pattern;
        var request = new DistributionRequest
        {
            Pattern = pattern,
            Seed = _seed,
            Rows = Rows,
            RowArrangement = (ArrangementBox.SelectedItem as Choice<RowArrangement>)?.Value ?? RowArrangement.Paralelas,
        };

        void Fail(string message)
        {
            if (first.Length == 0)
            {
                first = message;
            }
        }

        if (SpacingRow.Visibility == Visibility.Visible)
        {
            if (!Num.TryParse(SpacingBox.Text, out double spacing) || spacing < PlantDistributor.MinSpacing)
            {
                Fail("Informe o espaçamento em metros (no mínimo 0,02 m).");
            }

            request.SpacingM = spacing;
        }

        if (pattern == DistributionPattern.Densidade)
        {
            if (!Num.TryParse(DensityBox.Text, out double density) || density <= 0)
            {
                Fail("Informe a densidade em unidades por m².");
            }

            request.DensityPerM2 = density;
        }

        if (pattern == DistributionPattern.Linear)
        {
            request.LinearMode = Mode;
            request.LinearAlignment = (AlignmentBox.SelectedItem as Choice<LinearAlignment>)?.Value ?? LinearAlignment.Inicio;
            request.PathOffsetM = Num.ParseOr(OffsetBox.Text, 0);
            request.PathClosed = IsClosed;
            if (Mode == LinearMode.Quantidade)
            {
                if (!Num.TryParseInt(CountBox.Text, out int count) || count < 1)
                {
                    Fail("Informe a quantidade de plantas por fileira.");
                }

                request.Count = count;
                request.SpacingM = _species.EffectiveSpacingM;
            }
        }
        else
        {
            request.EdgeMarginM = Math.Max(0, Num.ParseOr(MarginBox.Text, 0));
            request.JitterPercent = Math.Clamp(Num.ParseOr(JitterBox.Text, 0), 0, 50);
            request.AngleDeg = Num.ParseOr(AngleBox.Text, 0);
        }

        if (RowSpacingRow.Visibility == Visibility.Visible)
        {
            request.RowSpacingM = Math.Max(0, Num.ParseOr(RowSpacingBox.Text, 0));
        }

        if (pattern == DistributionPattern.Agrupamentos)
        {
            if (!Num.TryParseInt(GroupMinBox.Text, out int min) || !Num.TryParseInt(GroupMaxBox.Text, out int max) || min < 1 || max < min || max > 25)
            {
                Fail("Plantas por mancha: informe um mínimo e um máximo entre 1 e 25 (mínimo ≤ máximo).");
            }
            else
            {
                request.GroupSizeMin = min;
                request.GroupSizeMax = max;
            }

            request.GroupGapM = Math.Max(0, Num.ParseOr(GapBox.Text, 0));
        }

        if (pattern == DistributionPattern.Gradiente)
        {
            request.GradientEndPercent = Math.Clamp(Num.ParseOr(GradientEndBox.Text, 15), 0, 100);
        }

        request.MaxCount = Num.TryParseInt(MaxBox.Text, out int maxCount) && maxCount > 0 ? maxCount : 0;

        if (AvoidBox.IsChecked == true && _existing.Count > 0)
        {
            double clearance = pattern == DistributionPattern.Densidade
                ? PlantDistributor.EffectiveSpacing(request)
                : request.SpacingM > 0 ? request.SpacingM : _species.EffectiveSpacingM;
            if (clearance > 0)
            {
                request.Obstacles = _existing.Select(p => new Obstacle(p, clearance * 0.999)).ToList();
            }
        }

        problem = first;
        return request;
    }

    private IReadOnlyList<PlantingPath> EffectivePaths() =>
        _paths.Count == 1 ? new[] { _paths[0] with { Closed = IsClosed } } : _paths;

    private void Recalculate()
    {
        if (_loading)
        {
            return;
        }

        UpdateFields();
        DistributionRequest request = BuildRequest(out string problem);
        if (problem.Length > 0)
        {
            _result = new DistributionResult { Message = problem };
        }
        else
        {
            _result = _linear
                ? PlantDistributor.DistributeAlongPaths(null, EffectivePaths(), request)
                : PlantDistributor.Distribute(_region, request);
        }

        Preview.Show(_region, _result.Points, _species.CrownDiameterM, false, EffectivePaths(), VegetationPalette.Foliage(_species, _settings.Colors));

        var parts = new List<string>();
        if (_region is not null && !_region.IsEmpty)
        {
            parts.Add($"Área: {Num.Format(_region.Area, "N2")} m²");
        }

        if (_linear)
        {
            parts.Add($"Eixo: {Num.Format(EffectivePaths().Sum(p => p.Length), "N2")} m");
        }

        parts.Add(_result.Exceeded
            ? $"Calculados: {_result.ComputedCount.ToString("N0", Num.PtBr)} exemplares"
            : $"Exemplares: {_result.Points.Count.ToString("N0", Num.PtBr)}");
        if (_linear && _result.HasPoints && _result.EffectiveSpacingM > 0)
        {
            parts.Add($"Espaçamento efetivo: {Num.Format(_result.EffectiveSpacingM, "0.00")} m");
        }

        SummaryText.Text = string.Join("   ·   ", parts);
        MessageText.Text = _result.Message;
        OkButton.IsEnabled = _result.HasPoints && !_result.Exceeded;
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Recalculate();

    private void Pattern_Changed(object sender, SelectionChangedEventArgs e) => Recalculate();

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
