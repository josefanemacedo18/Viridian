using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Viridian.Core.Coding;
using Viridian.Core.Config;
using Viridian.Core.Hatch;
using Viridian.Core.Model;
using Viridian.Core.Scale;
using Viridian.Core.Symbols;
using Viridian.Library;
using Viridian.Core.Colors;
using Viridian.UI.Controls;
using Viridian.Core.Interference;
using Viridian.Core.Composition;
using Viridian.Core.Detailing;

namespace Viridian.UI.Windows;

/// <summary>Linha editável do padrão gráfico de uma camada.</summary>
public sealed class TierRow : INotifyPropertyChanged
{
    private int _lineWeight;
    private string _color;

    public TierRow(TierStyle style)
    {
        Tier = style.Tier;
        _lineWeight = style.LineWeight;
        _color = style.Color;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SymbolTier Tier { get; }

    public string Name => TierVisibility.DisplayName(Tier);

    public IReadOnlyList<int> Weights { get; } = Enumerable.Range(1, 16).ToList();

    public int LineWeight
    {
        get => _lineWeight;
        set
        {
            _lineWeight = value;
            Raise();
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            _color = value ?? string.Empty;
            Raise();
            Raise(nameof(Swatch));
        }
    }

    public Brush Swatch =>
        ColorHex.TryParse(Color, out byte r, out byte g, out byte b)
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b))
            : Brushes.Transparent;

    public bool IsValid => ColorHex.TryParse(Color, out _, out _, out _);

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Linha editável da cor-base de uma categoria.</summary>
public sealed class CategoryColorRow : INotifyPropertyChanged
{
    private string _color;

    public CategoryColorRow(PlantCategory category, string color)
    {
        Category = category;
        _color = color;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlantCategory Category { get; }

    public string Name => Labels.Plural(Category);

    public string Color
    {
        get => _color;
        set
        {
            _color = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Swatch)));
        }
    }

    public Brush Swatch =>
        Rgb.TryParse(Color, out Rgb rgb) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B)) : Brushes.Transparent;

    public bool IsValid => Rgb.TryParse(Color, out _);
}

/// <summary>Linha editável de uma regra de interferência.</summary>
public sealed class InterferenceRuleRow
{
    public InterferenceRuleRow(InterferenceRule rule)
    {
        Kind = rule.Kind;
        Enabled = rule.Enabled;
        Tree = Num.Format(rule.TreeM);
        Palm = Num.Format(rule.PalmM);
        Shrub = Num.Format(rule.ShrubM);
    }

    public NetworkKind Kind { get; }

    public string Name => InterferenceChecker.DisplayName(Kind);

    public bool Enabled { get; set; }

    public string Tree { get; set; }

    public string Palm { get; set; }

    public string Shrub { get; set; }
}

/// <summary>Configurações do escritório.</summary>
public partial class SettingsWindow : ViridianWindow
{
    private readonly LibraryPaths _paths;
    private readonly List<HatchPattern> _customHatches;
    private List<TierRow> _tiers = new();
    private List<CategoryColorRow> _categoryColors = new();
    private List<InterferenceRuleRow> _interferenceRules = new();
    private Dictionary<string, string> _specTemplates = new();
    private bool _specLoading;
    private readonly List<(SymbolPreview View, Species Sample)> _colorPreviews = new();
    private bool _loading = true;

    public SettingsWindow(ViridianSettings settings, LibraryPaths paths)
    {
        Settings = settings.Clone();
        _paths = paths;
        _customHatches = Settings.CustomHatches.Select(h => ViridianJson.Deserialize<HatchPattern>(ViridianJson.Serialize(h))!).ToList();
        InitializeComponent();
        for (int i = 1; i <= 8; i++)
        {
            DigitsBox.Items.Add(i);
        }

        foreach (double scale in AccessibilitySettings.Scales)
        {
            TextScaleBox.Items.Add(new Choice<double>((scale * 100).ToString("0", Num.PtBr) + "%", scale));
        }

        CrownFillBox.Items.Add(new Choice<CrownFillMode>("Cor sólida", CrownFillMode.Solido));
        CrownFillBox.Items.Add(new Choice<CrownFillMode>("Reticulado colorido", CrownFillMode.Reticulado));
        CrownFillBox.Items.Add(new Choice<CrownFillMode>("Sem cor (só linhas)", CrownFillMode.Nenhum));
        BuildColorPreviews();

        Load(Settings);
        _loading = false;
        UpdateColorPreview();
        UpdateScaleText();
        UpdateCodeExample();
        FillHatches(null);
    }

