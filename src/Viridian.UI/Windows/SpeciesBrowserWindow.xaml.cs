using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Colors;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;

namespace Viridian.UI.Windows;

/// <summary>Ação escolhida ao fechar a biblioteca.</summary>
public enum BrowserAction
{
    None,
    Insert,
    Distribute,
    Massing,
    Select,
}

/// <summary>Linha da lista de espécies.</summary>
public sealed class SpeciesRow
{
    public SpeciesRow(Species species, ColorSettings colors)
    {
        Species = species;
        Rgb foliage = VegetationPalette.Foliage(species, colors);
        FoliageBrush = Brush(foliage);
        Rgb? flower = Rgb.TryParse(species.FlowerColor, out Rgb f) ? f : null;
        FlowerBrush = flower is Rgb fl ? Brush(fl) : System.Windows.Media.Brushes.Transparent;
        ColorTip = "Folhagem " + foliage.Hex + (flower is Rgb x ? " · flor " + x.Hex : string.Empty);
    }

    public System.Windows.Media.Brush FoliageBrush { get; }

    public System.Windows.Media.Brush FlowerBrush { get; }

    public string ColorTip { get; }

    private static System.Windows.Media.Brush Brush(Rgb color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public Species Species { get; }

    public string Code => Species.Code;

    public string PopularName => Species.PopularName;

    public string ScientificName => Species.ScientificName;

    public string CategoryName => Labels.Of(Species.Category);

    public string OriginName => Labels.Of(Species.Origin);

    public string HeightText => Species.AdultHeightM > 0 ? Num.Format(Species.AdultHeightM, "0.0") + " m" : "—";

    public string CrownText => Num.Format(Species.CrownDiameterM, "0.0#") + " m";
}

/// <summary>Biblioteca botânica: pesquisa com filtros projetuais, ficha da espécie e cadastro.</summary>
public partial class SpeciesBrowserWindow : ViridianWindow
{
    private readonly CatalogService _catalog;
    private readonly LibraryPaths _paths;
    private readonly ViridianSettings _settings;
    private bool _loading = true;

    /// <param name="catalog">Catálogo botânico.</param>
    /// <param name="paths">Pastas da biblioteca.</param>
    /// <param name="settings">Configurações do escritório.</param>
    /// <param name="pickCaption">Quando informado, a janela funciona como seletor de espécie com este título.</param>
    /// <param name="allowPlacement">Falso quando não há projeto aberto: oculta as ações de inserção.</param>
    public SpeciesBrowserWindow(CatalogService catalog, LibraryPaths paths, ViridianSettings settings, string? pickCaption = null, bool allowPlacement = true)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
        InitializeComponent();

        FillChoices(CategoryBox, "Todas", Labels.Categories, Labels.Of);
        FillChoices(OriginBox, "Todas", Enum.GetValues<PlantOrigin>().Where(o => o != PlantOrigin.NaoInformada), Labels.Of);
        FillChoices(FormBox, "Todas", Enum.GetValues<CrownForm>(), Labels.Of);
        FillChoices(WaterBox, "Todas", Enum.GetValues<WaterNeed>().Where(w => w != WaterNeed.NaoInformada), Labels.Of);
        FillChoices(LightBox, "Todas", Enum.GetValues<LightNeed>().Where(l => l != LightNeed.NaoInformada), Labels.Of);
        FillChoices(GrowthBox, "Todos", Enum.GetValues<GrowthRate>().Where(g => g != GrowthRate.NaoInformado), Labels.Of);
        FillChoices(BiomeBox, "Todos", Labels.Biomes, Labels.Of);
        FillChoices(MonthBox, "Qualquer", Enumerable.Range(1, 12), m => System.Globalization.CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.GetMonthName(m));

        foreach (DetailProfile profile in Enum.GetValues<DetailProfile>())
        {
            ProfileBox.Items.Add(new Choice<DetailProfile>("Perfil " + ScaleProfileSet.DisplayName(profile), profile));
        }

        ProfileBox.SelectedIndex = 0;

        if (pickCaption is not null)
        {
            Title = "Viridian — " + pickCaption;
            HeaderText.Text = pickCaption;
            InsertButton.Visibility = Visibility.Collapsed;
            DistributeButton.Visibility = Visibility.Collapsed;
            MassingButton.Visibility = Visibility.Collapsed;
            SelectButton.Visibility = Visibility.Visible;
        }

        if (!allowPlacement && pickCaption is null)
        {
            InsertButton.Visibility = Visibility.Collapsed;
            DistributeButton.Visibility = Visibility.Collapsed;
            MassingButton.Visibility = Visibility.Collapsed;
        }

