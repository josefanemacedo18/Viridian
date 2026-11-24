using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;
using Viridian.Core.Colors;
using System.Windows.Media;

namespace Viridian.UI.Windows;

/// <summary>Cadastro e edição de uma espécie do catálogo.</summary>
public partial class SpeciesEditorWindow : ViridianWindow
{
    private readonly LibraryPaths _paths;
    private readonly ViridianSettings _settings;
    private readonly List<Viridian.Core.Model3D.Profiles.Plant3DProfile> _profiles;
    private bool _loading = true;
    private int _seed;

    public SpeciesEditorWindow(Species species, LibraryPaths paths, ViridianSettings settings, string title)
    {
        Species = species;
        _paths = paths;
        _settings = settings;
        _seed = species.SymbolSeed;
        InitializeComponent();
        Title = "Viridian — " + title;
        HeaderText.Text = title;

        foreach (PlantCategory category in Labels.Categories)
        {
            CategoryBox.Items.Add(new Choice<PlantCategory>(Labels.Of(category), category));
        }

        Fill(OriginBox, Enum.GetValues<PlantOrigin>(), Labels.Of);
        Fill(FormBox, Enum.GetValues<CrownForm>(), Labels.Of);
        Fill(GrowthBox, Enum.GetValues<GrowthRate>(), Labels.Of);
        Fill(WaterBox, Enum.GetValues<WaterNeed>(), Labels.Of);
        Fill(LightBox, Enum.GetValues<LightNeed>(), Labels.Of);
        foreach (string unit in SupplyUnits.Common)
        {
            UnitBox.Items.Add(unit);
        }

        foreach (DetailProfile profile in Enum.GetValues<DetailProfile>())
        {
            ProfileBox.Items.Add(new Choice<DetailProfile>(ScaleProfileSet.DisplayName(profile), profile));
        }

        for (int i = 1; i <= Species.MaxVariants; i++)
        {
            VariantsBox.Items.Add(i);
        }

        foreach (Texture texture in new[] { Texture.Fina, Texture.Media, Texture.Grossa })
        {
            TextureBox.Items.Add(new Choice<Texture>(Labels.Of(texture), texture));
        }

        foreach (LeafHabit h in Enum.GetValues<LeafHabit>())
        {
            LeafHabitBox.Items.Add(new Choice<LeafHabit>(Labels.Of(h), h));
        }

        _profiles = LoadProfiles(paths);
        Profile3DBox.Items.Add(new Choice<string>("Automático", string.Empty));
        foreach (Viridian.Core.Model3D.Profiles.Plant3DProfile profile in _profiles.OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            Profile3DBox.Items.Add(new Choice<string>(profile.Name, profile.Id));
        }

        foreach (WaterloggingTolerance t in Enum.GetValues<WaterloggingTolerance>())
        {
            WaterloggingBox.Items.Add(new Choice<WaterloggingTolerance>(Labels.Of(t), t));
        }

        foreach (Biome biome in Labels.Biomes)
        {
            BiomePanel.Children.Add(new CheckBox { Content = Labels.Of(biome), Tag = biome, Margin = new Thickness(0, 3, 14, 3) });
        }

        foreach (HatchPattern hatch in HatchLibrary.BuiltIn.Concat(settings.CustomHatches))
        {
            HatchBox.Items.Add(new Choice<string>(hatch.Name + " — " + hatch.Application, hatch.Key));
        }

        Load(species);
        _loading = false;
        UpdateStyleList();
        UpdatePreview();
        UpdateHatch();
        Loaded += (_, _) => PopularBox.Focus();
    }

    /// <summary>Espécie editada (válida somente quando o diálogo retorna verdadeiro).</summary>
    public Species Species { get; }

    private static void Fill<T>(ComboBox box, IEnumerable<T> values, Func<T, string> label)
    {
        foreach (T value in values)
        {
            box.Items.Add(new Choice<T>(label(value), value));
        }
    }

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

