using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Config;
using Viridian.Core.Distribution;
using Viridian.Core.Geometry;
using Viridian.Core.Hatch;
using Viridian.Core.Massing;
using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Reports;

namespace Viridian.UI.Windows;

/// <summary>Criação e edição de um maciço vegetal, com cálculo de quantidade e pré-visualização.</summary>
public partial class MassingWindow : ViridianWindow
{
    private readonly Species _species;
    private readonly Polygon2 _region;
    private readonly ViridianSettings _settings;
    private readonly ISet<string> _usedIds;
    private readonly bool _areaBased;
    private readonly bool _planView;
    private readonly IReadOnlyList<HatchPattern> _hatches;
    private bool _loading = true;
    private bool _densityDrives;
    private long _seed;
    private DistributionResult _distribution = new();
    private MassingMode _effectiveMode = MassingMode.Hachura;
    private string _effectiveHatchKey = string.Empty;

    /// <param name="species">Espécie do maciço.</param>
    /// <param name="region">Limite da região, em metros.</param>
    /// <param name="record">Registro a editar (ou rascunho de um novo maciço).</param>
    /// <param name="isNew">Verdadeiro ao criar; falso ao editar um maciço existente.</param>
    /// <param name="usedIds">Códigos de maciço já usados por outras regiões do projeto.</param>
    /// <param name="settings">Configurações do escritório.</param>
    /// <param name="canUseDetailSymbol">A espécie possui componente de detalhe personalizado.</param>
    /// <param name="isPlanView">A vista do maciço é uma planta (necessário para símbolos de exemplares).</param>
    public MassingWindow(
        Species species,
        Polygon2 region,
        MassingRecord record,
        bool isNew,
        ISet<string> usedIds,
        ViridianSettings settings,
        bool canUseDetailSymbol,
        bool isPlanView)
    {
        _species = species;
        _region = region;
        _settings = settings;
        _usedIds = usedIds;
        _planView = isPlanView;
        _areaBased = SupplyUnits.IsAreaBased(species.SupplyUnit);
        _hatches = HatchLibrary.BuiltIn.Concat(settings.CustomHatches).ToList();
        _seed = record.Seed == 0 ? 1 : record.Seed;
        Record = record;
        InitializeComponent();

        HeaderText.Text = (isNew ? "Novo maciço — " : "Editar maciço — ") + species.DisplayName;
        OkButton.Content = isNew ? "Criar maciço" : "Aplicar";

        ModeBox.Items.Add(new Choice<MassingMode?>("Automático (pela densidade)", null));
        foreach (MassingMode mode in Enum.GetValues<MassingMode>())
        {
            ModeBox.Items.Add(new Choice<MassingMode?>(DensityRules.DisplayName(mode), mode));
        }

        foreach (HatchPattern hatch in _hatches)
        {
            HatchBox.Items.Add(new Choice<string>(hatch.Name + " — " + hatch.Application, hatch.Key));
        }

        PatternBox.Items.Add(new Choice<DistributionPattern>("Triangular", DistributionPattern.Triangular));
        PatternBox.Items.Add(new Choice<DistributionPattern>("Retangular", DistributionPattern.Retangular));
        PatternBox.Items.Add(new Choice<DistributionPattern>("Natural (agrupamento livre)", DistributionPattern.Natural));

        SymbolKindBox.Items.Add(new Choice<MassingSymbolKind>("Exemplares (Vegetação, contados um a um)", MassingSymbolKind.Planting));
        if (canUseDetailSymbol)
        {
            SymbolKindBox.Items.Add(new Choice<MassingSymbolKind>("Componente de detalhe (apenas gráfico)", MassingSymbolKind.DetailItem));
        }

        if (!canUseDetailSymbol)
        {
            SymbolKindLabel.Visibility = Visibility.Collapsed;
            SymbolKindBox.Visibility = Visibility.Collapsed;
        }

        Load(record, isNew);
        _loading = false;
        Recalculate();
    }

    /// <summary>Registro resultante (válido quando o diálogo retorna verdadeiro).</summary>
    public MassingRecord Record { get; }

    /// <summary>Hachura a aplicar (considera a hachura simplificada para densidades muito altas).</summary>
    public string HatchKey => _effectiveHatchKey;