        _loading = false;
        Refresh(null);
        Loaded += (_, _) => SearchBox.Focus();
    }

    public BrowserAction Action { get; private set; } = BrowserAction.None;

    public Species? SelectedSpecies => (SpeciesGrid.SelectedItem as SpeciesRow)?.Species;

    private static void FillChoices<T>(ComboBox box, string allLabel, IEnumerable<T> values, Func<T, string> label)
        where T : struct
    {
        box.Items.Add(new Choice<T?>(allLabel, null));
        foreach (T value in values)
        {
            box.Items.Add(new Choice<T?>(label(value), value));
        }

        box.SelectedIndex = 0;
    }

    private static T? Selected<T>(ComboBox box)
        where T : struct => (box.SelectedItem as Choice<T?>)?.Value;

    private SpeciesFilter BuildFilter() => new()
    {
        Text = SearchBox.Text,
        Category = Selected<PlantCategory>(CategoryBox),
        Origin = Selected<PlantOrigin>(OriginBox),
        CrownForm = Selected<CrownForm>(FormBox),
        Water = Selected<WaterNeed>(WaterBox),
        Light = Selected<LightNeed>(LightBox),
        Growth = Selected<GrowthRate>(GrowthBox),
        MinHeightM = Num.TryParse(MinHeightBox.Text, out double minHeight) ? minHeight : null,
        MaxHeightM = Num.TryParse(MaxHeightBox.Text, out double maxHeight) ? maxHeight : null,
        MinCrownM = Num.TryParse(MinCrownBox.Text, out double minCrown) ? minCrown : null,
        MaxCrownM = Num.TryParse(MaxCrownBox.Text, out double maxCrown) ? maxCrown : null,
        OnlyUserDefined = UserOnlyBox.IsChecked == true,
        Biome = Selected<Biome>(BiomeBox),
        FloweringMonth = Selected<int>(MonthBox),
        OnlyShowyFlowers = ShowyBox.IsChecked == true,
    };

    private void Refresh(string? selectId)
    {
        if (_loading)
        {
            return;
        }

        selectId ??= SelectedSpecies?.Id;
        List<SpeciesRow> rows = _catalog.Search(BuildFilter()).Select(s => new SpeciesRow(s, _settings.Colors)).ToList();
        SpeciesGrid.ItemsSource = rows;
        CountText.Text = rows.Count == 1 ? "1 espécie encontrada" : rows.Count + " espécies encontradas";

        SpeciesRow? toSelect = rows.FirstOrDefault(r => r.Species.Id == selectId) ?? rows.FirstOrDefault();
        SpeciesGrid.SelectedItem = toSelect;
        if (toSelect is not null)
        {
            SpeciesGrid.ScrollIntoView(toSelect);
        }

        ShowDetails();
    }