    private static T Value<T>(ComboBox box, T fallback) => box.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private void Load(Species s)
    {
        Select(CategoryBox, s.Category);
        CodeBox.Text = s.Code;
        PopularBox.Text = s.PopularName;
        ScientificBox.Text = s.ScientificName;
        FamilyBox.Text = s.BotanicalFamily;
        Select(OriginBox, s.Origin);
        Select(FormBox, s.CrownForm);
        HeightBox.Text = s.AdultHeightM > 0 ? Num.Format(s.AdultHeightM) : string.Empty;
        CrownBox.Text = Num.Format(s.CrownDiameterM);
        PlantingHeightBox.Text = s.PlantingHeightM > 0 ? Num.Format(s.PlantingHeightM) : string.Empty;
        SpacingBox.Text = s.RecommendedSpacingM > 0 ? Num.Format(s.RecommendedSpacingM) : string.Empty;
        DensityBox.Text = s.DensityPerM2 > 0 ? Num.Format(s.DensityPerM2) : string.Empty;
        UnitBox.Text = s.SupplyUnit;
        PriceBox.Text = s.UnitPrice > 0 ? s.UnitPrice.ToString("0.00", Num.PtBr) : string.Empty;
        Select(GrowthBox, s.Growth);
        Select(WaterBox, s.Water);
        Select(LightBox, s.Light);
        FloweringBox.Text = s.Flowering;
        NotesBox.Text = s.Notes;
        foreach (CheckBox box in BiomePanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = box.Tag is Biome biome && (s.Biomes & biome) != 0;
        }

        Select(TextureBox, s.EffectiveTexture);
        if (WaterloggingBox.Items[0] is Choice<WaterloggingTolerance>)
        {
            // Primeira opção mostra a estimativa usada enquanto a tolerância não for cadastrada.
            WaterloggingBox.Items[0] = new Choice<WaterloggingTolerance>(
                $"Não informada (estimada: {Labels.Of(Viridian.Core.Water.WaterloggingRules.Estimate(s))})", WaterloggingTolerance.NaoInformada);
        }

        Select(WaterloggingBox, s.Waterlogging);
        if (LeafHabitBox.Items[0] is Choice<LeafHabit>)
        {
            LeafHabitBox.Items[0] = new Choice<LeafHabit>($"Não informada (estimada: {Labels.Of(Viridian.Core.Solar.LeafRules.Estimate(s))})", LeafHabit.NaoInformada);
        }

        Select(LeafHabitBox, s.LeafHabit);
        LeaflessBox.Text = s.LeaflessMonths;
        Fill3D(s);
        ScaleText.Text = Labels.Of(s.Scale);
        FoliageColorBox.Text = s.FoliageColor;
        FlowerColorBox.Text = s.FlowerColor;
        PriceCodeBox.Text = s.PriceItemCode;
        ServiceCodeBox.Text = s.ServiceItemCode;
        VariantsBox.SelectedItem = s.EffectiveVariants;
        ProfileBox.SelectedIndex = 0;
        Select(HatchBox, string.IsNullOrWhiteSpace(s.HatchPatternKey) ? HatchLibrary.DefaultKeyFor(s.Category) : s.HatchPatternKey);
        ShowFiles();
    }

    private void ShowFiles()
    {
        PhotoText.Text = string.IsNullOrWhiteSpace(Species.PhotoFile) ? "(nenhuma)" : Path.GetFileName(Species.PhotoFile);
        PhotoCreditBox.Text = Species.PhotoCredit;
        CustomFamilyText.Text = Species.HasCustomPlantingFamily ? Path.GetFileName(Species.CustomPlantingFamilyFile) : "(símbolo gerado pelo Viridian)";
        CustomFamilyRemove.IsEnabled = Species.HasCustomPlantingFamily;
        CustomDetailText.Text = Species.HasCustomDetailFamily ? Path.GetFileName(Species.CustomDetailFamilyFile) : "(nenhum)";
        CustomDetailRemove.IsEnabled = Species.HasCustomDetailFamily;
        StyleBox.IsEnabled = !Species.HasCustomPlantingFamily;
    }

