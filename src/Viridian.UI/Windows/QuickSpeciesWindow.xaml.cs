using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.UI.Windows;

/// <summary>
/// Seletor rápido de espécie usado pelas ferramentas de plantio: pesquisa direta, espécies recentes no topo,
/// símbolo colorido e foto. No modo de inserção reúne também as opções dos exemplares, em uma única janela.
/// A biblioteca completa (filtros e cadastro) fica a um clique.
/// </summary>
public partial class QuickSpeciesWindow : ViridianWindow
{
    private const int MaxRows = 300;
    private readonly CatalogService _catalog;
    private readonly LibraryPaths _paths;
    private readonly ViridianSettings _settings;
    private readonly bool _insertOptions;
    private string _lastVariantSpecies = string.Empty;

    /// <param name="caption">Título (por exemplo, "Inserir vegetação").</param>
    /// <param name="actionLabel">Texto do botão principal (por exemplo, "Inserir").</param>
    /// <param name="insertOptions">Mostra as opções dos exemplares (variante, situação, estágio, observações).</param>
    /// <param name="actionHint">Explicação do que acontece depois de confirmar.</param>
    public QuickSpeciesWindow(CatalogService catalog, LibraryPaths paths, ViridianSettings settings, string caption, string actionLabel, bool insertOptions, string actionHint)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
        _insertOptions = insertOptions;
        InitializeComponent();
        Title = "Viridian — " + caption;
        HeaderText.Text = caption;
        OkButton.Content = "_" + actionLabel;
        ActionHint.Text = actionHint;
        OptionsGroup.Visibility = insertOptions ? Visibility.Visible : Visibility.Collapsed;
        foreach (PlantStatus status in Enum.GetValues<PlantStatus>())
        {
            StatusBox.Items.Add(new Choice<PlantStatus>(Labels.Of(status), status));
        }

        StatusBox.SelectedIndex = 0;
        Refresh(null);
        Loaded += (_, _) => SearchBox.Focus();
    }

    public Species? SelectedSpecies => (SpeciesGrid.SelectedItem as SpeciesRow)?.Species;

    /// <summary>Opções dos exemplares (modo de inserção).</summary>
    public PlantOptions Options { get; } = new();

    private void Refresh(string? selectId)
    {
        string text = SearchBox.Text.Trim();
        List<Species> found;
        if (text.Length == 0)
        {
            // Sem pesquisa: recentes primeiro, depois o catálogo em ordem de código.
            var recent = _settings.RecentSpecies.Select(_catalog.Find).OfType<Species>().ToList();
            var recentIds = new HashSet<string>(recent.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            found = recent.Concat(_catalog.All.Where(s => !recentIds.Contains(s.Id)).OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase)).ToList();
            CountText.Text = recent.Count > 0 ? $"{recent.Count} recente(s) no topo · {_catalog.All.Count} espécies" : $"{_catalog.All.Count} espécies";
        }
        else
        {
            found = _catalog.Search(new SpeciesFilter { Text = text }).ToList();
            CountText.Text = found.Count == 1 ? "1 espécie encontrada" : $"{found.Count} espécies encontradas";
        }

        List<SpeciesRow> rows = found.Take(MaxRows).Select(s => new SpeciesRow(s, _settings.Colors)).ToList();
        SpeciesGrid.ItemsSource = rows;
        SpeciesRow? select = rows.FirstOrDefault(r => string.Equals(r.Species.Id, selectId, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
        SpeciesGrid.SelectedItem = select;
        if (select is not null)
        {
            SpeciesGrid.ScrollIntoView(select);
        }

        ShowDetails();
    }

    private void ShowDetails()
    {
        Species? species = SelectedSpecies;
        OkButton.IsEnabled = species is not null;
        PhotoImage.Source = null;
        NoPhotoText.Visibility = Visibility.Visible;
        if (species is null)
        {
            DetailTitle.Text = "Nenhuma espécie";
            DetailSubtitle.Text = string.Empty;
            InfoText.Text = string.Empty;
            SymbolView.Show(null, DetailProfile.Detalhado);
            return;
        }

        DetailTitle.Text = species.DisplayName;
        DetailSubtitle.Text = species.ScientificName;
        InfoText.Text = $"{Labels.Of(species.Category)} · {Labels.Of(species.Origin)} · altura {Num.Format(species.AdultHeightM)} m · copa {Num.Format(species.CrownDiameterM)} m"
                        + (string.IsNullOrWhiteSpace(species.Flowering) ? string.Empty : " · floração " + species.Flowering);

        if (_insertOptions && !string.Equals(_lastVariantSpecies, species.Id, StringComparison.OrdinalIgnoreCase))
        {
            _lastVariantSpecies = species.Id;
            VariantBox.Items.Clear();
            VariantBox.Items.Add(new Choice<int>("Aleatória", -1));
            for (int i = 0; i < species.EffectiveVariants; i++)
            {
                VariantBox.Items.Add(new Choice<int>("Variante " + (i + 1), i));
            }

            VariantBox.SelectedIndex = 0;
            VariantBox.IsEnabled = !species.HasCustomPlantingFamily && species.EffectiveVariants > 1;
            StageBox.Text = species.PlantingHeightM > 0 ? "Muda h = " + Num.Format(species.PlantingHeightM, "0.00") + " m" : string.Empty;
        }

        DrawSymbol(species);
        LoadPhoto(species);
    }

    private void DrawSymbol(Species species)
    {
        int variant = _insertOptions ? Math.Max(0, (VariantBox.SelectedItem as Choice<int>)?.Value ?? 0) : 0;
        SymbolView.Show(
            SymbolGenerator.Generate(species.SymbolStyle, species.CrownDiameterM, species.EffectiveSymbolSeed, variant),
            DetailProfile.Detalhado,
            SymbolPaint.For(species, _settings.Colors),
            _settings);
    }

    private void LoadPhoto(Species species)
    {
        string file = _paths.ResolvePhoto(species.PhotoFile);
        if (file.Length == 0)
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 280;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            PhotoImage.Source = image;
            NoPhotoText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or UriFormatException or InvalidOperationException)
        {
            ViridianLog.Warn("Fotografia ilegível: " + species.PhotoFile, ex);
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh(null);
        }
    }

    /// <summary>Setas para cima e para baixo percorrem a lista sem sair do campo de pesquisa.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up) || SpeciesGrid.Items.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(SpeciesGrid.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, SpeciesGrid.Items.Count - 1);
        SpeciesGrid.SelectedIndex = index;
        SpeciesGrid.ScrollIntoView(SpeciesGrid.SelectedItem);
        e.Handled = true;
    }

    private void SpeciesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void SpeciesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSpecies is not null)
        {
            Ok_Click(sender, e);
        }
    }

    private void VariantBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && SelectedSpecies is Species species)
        {
            DrawSymbol(species);
        }
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        var browser = new SpeciesBrowserWindow(_catalog, _paths, _settings, "Escolher na biblioteca") { Owner = this };
        if (browser.ShowDialog() == true && browser.SelectedSpecies is Species chosen)
        {
            SearchBox.Text = string.Empty;
            Refresh(chosen.Id);
        }
        else
        {
            Refresh(SelectedSpecies?.Id);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSpecies is null)
        {
            return;
        }

        Options.VariantIndex = (VariantBox.SelectedItem as Choice<int>)?.Value ?? -1;
        Options.Status = (StatusBox.SelectedItem as Choice<PlantStatus>)?.Value ?? PlantStatus.Novo;
        Options.Stage = StageBox.Text.Trim();
        Options.Notes = NotesBox.Text.Trim();
        DialogResult = true;
    }
}