    /// <summary>Configurações editadas (válidas quando o diálogo retorna verdadeiro).</summary>
    public ViridianSettings Settings { get; private set; }

    private void Load(ViridianSettings s)
    {
        LibraryBox.Text = s.LibraryRoot;
        TemplateBox.Text = s.FamilyTemplateFile;
        FilesText.Text = $"Catálogo: {_paths.CatalogFile}\nConfigurações: {LibraryPaths.SettingsFile}\nParâmetros compartilhados: {LibraryPaths.SharedParametersFile}\nRegistro de erros: {LibraryPaths.LogFolder}";

        DetailedBox.Text = RangeOf(s, DetailProfile.Detalhado, 50).ToString(Num.PtBr);
        IntermediateBox.Text = RangeOf(s, DetailProfile.Intermediario, 100).ToString(Num.PtBr);
        SimplifiedBox.Text = RangeOf(s, DetailProfile.Simplificado, 250).ToString(Num.PtBr);
        AutoScaleBox.IsChecked = s.AutoApplyScaleProfiles;

        _tiers = s.TierStyles.Select(t => new TierRow(t)).ToList();
        TierList.ItemsSource = _tiers;
        SyncMarksBox.IsChecked = s.SyncMarks;
        foreach (TierRow tier in _tiers)
        {
            tier.PropertyChanged += (_, _) => UpdateColorPreview();
        }

        LoadColors(s.Colors);
        TextScaleBox.SelectedIndex = Math.Max(0, AccessibilitySettings.Scales.ToList().FindIndex(x => Math.Abs(x - s.Accessibility.TextScale) < 0.01));
        HighContrastBox.IsChecked = s.Accessibility.HighContrast;
        LoadComposition(s);
        _interferenceRules = s.Interference.Rules.Select(r => new InterferenceRuleRow(r)).ToList();
        InterferenceList.ItemsSource = _interferenceRules;
        CrownFactorBox.Text = Num.Format(s.Interference.BuildingCrownFactor, "0.##");
        PavementBox.IsChecked = s.Interference.CheckPavements;
        LinksBox.IsChecked = s.Interference.IncludeLinks;
        InterferenceEnabledBox.IsChecked = s.Interference.Enabled;

        LowBox.Text = Num.Format(s.Density.LowMax);
        MediumBox.Text = Num.Format(s.Density.MediumMax);
        HighBox.Text = Num.Format(s.Density.HighMax);
        PrefixBox.Text = s.MassingPrefix;
        MassingReserveBox.Text = Num.Format(s.Quantities.MassingReservePercent);
        IndividualReserveBox.Text = Num.Format(s.Quantities.IndividualReservePercent);

        EdgeBox.Text = Num.Format(s.Planting.EdgeMarginM);
        JitterBox.Text = Num.Format(s.Planting.JitterPercent);
        MaxCountBox.Text = s.Planting.MaxCount.ToString(Num.PtBr);
        ElevationBox.Text = Num.Format(s.Planting.ElevationOffsetM);
        RotationBox.IsChecked = s.Planting.RandomRotation;

        CodeTemplateBox.Text = s.InstanceCode.Template;
        DigitsBox.SelectedItem = s.InstanceCode.Digits;

        CheckCrownBox.IsChecked = s.Analysis.CheckCrownConflicts;
        CheckSpacingBox.IsChecked = s.Analysis.CheckSpacing;
        OverlapBox.Text = Num.Format(s.Analysis.CrownOverlapTolerancePercent);
        MinCrownBox.Text = Num.Format(s.Analysis.MinCrownForConflictM);
        SpacingTolBox.Text = Num.Format(s.Analysis.SpacingTolerancePercent);
        DuplicateBox.Text = Num.Format(s.Analysis.DuplicateToleranceM, "0.###");

        TextSizeBox.Text = Num.Format(s.Labels.TextSizeMm);
        OffsetBox.Text = Num.Format(s.Labels.OffsetMm);
        LeaderBox.IsChecked = s.Labels.LeaderByDefault;
    }

    private void LoadColors(ColorSettings colors)
    {
        CrownFillBox.SelectedIndex = Math.Max(0, CrownFillBox.Items.Cast<Choice<CrownFillMode>>().ToList().FindIndex(c => c.Value == colors.CrownFill));
        CrownTintBox.Text = Num.Format(colors.CrownTint * 100, "0");
        MassingTintBox.Text = Num.Format(colors.MassingBackgroundTint * 100, "0");
        ShowFlowersBox.IsChecked = colors.ShowFlowers;
        ColoredMassingsBox.IsChecked = colors.ColoredMassings;
        _categoryColors = Labels.Categories
            .Select(c => new CategoryColorRow(c, colors.CategoryColors.TryGetValue(c, out string? hex) ? hex : VegetationPalette.DefaultCategoryColors()[c]))
            .ToList();
        foreach (CategoryColorRow row in _categoryColors)
        {
            row.PropertyChanged += (_, _) => UpdateColorPreview();
        }

        CategoryColorList.ItemsSource = _categoryColors;
    }