    private void UpdateStyleList()
    {
        PlantCategory category = Value(CategoryBox, Species.Category);
        SymbolStyle current = StyleBox.SelectedItem is Choice<SymbolStyle> selected ? selected.Value : Species.SymbolStyle;
        bool previous = _loading;
        _loading = true;
        StyleBox.Items.Clear();
        foreach (SymbolStyle style in SymbolStyles.ForCategory(category))
        {
            StyleBox.Items.Add(new Choice<SymbolStyle>(SymbolStyles.DisplayName(style), style));
        }

        Select(StyleBox, current);
        _loading = previous;
    }

    private void UpdateVariantView()
    {
        int count = VariantsBox.SelectedItem is int n ? n : 1;
        int index = Math.Clamp(VariantViewBox.SelectedIndex, 0, count - 1);
        bool previous = _loading;
        _loading = true;
        VariantViewBox.Items.Clear();
        for (int i = 1; i <= count; i++)
        {
            VariantViewBox.Items.Add("Variante " + i);
        }

        VariantViewBox.SelectedIndex = index;
        _loading = previous;
    }

    private void UpdatePreview()
    {
        if (_loading)
        {
            return;
        }

        if (VariantViewBox.Items.Count == 0)
        {
            UpdateVariantView();
        }

        double diameter = Num.TryParse(CrownBox.Text, out double d) ? d : Species.CrownDiameterM;
        SymbolStyle style = Value(StyleBox, Species.SymbolStyle);
        DetailProfile profile = Value(ProfileBox, DetailProfile.Detalhado);
        int seed = _seed != 0
            ? _seed
            : Core.Geometry.DeterministicRandom.StableHash(string.IsNullOrWhiteSpace(ScientificBox.Text) ? Species.Id : ScientificBox.Text.Trim());
        ShowSwatch(FoliageSwatch, FoliageColorBox.Text);
        ShowSwatch(FlowerSwatch, FlowerColorBox.Text);
        var draft = new Species
        {
            Category = Value(CategoryBox, Species.Category),
            SymbolSeed = seed,
            FoliageColor = Rgb.TryParse(FoliageColorBox.Text, out Rgb foliage) ? foliage.Hex : string.Empty,
            FlowerColor = Rgb.TryParse(FlowerColorBox.Text, out Rgb flower) ? flower.Hex : string.Empty,
        };
        SymbolView.Show(SymbolGenerator.Generate(style, diameter, seed, Math.Max(0, VariantViewBox.SelectedIndex)), profile, SymbolPaint.For(draft, _settings.Colors), _settings);
    }

    private void UpdateHatch()
    {
        if (!_loading)
        {
            HatchView.Show(HatchLibrary.Find(Value(HatchBox, string.Empty), _settings.CustomHatches));
        }
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PlantCategory category = Value(CategoryBox, Species.Category);
        UpdateStyleList();

        // Ao trocar de categoria em um cadastro novo, sugere o símbolo e a hachura da nova categoria.
        _loading = true;
        Select(StyleBox, SymbolStyles.Suggest(category, Value(FormBox, CrownForm.Arredondada)));
        Select(HatchBox, HatchLibrary.DefaultKeyFor(category));
        _loading = false;
        UpdatePreview();
        UpdateHatch();
    }