    private static void Select<T>(ComboBox box, T value)
    {
        foreach (object item in box.Items)
        {
            if (item is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                box.SelectedItem = item;
                return;
            }
        }

        if (box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private void Load(MassingRecord record, bool isNew)
    {
        IdBox.Text = record.Id;
        NotesBox.Text = record.Notes;
        Select<MassingMode?>(ModeBox, isNew ? null : record.Mode);
        string hatchKey = string.IsNullOrWhiteSpace(record.HatchPatternKey)
            ? (string.IsNullOrWhiteSpace(_species.HatchPatternKey) ? HatchLibrary.DefaultKeyFor(_species.Category) : _species.HatchPatternKey)
            : record.HatchPatternKey;
        if (hatchKey == HatchLibrary.Simplified && !string.IsNullOrWhiteSpace(_species.HatchPatternKey))
        {
            hatchKey = _species.HatchPatternKey;
        }

        Select(HatchBox, hatchKey);
        Select(PatternBox, record.Pattern is DistributionPattern.Retangular or DistributionPattern.Natural ? record.Pattern : DistributionPattern.Triangular);
        Select(SymbolKindBox, record.SymbolKind);

        double density = record.DensityPerM2 > 0 ? record.DensityPerM2 : _species.DensityPerM2;
        double spacing = record.SpacingM > 0 ? record.SpacingM : _species.RecommendedSpacingM;
        if (isNew ? _species.DensityPerM2 > 0 : record.DensityPerM2 > 0)
        {
            _densityDrives = true;
            DensityBox.Text = Num.Format(density);
        }
        else
        {
            _densityDrives = false;
            SpacingBox.Text = Num.Format(spacing > 0 ? spacing : _species.EffectiveSpacingM, "0.###");
        }

        MarginBox.Text = Num.Format(isNew ? Math.Min(_settings.Planting.EdgeMarginM, Math.Max(0.05, _species.EffectiveSpacingM / 2)) : record.EdgeMarginM);
        JitterBox.Text = Num.Format(isNew ? _settings.Planting.JitterPercent : record.JitterPercent);
        AngleBox.Text = Num.Format(record.AngleDeg);
        MaxBox.Text = (record.MaxCount > 0 ? record.MaxCount : _settings.Planting.MaxCount).ToString(Num.PtBr);
        RotationBox.IsChecked = isNew ? _settings.Planting.RandomRotation : record.RandomRotation;
        ReserveBox.Text = Num.Format(isNew ? _settings.Quantities.MassingReservePercent : record.ReservePercent);

        if (_areaBased)
        {
            PlantingGroup.IsEnabled = false;
            RotationBox.IsEnabled = false;
            ReseedButton.IsEnabled = false;
            Select<MassingMode?>(ModeBox, MassingMode.Hachura);
            ModeBox.IsEnabled = false;
        }
    }

    private DistributionPattern Pattern => (PatternBox.SelectedItem as Choice<DistributionPattern>)?.Value ?? DistributionPattern.Triangular;

    /// <summary>Mantém densidade e espaçamento coerentes: o último campo editado comanda o outro.</summary>
    private void SyncDensityAndSpacing()
    {
        bool previous = _loading;
        _loading = true;
        if (_densityDrives)
        {
            SpacingBox.Text = Num.TryParse(DensityBox.Text, out double density) && density > 0
                ? Num.Format(MassingCalculator.SpacingFromDensity(density, Pattern), "0.###")
                : string.Empty;
        }
        else
        {
            DensityBox.Text = Num.TryParse(SpacingBox.Text, out double spacing) && spacing > 0
                ? Num.Format(MassingCalculator.DensityFromSpacing(spacing, Pattern), "0.##")
                : string.Empty;
        }

        _loading = previous;
    }

    private void Recalculate()
    {
        if (_loading)
        {
            return;
        }

        SyncDensityAndSpacing();
        double density = Num.ParseOr(DensityBox.Text, 0);
        double spacing = Num.ParseOr(SpacingBox.Text, 0);
        double area = _region.Area;
        string message = string.Empty;

        MassingMode? chosen = (ModeBox.SelectedItem as Choice<MassingMode?>)?.Value;
        _effectiveMode = _areaBased ? MassingMode.Hachura : chosen ?? _settings.Density.SuggestMode(density);

        string selectedHatch = (HatchBox.SelectedItem as Choice<string>)?.Value ?? HatchLibrary.DefaultKeyFor(_species.Category);
        bool simplified = !_areaBased && chosen is null && _effectiveMode == MassingMode.Hachura && _settings.Density.UseSimplifiedHatch(density);
        _effectiveHatchKey = _effectiveMode == MassingMode.Simbolos ? string.Empty : simplified ? HatchLibrary.Simplified : selectedHatch;

        MassingSymbolKind kind = (SymbolKindBox.SelectedItem as Choice<MassingSymbolKind>)?.Value ?? MassingSymbolKind.Planting;
        bool symbols = _effectiveMode != MassingMode.Hachura;
        HatchBox.IsEnabled = _effectiveMode != MassingMode.Simbolos;
        SymbolKindBox.IsEnabled = symbols;
        HatchView.Show(HatchLibrary.Find(_effectiveHatchKey, _settings.CustomHatches));

        string classText = _areaBased
            ? "Espécie fornecida por área: quantificada em m²."
            : $"Densidade {DensityRules.DisplayName(_settings.Density.Classify(density)).ToLowerInvariant()}: {DensityRules.DisplayName(_effectiveMode).ToLowerInvariant()}"
              + (simplified ? " simplificada" : string.Empty) + (chosen is null ? " (sugerido)." : ".");
        ModeHint.Text = classText;

        _distribution = new DistributionResult();
        int quantity;
        if (_areaBased)
        {
            quantity = (int)Math.Min(int.MaxValue, Math.Ceiling(area - 1e-9));
        }
        else if (!(density > 0) || !(spacing > 0))
        {
            quantity = 0;
            message = "Informe a densidade ou o espaçamento.";
        }
        else if (symbols)
        {
            if (kind == MassingSymbolKind.Planting && !_planView)
            {
                message = "Símbolos de exemplares só podem ser inseridos em vistas em planta. Use o modo Hachura nesta vista.";
                quantity = 0;
            }
            else
            {
                _distribution = PlantDistributor.Distribute(_region, new DistributionRequest
                {
                    Pattern = Pattern,
                    SpacingM = spacing,
                    EdgeMarginM = Math.Max(0, Num.ParseOr(MarginBox.Text, 0)),
                    JitterPercent = Math.Clamp(Num.ParseOr(JitterBox.Text, 0), 0, 50),
                    AngleDeg = Num.ParseOr(AngleBox.Text, 0),
                    MaxCount = Num.TryParseInt(MaxBox.Text, out int max) && max > 0 ? max : 0,
                    Seed = _seed,
                });
                quantity = _distribution.Exceeded ? _distribution.ComputedCount : _distribution.Points.Count;
                message = _distribution.Exceeded
                    ? _distribution.Message + " Use o modo Hachura para maciços densos."
                    : _distribution.Message;
            }
        }
        else
        {
            quantity = MassingCalculator.Quantity(area, density);
        }

        Preview.Show(_region, symbols ? _distribution.Points : null, _species.CrownDiameterM, _effectiveMode != MassingMode.Simbolos, null, VegetationPalette.Foliage(_species, _settings.Colors));

        string unit = _areaBased ? "m²" : _species.SupplyUnit;
        double reserve = Math.Clamp(Num.ParseOr(ReserveBox.Text, 0), 0, 100);
        SummaryText.Text = $"Área: {Num.Format(area, "N2")} m²   ·   Quantidade: {quantity.ToString("N0", Num.PtBr)} {unit}";
        PurchaseText.Text = quantity > 0
            ? $"Aquisição com {Num.Format(reserve)}% de reserva: {MassingCalculator.PurchaseQuantity(quantity, reserve).ToString("N0", Num.PtBr)} {unit}"
            : string.Empty;
        MessageText.Text = message;

        bool valid = _areaBased || (density > 0 && spacing > 0 && (!symbols || (_distribution.HasPoints && !_distribution.Exceeded)));
        if (symbols && kind == MassingSymbolKind.Planting && !_planView)
        {
            valid = false;
        }

        OkButton.IsEnabled = valid;
        bool jitterApplies = Pattern != DistributionPattern.Natural;
        JitterBox.IsEnabled = symbols && jitterApplies;
        AngleBox.IsEnabled = symbols && jitterApplies;
        MarginBox.IsEnabled = symbols;
        MaxBox.IsEnabled = symbols;
        RotationBox.IsEnabled = symbols && !_areaBased;
        ReseedButton.IsEnabled = symbols && !_areaBased;
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Recalculate();

    private void Pattern_Changed(object sender, SelectionChangedEventArgs e) => Recalculate();

    private void Density_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            _densityDrives = true;
            Recalculate();
        }
    }

    private void Spacing_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            _densityDrives = false;
            Recalculate();
        }
    }

    private void Reseed_Click(object sender, RoutedEventArgs e)
    {
        _seed = Random.Shared.NextInt64(1, long.MaxValue);
        Recalculate();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string id = IdBox.Text.Trim();
        if (id.Length == 0)
        {
            Warn("Informe o código do maciço.");
            return;
        }

        if (_usedIds.Contains(id))
        {
            Warn($"O código {id} já pertence a outro maciço do projeto.");
            return;
        }

        Recalculate();
        if (!OkButton.IsEnabled)
        {
            return;
        }

        double density = Num.ParseOr(DensityBox.Text, 0);
        double spacing = Num.ParseOr(SpacingBox.Text, 0);
        Record.Id = id;
        Record.Notes = NotesBox.Text.Trim();
        Record.Mode = _effectiveMode;
        Record.SymbolKind = (SymbolKindBox.SelectedItem as Choice<MassingSymbolKind>)?.Value ?? MassingSymbolKind.Planting;
        Record.Pattern = Pattern;
        Record.SpacingM = _areaBased ? 0 : spacing;
        Record.DensityPerM2 = _areaBased ? 0 : (_densityDrives ? density : 0);
        Record.EdgeMarginM = Math.Max(0, Num.ParseOr(MarginBox.Text, 0));
        Record.JitterPercent = Math.Clamp(Num.ParseOr(JitterBox.Text, 0), 0, 50);
        Record.AngleDeg = Num.ParseOr(AngleBox.Text, 0);
        Record.MaxCount = Num.TryParseInt(MaxBox.Text, out int max) && max > 0 ? max : 0;
        Record.RandomRotation = RotationBox.IsChecked == true;
        Record.ReservePercent = Math.Clamp(Num.ParseOr(ReserveBox.Text, 0), 0, 100);
        Record.Seed = _seed;
        Record.HatchPatternKey = _effectiveHatchKey;
        DialogResult = true;
    }
}