    private void ShowDetails()
    {
        Species? species = SelectedSpecies;
        bool has = species is not null;
        EditButton.IsEnabled = has;
        DuplicateButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
        InsertButton.IsEnabled = has;
        DistributeButton.IsEnabled = has;
        MassingButton.IsEnabled = has;
        SelectButton.IsEnabled = has;
        PhotoButtons.IsEnabled = has;
        PhotoRestoreButton.Visibility = species is not null && PhotoImport.HasSeedPhoto(species) ? Visibility.Visible : Visibility.Collapsed;

        if (species is null)
        {
            DetailTitle.Text = "Nenhuma espécie selecionada";
            DetailSubtitle.Text = string.Empty;
            DetailRows.ItemsSource = null;
            SymbolView.Show(null, DetailProfile.Detalhado);
            PhotoImage.Source = null;
            NoPhotoText.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = species.DisplayName;
        DetailSubtitle.Text = species.ScientificName;
        DrawSymbol(species);
        LoadPhoto(species);

        var rows = new List<KeyValuePair<string, string>>
        {
            new("Família", species.BotanicalFamily),
            new("Categoria", Labels.Of(species.Category)),
            new("Origem", Labels.Of(species.Origin)),
            new("Forma da copa", Labels.Of(species.CrownForm)),
            new("Altura adulta", species.AdultHeightM > 0 ? Num.Format(species.AdultHeightM) + " m" : "—"),
            new("Copa adulta", Num.Format(species.CrownDiameterM) + " m"),
            new("Altura da muda", species.PlantingHeightM > 0 ? Num.Format(species.PlantingHeightM) + " m" : "—"),
            new("Espaçamento", species.RecommendedSpacingM > 0 ? Num.Format(species.RecommendedSpacingM) + " m" : "—"),
            new("Densidade", species.DensityPerM2 > 0 ? Num.Format(species.DensityPerM2) + " un/m²" : "—"),
            new("Fornecimento", species.SupplyUnit),
            new("Preço unitário", species.UnitPrice > 0 ? species.UnitPrice.ToString("C2", Num.PtBr) : "—"),
            new("Crescimento", Labels.Of(species.Growth)),
            new("Água", Labels.Of(species.Water)),
            new("Luminosidade", Labels.Of(species.Light)),
            new("Floração", string.IsNullOrWhiteSpace(species.Flowering) ? "—" : species.Flowering),
            new("Textura", Labels.Of(species.EffectiveTexture) + (species.Texture == Texture.NaoInformada ? " (sugerida)" : string.Empty)),
            new("Tolera encharcamento", Labels.Of(species.EffectiveWaterlogging) + (species.WaterloggingEstimated ? " (estimativa)" : string.Empty)),
            new("Tolera seca", Labels.Of(species.Drought)),
            new("Folhagem", Labels.Of(species.EffectiveLeafHabit) + (species.LeafHabitEstimated ? " (estimativa)" : string.Empty)
                            + (species.EffectiveLeaflessMonths.IsEmpty ? string.Empty : $"; sem folhas em {species.EffectiveLeaflessMonths}")),
            new("Escala visual", Labels.Of(species.Scale)),
            new("Biomas", species.Biomes == Biome.Nenhum ? (species.Origin == PlantOrigin.Nativa ? "—" : "não nativa do Brasil") : Labels.Of(species.Biomes)),
            new("Cores", ColorText(species)),
            new("Símbolo", species.HasCustomPlantingFamily ? "Família personalizada: " + Path.GetFileName(species.CustomPlantingFamilyFile) : SymbolStyles.DisplayName(species.SymbolStyle)),
            new("Hachura", HatchLibrary.Find(species.HatchPatternKey, _settings.CustomHatches)?.Name ?? "—"),
        };

        if (species.HasCustomDetailFamily)
        {
            rows.Add(new("Símbolo de maciço", Path.GetFileName(species.CustomDetailFamilyFile)));
        }

        if (!string.IsNullOrWhiteSpace(species.Notes))
        {
            rows.Add(new("Observações", species.Notes));
        }

        DetailRows.ItemsSource = rows;
    }

    private string ColorText(Species species)
    {
        string foliage = "folhagem " + VegetationPalette.Foliage(species, _settings.Colors).Hex
                         + (string.IsNullOrWhiteSpace(species.FoliageColor) ? " (da categoria)" : string.Empty);
        return Rgb.TryParse(species.FlowerColor, out Rgb flower) ? foliage + "; flor " + flower.Hex : foliage;
    }

    private void DrawSymbol(Species species)
    {
        DetailProfile profile = (ProfileBox.SelectedItem as Choice<DetailProfile>)?.Value ?? DetailProfile.Detalhado;
        SymbolView.Show(SymbolGenerator.Generate(species.SymbolStyle, species.CrownDiameterM, species.EffectiveSymbolSeed), profile, SymbolPaint.For(species, _settings.Colors), _settings);
    }

    private void LoadPhoto(Species species)
    {
        PhotoImage.Source = null;
        NoPhotoText.Visibility = Visibility.Visible;
        PhotoCreditText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(species.PhotoFile))
        {
            return;
        }

        try
        {
            string file = _paths.ResolvePhoto(species.PhotoFile);
            if (file.Length == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(species.PhotoCredit))
            {
                PhotoCreditText.Text = "Foto: " + species.PhotoCredit
                                       + (string.IsNullOrWhiteSpace(species.PhotoLicense) ? string.Empty : " · " + species.PhotoLicense)
                                       + (species.PhotoSource.Contains("inaturalist", StringComparison.OrdinalIgnoreCase) ? " · iNaturalist" : string.Empty);
                PhotoCreditText.ToolTip = species.PhotoSource;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // não mantém o arquivo aberto
            image.DecodePixelWidth = 320;
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

    private void Filter_Changed(object sender, RoutedEventArgs e) => Refresh(null);

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        SearchBox.Text = string.Empty;
        MinHeightBox.Text = string.Empty;
        MaxHeightBox.Text = string.Empty;
        MinCrownBox.Text = string.Empty;
        MaxCrownBox.Text = string.Empty;
        UserOnlyBox.IsChecked = false;
        ShowyBox.IsChecked = false;
        foreach (ComboBox box in new[] { CategoryBox, OriginBox, FormBox, WaterBox, LightBox, GrowthBox, BiomeBox, MonthBox })
        {
            box.SelectedIndex = 0;
        }

        _loading = false;
        Refresh(null);
    }

    private void SpeciesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            ShowDetails();
        }
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SelectedSpecies is not null)
        {
            DrawSymbol(SelectedSpecies);
        }
    }

    private void SpeciesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSpecies is null)
        {
            return;
        }

        if (SelectButton.Visibility == Visibility.Visible)
        {
            Finish(BrowserAction.Select);
        }
        else
        {
            Edit_Click(sender, e);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        PlantCategory category = Selected<PlantCategory>(CategoryBox) ?? PlantCategory.Arvore;
        EditSpecies(_catalog.NewDraft(category), "Nova espécie");
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSpecies is not null)
        {
            EditSpecies(SelectedSpecies.Clone(), "Editar espécie");
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSpecies is not null)
        {
            EditSpecies(_catalog.Duplicate(SelectedSpecies), "Duplicar espécie");
        }
    }

    // ------------------------------------------------------------------ fotografia

    private void PhotoChange_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSpecies is null)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Fotografia da espécie (de preferência a planta inteira)",
            Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            ReplacePhoto(() => PhotoImport.FromFile(_paths, SelectedSpecies.Id, dialog.FileName));
        }
    }

    private void PhotoPaste_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSpecies is null)
        {
            return;
        }

        ReplacePhoto(() => PhotoImport.FromClipboard(_paths, SelectedSpecies.Id)
                           ?? throw new InvalidOperationException("Não há imagem na área de transferência. No navegador, clique com o botão direito na foto e escolha \"Copiar imagem\"."));
    }

    private void Photo_DragOver(object sender, DragEventArgs e)
    {
        bool ok = SelectedSpecies is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
                  && (e.Data.GetData(DataFormats.FileDrop) as string[])?.Any(PhotoImport.IsImageFile) == true;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Photo_Drop(object sender, DragEventArgs e)
    {
        string? file = (e.Data.GetData(DataFormats.FileDrop) as string[])?.FirstOrDefault(PhotoImport.IsImageFile);
        if (SelectedSpecies is not null && file is not null)
        {
            ReplacePhoto(() => PhotoImport.FromFile(_paths, SelectedSpecies.Id, file));
        }
    }

    private void PhotoRestore_Click(object sender, RoutedEventArgs e)
    {
        Species? current = SelectedSpecies;
        if (current is null)
        {
            return;
        }

        Species copy = ViridianJson.Deserialize<Species>(ViridianJson.Serialize(current)) ?? current.Clone();
        if (!PhotoImport.RestoreSeed(copy))
        {
            Inform("Esta espécie não faz parte do catálogo padrão do Viridian.");
            return;
        }

        SavePhoto(copy);
    }

    /// <summary>Importa a imagem e grava na espécie selecionada (crédito "Foto do escritório", editável no cadastro).</summary>
    private void ReplacePhoto(Func<string> import)
    {
        Species? current = SelectedSpecies;
        if (current is null)
        {
            return;
        }

        string file;
        try
        {
            file = import();
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            ViridianLog.Error("Falha ao importar fotografia.", ex);
            Warn("Não foi possível usar esta imagem:\n" + ex.Message);
            return;
        }

        Species copy = ViridianJson.Deserialize<Species>(ViridianJson.Serialize(current)) ?? current.Clone();
        PhotoImport.Assign(copy, file, string.Empty);
        SavePhoto(copy);
    }

    private void SavePhoto(Species species)
    {
        try
        {
            _catalog.Save(species);
            Refresh(species.Id);
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar o catálogo.", ex);
            Warn("Não foi possível gravar o catálogo em disco:\n" + ex.Message);
        }
    }

    private void EditSpecies(Species draft, string title)
    {
        var editor = new SpeciesEditorWindow(draft, _paths, _settings, title) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _catalog.Save(editor.Species);
            Refresh(editor.Species.Id);
        }
        catch (InvalidOperationException ex)
        {
            Warn(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar o catálogo.", ex);
            Warn("Não foi possível gravar o catálogo em disco:\n" + ex.Message);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        Species? species = SelectedSpecies;
        if (species is null || !Confirm($"Excluir \"{species.DisplayName}\" do catálogo?\n\nOs exemplares já inseridos nos projetos não são alterados."))
        {
            return;
        }

        try
        {
            _catalog.Remove(species.Id);
            Refresh(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViridianLog.Error("Falha ao gravar o catálogo.", ex);
            Warn("Não foi possível gravar o catálogo em disco:\n" + ex.Message);
        }
    }

    private void Insert_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Insert);

    private void Distribute_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Distribute);

    private void Massing_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Massing);

    private void Select_Click(object sender, RoutedEventArgs e) => Finish(BrowserAction.Select);

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
