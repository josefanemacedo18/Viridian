using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.UI.Windows;

/// <summary>Linha do resultado do ateliê.</summary>
public sealed class PaletteRow
{
    public PaletteRow(PaletteMatch match, ColorSettings colors)
    {
        Match = match;
        Row = new SpeciesRow(match.Species, colors);
    }

    public PaletteMatch Match { get; }

    public SpeciesRow Row { get; }

    public string ScoreText => (Match.Score * 100).ToString("0", Num.PtBr) + "%";

    public string Reason => Match.Reason;
}

/// <summary>Ateliê de paletas: escolhe espécies pela harmonia de cores e pela época de floração.</summary>
public partial class PaletteWindow : ViridianWindow
{
    private readonly CatalogService _catalog;
    private readonly LibraryPaths _paths;
    private readonly ViridianSettings _settings;
    private readonly List<CheckBox> _months = new();
    private Palette _palette;
    private bool _loading = true;

    public PaletteWindow(CatalogService catalog, LibraryPaths paths, ViridianSettings settings, bool allowPlacement)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
        InitializeComponent();

        foreach (Palette preset in Palette.Presets())
        {
            PresetList.Items.Add(new Choice<Palette>(preset.Name, preset));
        }

        HarmonyBox.Items.Add(new Choice<Harmony>("Monocromática (uma cor)", Harmony.Monocromatica));
        HarmonyBox.Items.Add(new Choice<Harmony>("Análoga (cores vizinhas)", Harmony.Analoga));
        HarmonyBox.Items.Add(new Choice<Harmony>("Complementar (cores opostas)", Harmony.Complementar));
        HarmonyBox.Items.Add(new Choice<Harmony>("Tríade (três cores)", Harmony.Triade));
        HarmonyBox.SelectedIndex = 0;
        HueSlider.Value = 280;
        HueStrip.Background = HueGradient();

        for (int m = 1; m <= 12; m++)
        {
            var box = new CheckBox { Content = MonthSet.Abbreviations[m - 1], Tag = m, Margin = new Thickness(0, 2, 10, 2), MinWidth = 48 };
            box.Checked += Filter_Changed;
            box.Unchecked += Filter_Changed;
            _months.Add(box);
            MonthPanel.Children.Add(box);
        }

        CategoryBox.Items.Add(new Choice<PlantCategory?>("Todas as categorias", null));
        foreach (PlantCategory category in Labels.Categories)
        {
            CategoryBox.Items.Add(new Choice<PlantCategory?>(Labels.Plural(category), category));
        }

        CategoryBox.SelectedIndex = 0;
        InsertButton.Visibility = allowPlacement ? Visibility.Visible : Visibility.Collapsed;
        DistributeButton.Visibility = allowPlacement ? Visibility.Visible : Visibility.Collapsed;