    /// <summary>Cores como estão na tela (valores inválidos são substituídos pelos padrões).</summary>
    private ColorSettings ColorsFromForm()
    {
        var colors = new ColorSettings
        {
            CrownFill = (CrownFillBox.SelectedItem as Choice<CrownFillMode>)?.Value ?? CrownFillMode.Solido,
            CrownTint = Num.TryParse(CrownTintBox.Text, out double crown) ? crown / 100 : 0.28,
            MassingBackgroundTint = Num.TryParse(MassingTintBox.Text, out double massing) ? massing / 100 : 0.3,
            ShowFlowers = ShowFlowersBox.IsChecked == true,
            ColoredMassings = ColoredMassingsBox.IsChecked == true,
            CategoryColors = _categoryColors.ToDictionary(r => r.Category, r => r.Color.Trim()),
        };
        colors.Normalize();
        return colors;
    }

    /// <summary>Um exemplo de cada categoria, com floração nas que costumam florir.</summary>
    private void BuildColorPreviews()
    {
        (PlantCategory Category, double Diameter, string Flower)[] samples =
        {
            (PlantCategory.Arvore, 8, "#F2C230"),
            (PlantCategory.Palmeira, 6, string.Empty),
            (PlantCategory.Arbusto, 2, "#E5609A"),
            (PlantCategory.Herbacea, 0.8, "#E8743B"),
            (PlantCategory.Forracao, 0.5, "#9B6BD3"),
            (PlantCategory.Gramado, 0.5, string.Empty),
            (PlantCategory.Trepadeira, 1.5, "#C9446F"),
            (PlantCategory.Aquatica, 1.0, "#F4F1E6"),
        };

        foreach ((PlantCategory category, double diameter, string flower) in samples)
        {
            var sample = new Species
            {
                Id = "amostra-" + category,
                ScientificName = "Amostra " + category,
                Category = category,
                CrownDiameterM = diameter,
                SymbolStyle = SymbolStyles.Suggest(category, CrownForm.Arredondada),
                FlowerColor = flower,
            };
            var view = new SymbolPreview { Width = 92, Height = 92 };
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            panel.Children.Add(new Border { BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Child = view });
            panel.Children.Add(new TextBlock { Text = Labels.Of(category), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            ColorPreviewPanel.Children.Add(panel);
            _colorPreviews.Add((view, sample));
        }
    }

    private void UpdateColorPreview()
    {
        if (_loading || _colorPreviews.Count == 0)
        {
            return;
        }

        var preview = new ViridianSettings { Colors = ColorsFromForm() };
        preview.TierStyles = _tiers
            .Select(t => new TierStyle { Tier = t.Tier, LineWeight = t.LineWeight, Color = t.IsValid ? ColorHex.Normalize(t.Color) : "#000000" })
            .ToList();
        foreach ((SymbolPreview view, Species sample) in _colorPreviews)
        {
            view.Show(
                SymbolGenerator.Generate(sample.SymbolStyle, sample.CrownDiameterM, sample.EffectiveSymbolSeed),
                DetailProfile.Detalhado,
                SymbolPaint.For(sample, preview.Colors),
                preview);
        }
    }

    private void LoadComposition(ViridianSettings s)
    {
        CompositionSettings c = s.Composition;
        RuleShareBox.IsChecked = c.CheckShare;
        MaxShareBox.Text = Num.Format(c.MaxSharePercent);
        RuleDiversityBox.IsChecked = c.CheckDiversity;
        MinSpeciesBox.Text = c.MinSpecies.ToString(Num.PtBr);
        MinAreaBox.Text = Num.Format(c.MinAreaForDiversityM2);
        RuleScaleBox.IsChecked = c.CheckScale;
        MaxCrownBox.Text = Num.Format(c.MaxCrownToBedPercent);
        RuleTextureBox.IsChecked = c.CheckTexture;
        RulePalmBox.IsChecked = c.CheckPalmUnderstory;
        RuleFloweringBox.IsChecked = c.CheckFlowering;
        ModelMaxBox.Text = s.Model3D.MaxPlants.ToString(Num.PtBr);
        ModelTerrainBox.IsChecked = s.Model3D.FollowTerrain;
        ModelSearchBox.Text = Num.Format(s.Model3D.TerrainSearchM);
        ModelTrunkBox.Text = s.Model3D.TrunkColor;
        ModelFlowersBox.IsChecked = s.Model3D.ShowFlowers;
        SolarProjectBox.IsChecked = s.Solar.UseProjectLocation;
        SolarLatBox.Text = Num.Format(s.Solar.LatitudeDeg, "0.####");
        SolarNorthBox.Text = Num.Format(s.Solar.NorthAdjustDeg);
        SolarStepBox.Text = s.Solar.StepMinutes.ToString(Num.PtBr);
        SolarGridBox.Text = Num.Format(s.Solar.GridM);
        SolarMaxBox.Text = s.Solar.MaxPoints.ToString(Num.PtBr);
        SolarFullBox.Text = Num.Format(s.Solar.FullSunHours);
        SolarShadeBox.Text = Num.Format(s.Solar.ShadeHours);
        SolarPartBox.Text = Num.Format(s.Solar.PartShadeHours);
        SolarTolBox.Text = Num.Format(s.Solar.ToleranceHours);
        SolarLonBox.Text = Num.Format(s.Solar.LongitudeDeg, "0.####");
        SolarTzBox.Text = Num.Format(s.Solar.TimeZoneHours);
        SolarBuildingsBox.IsChecked = s.Solar.IncludeBuildings;
        ModulePavingBox.IsChecked = s.Modules.Paving;
        ModuleRainBox.IsChecked = s.Modules.RainGarden;
        ModuleSolarBox.IsChecked = s.Modules.Solar;

        s.Specifications.Normalize();
        _specTemplates = new Dictionary<string, string>(s.Specifications.Templates);
        _specLoading = true;
        SpecKindBox.Items.Clear();
        SpecKindBox.Items.Add(new Choice<string>("Geral (todas as espécies)", SpecificationTemplates.GeneralKey));
        foreach (DetailKind kind in Enum.GetValues<DetailKind>())
        {
            SpecKindBox.Items.Add(new Choice<string>(PlantingDetails.Name(kind), kind.ToString()));
        }

        SpecKindBox.SelectedIndex = 0;
        _specLoading = false;
        ShowSpec();
    }

    private string SpecKey => (SpecKindBox.SelectedItem as Choice<string>)?.Value ?? SpecificationTemplates.GeneralKey;

    private void ShowSpec()
    {
        _specLoading = true;
        SpecTextBox.Text = _specTemplates.TryGetValue(SpecKey, out string? text) ? text : string.Empty;
        _specLoading = false;
        UpdateSpecHint();
    }

    private void UpdateSpecHint()
    {
        IReadOnlyList<string> unknown = SpecificationTemplates.UnknownFields(SpecTextBox.Text);
        SpecFieldsText.Text = "Campos: " + string.Join(", ", SpecificationTemplates.Fields.Select(f => "{" + f + "}"))
                              + (unknown.Count > 0 ? "\nCampo(s) desconhecido(s): " + string.Join(", ", unknown.Select(f => "{" + f + "}")) : string.Empty);
    }

    private void SpecKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_specLoading)
        {
            ShowSpec();
        }
    }