    private void Symbol_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void Variants_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdateVariantView();
            UpdatePreview();
        }
    }

    private void Hatch_Changed(object sender, SelectionChangedEventArgs e) => UpdateHatch();

    private void NewSeed_Click(object sender, RoutedEventArgs e)
    {
        // Sementes positivas e diferentes de zero (zero significa "derivar do nome científico").
        _seed = Random.Shared.Next(1, int.MaxValue);
        UpdatePreview();
    }

    private void PhotoBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Fotografia da espécie (de preferência a planta inteira)",
            Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|Todos os arquivos|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // Foto própria do escritório: os créditos da foto do catálogo deixam de valer.
            PhotoImport.Assign(Species, PhotoImport.FromFile(_paths, Species.Id, dialog.FileName), PhotoCreditBox.Text);
            ShowFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
            ViridianLog.Error("Falha ao importar fotografia.", ex);
            Warn("Não foi possível usar esta imagem:\n" + ex.Message);
        }
    }

    private void PhotoRemove_Click(object sender, RoutedEventArgs e)
    {
        Species.PhotoFile = string.Empty;
        Species.PhotoCredit = string.Empty;
        Species.PhotoLicense = string.Empty;
        Species.PhotoSource = string.Empty;
        ShowFiles();
    }

    private void CustomFamilyRemove_Click(object sender, RoutedEventArgs e)
    {
        Species.CustomPlantingFamilyFile = string.Empty;
        Species.CustomPlantingTypeName = string.Empty;
        ShowFiles();
    }

    private void CustomDetailRemove_Click(object sender, RoutedEventArgs e)
    {
        Species.CustomDetailFamilyFile = string.Empty;
        Species.CustomDetailTypeName = string.Empty;
        ShowFiles();
    }

    private static string NormalizeColor(string text, string label, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (Rgb.TryParse(text, out Rgb color))
        {
            return color.Hex;
        }

        problems.Add($"A {label} deve estar no formato #RRGGBB (ou ficar vazia).");
        return text.Trim();
    }

    private static void ShowSwatch(Border swatch, string text)
    {
        if (Rgb.TryParse(text, out Rgb color))
        {
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            swatch.Background = brush;
        }
        else
        {
            swatch.Background = Brushes.Transparent;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        double Read(TextBox box, string label, bool required)
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                if (required)
                {
                    problems.Add($"Informe {label}.");
                }

                return 0;
            }

            if (!Num.TryParse(box.Text, out double value))
            {
                problems.Add($"O valor de {label} não é um número válido.");
                return 0;
            }

            return value;
        }

        Species s = Species;
        s.Category = Value(CategoryBox, s.Category);
        s.Code = CodeBox.Text.Trim();
        s.PopularName = PopularBox.Text.Trim();
        s.ScientificName = ScientificBox.Text.Trim();
        s.BotanicalFamily = FamilyBox.Text.Trim();
        s.Origin = Value(OriginBox, s.Origin);
        s.CrownForm = Value(FormBox, s.CrownForm);
        s.AdultHeightM = Read(HeightBox, "altura adulta", false);
        s.CrownDiameterM = Read(CrownBox, "copa adulta", true);
        s.PlantingHeightM = Read(PlantingHeightBox, "altura da muda", false);
        s.RecommendedSpacingM = Read(SpacingBox, "espaçamento", false);
        s.DensityPerM2 = Read(DensityBox, "densidade", false);
        s.SupplyUnit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "muda" : UnitBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(PriceBox.Text))
        {
            s.UnitPrice = 0;
        }
        else if (Num.TryParseDecimal(PriceBox.Text, out decimal price))
        {
            s.UnitPrice = price;
        }
        else
        {
            problems.Add("O preço unitário não é um número válido.");
        }

        s.Growth = Value(GrowthBox, s.Growth);
        s.Water = Value(WaterBox, s.Water);
        s.Light = Value(LightBox, s.Light);
        s.Flowering = FloweringBox.Text.Trim();
        s.Notes = NotesBox.Text.Trim();
        s.PhotoCredit = string.IsNullOrWhiteSpace(s.PhotoFile) ? string.Empty : PhotoCreditBox.Text.Trim();
        s.Biomes = BiomePanel.Children.OfType<CheckBox>()
            .Where(b => b.IsChecked == true && b.Tag is Biome)
            .Aggregate(Biome.Nenhum, (all, b) => all | (Biome)b.Tag);
        s.FoliageColor = NormalizeColor(FoliageColorBox.Text, "cor da folhagem", problems);
        s.FlowerColor = NormalizeColor(FlowerColorBox.Text, "cor da flor", problems);
        s.Texture = Value(TextureBox, s.EffectiveTexture);
        s.Waterlogging = Value(WaterloggingBox, s.Waterlogging);
        s.LeafHabit = Value(LeafHabitBox, s.LeafHabit);
        s.LeaflessMonths = LeaflessBox.Text.Trim();
        if (s.LeaflessMonths.Length > 0 && MonthSet.Parse(s.LeaflessMonths).IsEmpty)
        {
            problems.Add("Não reconheci os meses sem folhas. Use, por exemplo: jul–set.");
        }
        s.Plant3DProfileId = (Profile3DBox.SelectedItem as Choice<string>)?.Value ?? string.Empty;
        s.Fruiting = FruitingBox.Text.Trim();
        if (s.Fruiting.Length > 0 && MonthSet.Parse(s.Fruiting).IsEmpty)
        {
            problems.Add("Não reconheci os meses de frutificação. Use, por exemplo: nov–jan.");
        }

        s.FruitColor = NormalizeColor(FruitColorBox.Text, "cor do fruto", problems);
        if (!string.IsNullOrWhiteSpace(s.Flowering) && MonthSet.Parse(s.Flowering).IsEmpty)
        {
            problems.Add("Não reconheci os meses de floração. Use, por exemplo: ago–out, set–nov e mar–abr, ano todo.");
        }

        s.PriceItemCode = PriceCodeBox.Text.Trim();
        s.ServiceItemCode = ServiceCodeBox.Text.Trim();
        s.SymbolStyle = Value(StyleBox, s.SymbolStyle);
        s.SymbolVariants = VariantsBox.SelectedItem is int variants ? variants : 1;
        s.SymbolSeed = _seed;
        s.HatchPatternKey = Value(HatchBox, s.HatchPatternKey);

        if (problems.Count == 0)
        {
            problems.AddRange(SpeciesValidator.Validate(s));
        }

        if (problems.Count > 0)
        {
            Warn(string.Join("\n", problems));
            return;
        }

        DialogResult = true;
    }

    /// <summary>Perfis 3D da biblioteca (os padrões, se o arquivo não puder ser lido).</summary>
    private static List<Viridian.Core.Model3D.Profiles.Plant3DProfile> LoadProfiles(LibraryPaths paths)
    {
        try
        {
            return new Plant3DProfileLibrary(paths).Profiles;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ViridianLog.Warn("Perfis 3D: usando os padrões no cadastro da espécie.", ex);
            return Viridian.Core.Model3D.Profiles.Plant3DProfileDefaults.Create();
        }
    }

    /// <summary>Perfil 3D, frutos e a origem das épocas usadas na maquete (da ficha, estimada pelo gênero ou sem dado).</summary>
    private void Fill3D(Species s)
    {
        string automatic = Viridian.Core.Model3D.Profiles.ProfileResolver.AutomaticId(s);
        string automaticName = _profiles.FirstOrDefault(p => string.Equals(p.Id, automatic, StringComparison.OrdinalIgnoreCase))?.Name ?? automatic;
        Profile3DBox.Items[0] = new Choice<string>($"Automático ({automaticName})", string.Empty);
        Profile3DBox.SelectedItem = Profile3DBox.Items.Cast<Choice<string>>()
            .FirstOrDefault(c => c.Value.Length > 0 && string.Equals(c.Value, s.Plant3DProfileId, StringComparison.OrdinalIgnoreCase)) ?? Profile3DBox.Items[0];
        FruitingBox.Text = s.Fruiting;
        FruitColorBox.Text = s.FruitColor;

        var flowering = Viridian.Core.Model3D.Seasonality.SeasonalityResolver.Flowering(s);
        (var profile, _) = Viridian.Core.Model3D.Profiles.ProfileResolver.Resolve(s, _profiles);
        var fruit = Viridian.Core.Model3D.Seasonality.SeasonalityResolver.Fruit(s, profile);
        string Origin(Viridian.Core.Model3D.Seasonality.SeasonInfo info) => info.Source switch
        {
            Viridian.Core.Model3D.Seasonality.SeasonSource.Ficha => info.Months + " (da ficha)",
            Viridian.Core.Model3D.Seasonality.SeasonSource.Estimado => info.Months + " (estimada pelo gênero)",
            _ => "sem dado (não aparece com a estação ligada)",
        };
        Season3DText.Text = "Na maquete com estação — floração: " + Origin(flowering)
                            + "; frutos: " + (fruit is null ? "sem dado (não aparecem)" : Origin(fruit.Season)) + ".";
    }
}