        _palette = Palette.Presets()[0];
        PresetList.SelectedIndex = 0;
        _loading = false;
        Refresh();
    }

    public Species? SelectedSpecies => (ResultGrid.SelectedItem as PaletteRow)?.Match.Species;

    public BrowserAction Action { get; private set; } = BrowserAction.None;

    private void Refresh()
    {
        if (_loading)
        {
            return;
        }

        var months = MonthSet.None;
        foreach (CheckBox box in _months.Where(b => b.IsChecked == true))
        {
            months = months.With((int)box.Tag);
        }

        _palette.Months = months;
        ShowSwatches();
        PlantCategory? category = (CategoryBox.SelectedItem as Choice<PlantCategory?>)?.Value;
        IEnumerable<Species> pool = _catalog.All;
        if (category.HasValue)
        {
            pool = pool.Where(s => s.Category == category.Value);
        }

        if (NativeBox.IsChecked == true)
        {
            pool = pool.Where(s => s.Origin == PlantOrigin.Nativa);
        }

        if (FlowersOnlyBox.IsChecked == true)
        {
            pool = pool.Where(s => !string.IsNullOrWhiteSpace(s.FlowerColor));
        }

        List<PaletteRow> rows = PaletteStudio.Rank(pool, _palette, _settings.Colors).Select(m => new PaletteRow(m, _settings.Colors)).ToList();
        ResultGrid.ItemsSource = rows;
        ResultGrid.SelectedIndex = rows.Count > 0 ? 0 : -1;
        CountText.Text = rows.Count == 1 ? "1 espécie combina com a paleta" : $"{rows.Count} espécies combinam com a paleta";
        ShowDetails();
    }

    private void ShowSwatches()
    {
        SwatchPanel.Children.Clear();
        if (_palette.Hues.Count == 0)
        {
            SwatchPanel.Children.Add(Swatch(Rgb.White, "Branco"));
            SwatchPanel.Children.Add(Swatch(new Rgb(0x4E, 0x8A, 0x3E), "Verde"));
            return;
        }

        foreach (double hue in _palette.Hues)
        {
            Rgb color = Rgb.FromHsl(hue, 0.65, 0.55);
            SwatchPanel.Children.Add(Swatch(color, PaletteStudio.Describe(color)));
        }
    }

    private static FrameworkElement Swatch(Rgb color, string name)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new Border
        {
            Width = 40,
            Height = 26,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
        });
        panel.Children.Add(new TextBlock { Text = name, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center });
        return panel;
    }

    private static Brush HueGradient()
    {
        var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int h = 0; h <= 360; h += 30)
        {
            Rgb c = Rgb.FromHsl(h % 360, 0.7, 0.55);
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), h / 360.0));
        }

        gradient.Freeze();
        return gradient;
    }

    private void ShowDetails()
    {
        Species? s = SelectedSpecies;
        InsertButton.IsEnabled = s is not null;
        DistributeButton.IsEnabled = s is not null;
        PhotoImage.Source = null;
        NoPhotoText.Visibility = Visibility.Visible;
        if (s is null)
        {
            DetailTitle.Text = string.Empty;
            DetailSubtitle.Text = string.Empty;
            DetailInfo.Text = string.Empty;
            SymbolView.Show(null, DetailProfile.Detalhado);
            return;
        }

        DetailTitle.Text = s.DisplayName;
        DetailSubtitle.Text = s.ScientificName;
        DetailInfo.Text = $"{Labels.Of(s.Category)} · {Labels.Of(s.Origin)} · textura {Labels.Of(s.EffectiveTexture).ToLowerInvariant()} · escala {Labels.Of(s.Scale).ToLowerInvariant()}"
                          + (string.IsNullOrWhiteSpace(s.Flowering) ? string.Empty : " · floração " + s.Flowering);
        SymbolView.Show(SymbolGenerator.Generate(s.SymbolStyle, s.CrownDiameterM, s.EffectiveSymbolSeed), DetailProfile.Detalhado, SymbolPaint.For(s, _settings.Colors), _settings);
        string file = _paths.ResolvePhoto(s.PhotoFile);
        if (file.Length == 0)
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 240;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            PhotoImage.Source = image;
            NoPhotoText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            ViridianLog.Warn("Fotografia ilegível: " + s.PhotoFile, ex);
        }
    }

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is Choice<Palette> choice)
        {
            _palette = new Palette { Name = choice.Value.Name, Hues = choice.Value.Hues.ToList(), ToleranceDeg = choice.Value.ToleranceDeg, AcceptNeutralFoliage = choice.Value.AcceptNeutralFoliage };
            Refresh();
        }
    }

    private void Harmony_Changed(object sender, SelectionChangedEventArgs e) => BuildHarmony();

    private void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => BuildHarmony();

    private void BuildHarmony()
    {
        if (_loading)
        {
            return;
        }

        Harmony harmony = (HarmonyBox.SelectedItem as Choice<Harmony>)?.Value ?? Harmony.Monocromatica;
        _palette = Palette.FromHarmony(harmony, HueSlider.Value, "Harmonia personalizada");
        PresetList.SelectedIndex = -1;
        Refresh();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => Refresh();

    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void Result_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSpecies is not null && InsertButton.Visibility == Visibility.Visible)
        {
            Finish(BrowserAction.Insert);
        }
    }

    private void Insert_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Insert);

    private void Distribute_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Distribute);

    private void Finish(BrowserAction action)
    {
        if (SelectedSpecies is null)
        {
            return;
        }

        Action = action;
        DialogResult = true;
    }
}