    private void SpecText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_specLoading)
        {
            return;
        }

        _specTemplates[SpecKey] = SpecTextBox.Text;
        UpdateSpecHint();
    }

    private void SpecDefault_Click(object sender, RoutedEventArgs e)
    {
        _specTemplates[SpecKey] = SpecificationTemplates.Defaults()[SpecKey];
        ShowSpec();
    }

    private void ColorSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateColorPreview();

    private void ColorCheck_Changed(object sender, RoutedEventArgs e) => UpdateColorPreview();

    private void ColorText_Changed(object sender, TextChangedEventArgs e) => UpdateColorPreview();

    private void ColorDefaults_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        LoadColors(new ColorSettings());
        _loading = false;
        UpdateColorPreview();
    }

    private static int RangeOf(ViridianSettings s, DetailProfile profile, int fallback) =>
        s.Scales.Ranges.FirstOrDefault(r => r.Profile == profile)?.MaxDenominator ?? fallback;

    private void UpdateScaleText()
    {
        if (!_loading)
        {
            MinimalText.Text = Num.TryParseInt(SimplifiedBox.Text, out int max) && max > 0
                ? $"acima de 1:{max}"
                : "acima do limite do perfil Simplificado";
        }
    }

    private void UpdateCodeExample()
    {
        if (_loading)
        {
            return;
        }

        var format = new InstanceCodeFormat { Template = CodeTemplateBox.Text.Trim(), Digits = DigitsBox.SelectedItem is int d ? d : 4 };
        CodeExample.Text = format.IsValid
            ? "Exemplo: " + format.Format(PlantCategory.Arvore, "AR-001", 12)
            : "O formato precisa conter o marcador {N}.";
    }

    private void Scale_Changed(object sender, TextChangedEventArgs e) => UpdateScaleText();

    private void Code_Changed(object sender, RoutedEventArgs e) => UpdateCodeExample();

    // ------------------------------------------------------------------ biblioteca

    private void LibraryBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pasta da biblioteca Viridian" };
        if (Directory.Exists(LibraryBox.Text))
        {
            dialog.InitialDirectory = LibraryBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            LibraryBox.Text = dialog.FolderName;
        }
    }

    private void LibraryDefault_Click(object sender, RoutedEventArgs e) => LibraryBox.Text = string.Empty;

    private void TemplateBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Modelo de família",
            Filter = "Modelos de família do Revit (*.rft)|*.rft",
        };
        if (dialog.ShowDialog(this) == true)
        {
            TemplateBox.Text = dialog.FileName;
        }
    }

    private void TemplateAuto_Click(object sender, RoutedEventArgs e) => TemplateBox.Text = string.Empty;

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => OpenFolder(_paths.Root);

    private void OpenConfig_Click(object sender, RoutedEventArgs e) => OpenFolder(LibraryPaths.ConfigFolder);

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Warn("Não foi possível abrir a pasta:\n" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ hachuras

    private void FillHatches(string? selectKey)
    {
        HatchList.Items.Clear();
        foreach (HatchPattern hatch in HatchLibrary.BuiltIn.Concat(_customHatches))
        {
            var item = new ListBoxItem
            {
                Content = hatch.Name + (hatch.IsBuiltIn ? string.Empty : "   (escritório)"),
                Tag = hatch,
            };
            HatchList.Items.Add(item);
            if (hatch.Key == selectKey)
            {
                HatchList.SelectedItem = item;
            }
        }

        if (HatchList.SelectedItem is null && HatchList.Items.Count > 0)
        {
            HatchList.SelectedIndex = 0;
        }
    }

    private HatchPattern? SelectedHatch => (HatchList.SelectedItem as ListBoxItem)?.Tag as HatchPattern;

    private void HatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        HatchPattern? hatch = SelectedHatch;
        HatchView.Show(hatch);
        HatchInfo.Text = hatch is null
            ? string.Empty
            : $"{hatch.Application}\n{(hatch.Target == HatchTarget.Model ? "Padrão de modelo (dimensões reais)" : "Padrão de desenho (dimensões no papel)")}\n{hatch.Grids.Count} família(s) de linhas";
        HatchRemoveButton.IsEnabled = hatch is not null && !hatch.IsBuiltIn;
    }

    private void HatchImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar hachuras",
            Filter = "Arquivos de hachura (*.pat)|*.pat",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<HatchPattern> imported = PatFile.Parse(File.ReadAllText(dialog.FileName), out IReadOnlyList<string> warnings);
            if (imported.Count == 0)
            {
                Warn("Nenhum padrão válido foi encontrado no arquivo." + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings.Take(10)) : string.Empty));
                return;
            }

            var reserved = new HashSet<string>(HatchLibrary.BuiltIn.Select(h => h.Name), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (HatchPattern pattern in imported)
            {
                if (reserved.Contains(pattern.Name))
                {
                    pattern.Name += " (importada)";
                    pattern.Key += "-importada";
                }

                _customHatches.RemoveAll(h => string.Equals(h.Key, pattern.Key, StringComparison.OrdinalIgnoreCase));
                _customHatches.Add(pattern);
                added++;
            }

            try
            {
                Directory.CreateDirectory(_paths.CustomHatchesFolder);
                File.Copy(dialog.FileName, Path.Combine(_paths.CustomHatchesFolder, Path.GetFileName(dialog.FileName)), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ViridianLog.Warn("Não foi possível copiar o arquivo .pat para a biblioteca.", ex);
            }

            FillHatches(imported[^1].Key);
            Inform($"{added} padrão(ões) importado(s)." + (warnings.Count > 0 ? $"\n\n{warnings.Count} linha(s) ignorada(s):\n" + string.Join("\n", warnings.Take(8)) : string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível ler o arquivo:\n" + ex.Message);
        }
    }

    private void HatchRemove_Click(object sender, RoutedEventArgs e)
    {
        HatchPattern? hatch = SelectedHatch;
        if (hatch is null || hatch.IsBuiltIn)
        {
            return;
        }

        _customHatches.RemoveAll(h => h.Key == hatch.Key);
        FillHatches(null);
    }

    // ------------------------------------------------------------------ gravação

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Restaurar os valores padrão de todas as abas?\n\nA pasta da biblioteca e as hachuras do escritório são mantidas."))
        {
            return;
        }

        var defaults = new ViridianSettings
        {
            LibraryRoot = LibraryBox.Text.Trim(),
            FamilyTemplateFile = TemplateBox.Text.Trim(),
        };
        defaults.Normalize();
        _loading = true;
        Load(defaults);
        _loading = false;
        UpdateScaleText();
        UpdateCodeExample();
        UpdateColorPreview();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();

        double Number(TextBox box, string label, double min, double max)
        {
            if (!Num.TryParse(box.Text, out double value) || value < min || value > max)
            {
                problems.Add($"{label}: informe um número entre {Num.Format(min)} e {Num.Format(max)}.");
                return min;
            }

            return value;
        }

        int Integer(TextBox box, string label, int min, int max)
        {
            if (!Num.TryParseInt(box.Text, out int value) || value < min || value > max)
            {
                problems.Add($"{label}: informe um número inteiro entre {min} e {max}.");
                return min;
            }

            return value;
        }

        ViridianSettings s = Settings;
        string library = LibraryBox.Text.Trim();
        if (library.Length > 0 && !Path.IsPathRooted(library))
        {
            problems.Add("A pasta da biblioteca deve ser um caminho completo.");
        }

        string template = TemplateBox.Text.Trim();
        if (template.Length > 0 && !File.Exists(template))
        {
            problems.Add("O modelo de família informado não existe.");
        }

        int detailed = Integer(DetailedBox, "Escala do perfil Detalhado", 1, 100000);
        int intermediate = Integer(IntermediateBox, "Escala do perfil Intermediário", 1, 100000);
        int simplified = Integer(SimplifiedBox, "Escala do perfil Simplificado", 1, 100000);
        if (problems.Count == 0 && !(detailed < intermediate && intermediate < simplified))
        {
            problems.Add("As faixas de escala precisam crescer: Detalhado < Intermediário < Simplificado.");
        }

        foreach (CategoryColorRow row in _categoryColors.Where(r => !r.IsValid))
        {
            problems.Add($"Cor inválida para {row.Name} (use o formato #RRGGBB).");
        }

        var rules = new List<InterferenceRule>();
        foreach (InterferenceRuleRow row in _interferenceRules)
        {
            double Distance(string text, string label)
            {
                if (!Num.TryParse(text, out double value) || value < 0 || value > 50)
                {
                    problems.Add($"Interferências, {row.Name} ({label}): informe uma distância entre 0 e 50 m.");
                    return 0;
                }

                return value;
            }

            rules.Add(new InterferenceRule
            {
                Kind = row.Kind,
                Enabled = row.Enabled,
                TreeM = Distance(row.Tree, "árvores"),
                PalmM = Distance(row.Palm, "palmeiras"),
                ShrubM = Distance(row.Shrub, "arbustos"),
            });
        }

        double crownFactor = Number(CrownFactorBox, "Fração do raio da copa até a fachada", 0, 1.5);
        double maxShare = Number(MaxShareBox, "Participação máxima de uma espécie", 5, 100);
        int minSpecies = Integer(MinSpeciesBox, "Mínimo de espécies por canteiro", 1, 20);
        double minArea = Number(MinAreaBox, "Área mínima para exigir diversidade", 0, 10000);
        double maxCrown = Number(MaxCrownBox, "Copa adulta em relação ao canteiro", 20, 300);
        int modelMax = Integer(ModelMaxBox, "Máximo de exemplares da maquete", 1, Core.Model3D.ModelSettings.HardLimit);
        double modelSearch = Number(ModelSearchBox, "Distância de busca do terreno", 0.5, 200);
        double solarLat = Number(SolarLatBox, "Latitude", -66, 66);
        double solarNorth = Number(SolarNorthBox, "Ajuste do norte", -180, 180);
        int solarStep = Integer(SolarStepBox, "Intervalo do estudo solar", 10, 120);
        double solarGrid = Number(SolarGridBox, "Malha do estudo solar", 0.25, 20);
        int solarMax = Integer(SolarMaxBox, "Máximo de pontos do estudo solar", 50, 5000);
        double solarFull = Number(SolarFullBox, "Horas de sol pleno", 1, 14);
        double solarShade = Number(SolarShadeBox, "Horas de meia-sombra", 0, 14);
        double solarPart = Number(SolarPartBox, "Horas de sombra parcial", 0, 14);
        double solarTol = Number(SolarTolBox, "Tolerância do estudo solar", 0, 3);
        double solarLon = Number(SolarLonBox, "Longitude", -180, 180);
        double solarTz = Number(SolarTzBox, "Fuso horário", -12, 14);
        if (problems.Count == 0 && solarShade > solarFull)
        {
            problems.Add("As horas de sombra devem ser menores que as de sol pleno.");
        }
        if (!Core.Colors.Rgb.TryParse(ModelTrunkBox.Text.Trim(), out _))
        {
            problems.Add("Cor dos troncos inválida (use o formato #RRGGBB).");
        }
        foreach ((string key, string specText) in _specTemplates)
        {
            IReadOnlyList<string> unknown = SpecificationTemplates.UnknownFields(specText);
            if (unknown.Count > 0)
            {
                problems.Add($"Nota de especificação \"{key}\": campo(s) desconhecido(s) " + string.Join(", ", unknown.Select(f => "{" + f + "}")) + ".");
            }
        }
        double crownTint = Number(CrownTintBox, "Clareamento da copa", 0, 85);
        double massingTint = Number(MassingTintBox, "Clareamento do fundo dos maciços", 0, 95);

        foreach (TierRow tier in _tiers.Where(t => !t.IsValid))
        {
            problems.Add($"Cor inválida em \"{tier.Name}\" (use o formato #RRGGBB).");
        }

        double low = Number(LowBox, "Densidade baixa", 0.0001, 1000);
        double medium = Number(MediumBox, "Densidade média", 0.0001, 1000);
        double high = Number(HighBox, "Densidade alta", 0.0001, 1000);
        if (problems.Count == 0 && !(low < medium && medium < high))
        {
            problems.Add("Os limites de densidade precisam crescer: baixa < média < alta.");
        }

        var codeFormat = new InstanceCodeFormat { Template = CodeTemplateBox.Text.Trim(), Digits = DigitsBox.SelectedItem is int digits ? digits : 4 };
        if (!codeFormat.IsValid)
        {
            problems.Add("O formato de código precisa conter o marcador {N}.");
        }

        double massingReserve = Number(MassingReserveBox, "Reserva de maciços", 0, 100);
        double individualReserve = Number(IndividualReserveBox, "Reserva de exemplares", 0, 100);
        double edge = Number(EdgeBox, "Afastamento das bordas", 0, 50);
        double jitter = Number(JitterBox, "Variação de posição", 0, 50);
        int maxCount = Integer(MaxCountBox, "Quantidade máxima", 1, Core.Distribution.PlantDistributor.HardLimit);
        double elevation = Number(ElevationBox, "Deslocamento vertical", -1000, 1000);
        double overlap = Number(OverlapBox, "Sobreposição de copas", 0, 100);
        double minCrown = Number(MinCrownBox, "Copa mínima verificada", 0, 60);
        double spacingTol = Number(SpacingTolBox, "Redução do espaçamento", 0, 100);
        double duplicate = Number(DuplicateBox, "Distância de duplicado", 0.001, 1);
        double textSize = Number(TextSizeBox, "Altura do texto", 0.8, 20);
        double offset = Number(OffsetBox, "Distância da etiqueta", 0, 100);

        if (problems.Count > 0)
        {
            Warn(string.Join("\n", problems.Distinct()));
            return;
        }

        s.LibraryRoot = library;
        s.FamilyTemplateFile = template;
        s.Scales.Ranges = new List<ScaleProfileRange>
        {
            new() { Profile = DetailProfile.Detalhado, MaxDenominator = detailed },
            new() { Profile = DetailProfile.Intermediario, MaxDenominator = intermediate },
            new() { Profile = DetailProfile.Simplificado, MaxDenominator = simplified },
        };
        s.AutoApplyScaleProfiles = AutoScaleBox.IsChecked == true;
        s.TierStyles = _tiers.Select(t => new TierStyle { Tier = t.Tier, LineWeight = t.LineWeight, Color = ColorHex.Normalize(t.Color) }).ToList();
        s.SyncMarks = SyncMarksBox.IsChecked == true;
        s.Accessibility.TextScale = (TextScaleBox.SelectedItem as Choice<double>)?.Value ?? 1.0;
        s.Accessibility.HighContrast = HighContrastBox.IsChecked == true;
        s.Composition = new CompositionSettings
        {
            CheckShare = RuleShareBox.IsChecked == true,
            MaxSharePercent = maxShare,
            CheckDiversity = RuleDiversityBox.IsChecked == true,
            MinSpecies = minSpecies,
            MinAreaForDiversityM2 = minArea,
            CheckScale = RuleScaleBox.IsChecked == true,
            MaxCrownToBedPercent = maxCrown,
            CheckTexture = RuleTextureBox.IsChecked == true,
            CheckPalmUnderstory = RulePalmBox.IsChecked == true,
            CheckFlowering = RuleFloweringBox.IsChecked == true,
        };
        s.Specifications = new SpecificationSettings { Templates = new Dictionary<string, string>(_specTemplates) };
        // Parte das opções atuais: período, mapas e nome do local escolhidos na janela do estudo são mantidos.
        Core.Solar.SolarSettings solar = (s.Solar ?? new Core.Solar.SolarSettings()).Clone();
        solar.UseProjectLocation = SolarProjectBox.IsChecked == true;
        solar.LatitudeDeg = solarLat;
        solar.LongitudeDeg = solarLon;
        solar.TimeZoneHours = solarTz;
        solar.NorthAdjustDeg = solarNorth;
        solar.StepMinutes = solarStep;
        solar.GridM = solarGrid;
        solar.MaxPoints = solarMax;
        solar.FullSunHours = solarFull;
        solar.ShadeHours = solarShade;
        solar.PartShadeHours = Math.Min(solarPart, solarShade);
        solar.ToleranceHours = solarTol;
        solar.IncludeBuildings = SolarBuildingsBox.IsChecked == true;
        s.Solar = solar;
        s.Modules = new ModuleSettings
        {
            Paving = ModulePavingBox.IsChecked == true,
            RainGarden = ModuleRainBox.IsChecked == true,
            Solar = ModuleSolarBox.IsChecked == true,
        };
        // As escolhas da janela da maquete (motor, qualidade, estação...) não aparecem aqui: são mantidas.
        Core.Model3D.ModelSettings previous = s.Model3D ?? new Core.Model3D.ModelSettings();
        s.Model3D = new Core.Model3D.ModelSettings
        {
            MaxPlants = modelMax,
            FollowTerrain = ModelTerrainBox.IsChecked == true,
            TerrainSearchM = modelSearch,
            TrunkColor = ColorHex.Normalize(ModelTrunkBox.Text.Trim()),
            ShowFlowers = ModelFlowersBox.IsChecked == true,
            Engine = previous.Engine,
            Quality = previous.Quality,
            UseSeasonality = previous.UseSeasonality,
            ReferenceMonth = previous.ReferenceMonth,
            ShowFruits = previous.ShowFruits,
            Variation = previous.Variation,
            ShowEdges = previous.ShowEdges,
            WarnTriangles = previous.WarnTriangles,
        };
        s.Interference.Rules = rules;
        s.Interference.BuildingCrownFactor = crownFactor;
        s.Interference.CheckPavements = PavementBox.IsChecked == true;
        s.Interference.IncludeLinks = LinksBox.IsChecked == true;
        s.Interference.Enabled = InterferenceEnabledBox.IsChecked == true;
        s.Colors = ColorsFromForm();
        s.Colors.CrownTint = crownTint / 100;
        s.Colors.MassingBackgroundTint = massingTint / 100;
        s.Density = new Core.Massing.DensityRules { LowMax = low, MediumMax = medium, HighMax = high };
        s.MassingPrefix = PrefixBox.Text;
        s.Quantities.MassingReservePercent = massingReserve;
        s.Quantities.IndividualReservePercent = individualReserve;
        s.Planting.EdgeMarginM = edge;
        s.Planting.JitterPercent = jitter;
        s.Planting.MaxCount = maxCount;
        s.Planting.ElevationOffsetM = elevation;
        s.Planting.RandomRotation = RotationBox.IsChecked == true;
        s.InstanceCode = codeFormat;
        s.Analysis.CheckCrownConflicts = CheckCrownBox.IsChecked == true;
        s.Analysis.CheckSpacing = CheckSpacingBox.IsChecked == true;
        s.Analysis.CrownOverlapTolerancePercent = overlap;
        s.Analysis.MinCrownForConflictM = minCrown;
        s.Analysis.SpacingTolerancePercent = spacingTol;
        s.Analysis.DuplicateToleranceM = duplicate;
        s.Labels.TextSizeMm = textSize;
        s.Labels.OffsetMm = offset;
        s.Labels.LeaderByDefault = LeaderBox.IsChecked == true;
        s.CustomHatches = _customHatches;
        s.Normalize();
        DialogResult = true;
    }
}
