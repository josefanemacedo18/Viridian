using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Config;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Reports;
using Viridian.Library;
using Viridian.UI.Controls;

namespace Viridian.UI.Windows;

/// <summary>
/// Composição multiespécie de um canteiro em uma única operação: destaques, faixas de fundo, meio e frente por
/// proporção da área e forração de base, com prévia em planta. Receitas podem ser gravadas como modelos do escritório.
/// </summary>
public partial class CompositionWindow : ViridianWindow
{
    private readonly CatalogService _catalog;
    private readonly LibraryPaths _paths;
    private readonly ViridianSettings _settings;
    private readonly BedTemplateLibrary _templates;
    private readonly Polygon2 _polygon;
    private readonly Point2? _back;
    private readonly IReadOnlyDictionary<string, SpeciesUsage> _usage;
    private readonly List<ItemRow> _rows = new();
    private long _seed = Environment.TickCount64 & 0xFFFFFF;
    private bool _loading;
    private ItemRow? _editing;

    public CompositionWindow(CatalogService catalog, LibraryPaths paths, ViridianSettings settings, BedTemplateLibrary templates,
        Polygon2 polygon, Point2? backPoint, string regionSource, IReadOnlyDictionary<string, SpeciesUsage> usage)
    {
        _catalog = catalog;
        _paths = paths;
        _settings = settings;
        _templates = templates;
        _polygon = polygon;
        _back = backPoint;
        _usage = usage;
        _loading = true;
        InitializeComponent();
        HeaderText.Text = $"Compor canteiro — {regionSource}, {polygon.Area.ToString("0.0", Num.PtBr)} m²";
        LayoutBox.Items.Add(new Choice<CompositionLayout>(backPoint is null ? "Encostado (fundo no lado mais comprido)" : "Encostado (fundo no lado indicado)", CompositionLayout.Encostado));
        LayoutBox.Items.Add(new Choice<CompositionLayout>("Ilha (fundo no centro, frente em volta)", CompositionLayout.Ilha));
        LayoutBox.SelectedIndex = backPoint is null ? 1 : 0;
        foreach (CompositionRole role in Enum.GetValues<CompositionRole>())
        {
            RoleBox.Items.Add(new Choice<CompositionRole>(RoleName(role), role));
        }

        RefreshTemplates(null);
        _loading = false;
        UpdateEditor();
        Recompute();
    }

    /// <summary>Plano calculado para o contorno (exemplares por espécie e forração de base).</summary>
    public CompositionPlan? Plan { get; private set; }

    /// <summary>Receita atual (espécies, papéis e proporções).</summary>
    public BedTemplate Recipe => new()
    {
        Layout = SelectedLayout,
        Items = _rows.Select(r => r.Item.Clone()).ToList(),
    };

    /// <summary>Nome do modelo usado (vazio quando a receita foi montada aqui).</summary>
    public string TemplateName { get; private set; } = string.Empty;

    private CompositionLayout SelectedLayout => (LayoutBox.SelectedItem as Choice<CompositionLayout>)?.Value ?? CompositionLayout.Encostado;

    private ItemRow? SelectedRow => ItemsGrid.SelectedItem as ItemRow;

    public static string RoleName(CompositionRole role) => role switch
    {
        CompositionRole.Fundo => "Fundo",
        CompositionRole.Meio => "Meio",
        CompositionRole.Frente => "Frente",
        CompositionRole.Destaque => "Destaque",
        CompositionRole.Base => "Base (forração)",
        _ => role.ToString(),
    };

    /// <summary>Papel sugerido pelo porte: árvores e palmeiras como destaque, forrações e gramados como base.</summary>
    public static CompositionRole SuggestRole(Species s) => s.Category switch
    {
        PlantCategory.Arvore or PlantCategory.Palmeira => CompositionRole.Destaque,
        PlantCategory.Gramado => CompositionRole.Base,
        PlantCategory.Forracao => SupplyUnits.IsAreaBased(s.SupplyUnit) ? CompositionRole.Base : CompositionRole.Frente,
        _ when SupplyUnits.IsAreaBased(s.SupplyUnit) => CompositionRole.Base,
        _ when s.AdultHeightM >= 1.2 => CompositionRole.Fundo,
        _ when s.AdultHeightM > 0 && s.AdultHeightM < 0.5 => CompositionRole.Frente,
        _ => CompositionRole.Meio,
    };

    // ------------------------------------------------------------------ modelos

    private void RefreshTemplates(string? selectId)
    {
        bool was = _loading;
        _loading = true;
        TemplateBox.Items.Clear();
        TemplateBox.Items.Add(new Choice<string>("(nova receita)", string.Empty));
        foreach (BedTemplate t in _templates.Templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            TemplateBox.Items.Add(new Choice<string>($"{t.Name} — {t.Summary}", t.Id));
        }

        TemplateBox.SelectedItem = TemplateBox.Items.OfType<Choice<string>>().FirstOrDefault(c => c.Value == (selectId ?? string.Empty)) ?? TemplateBox.Items[0];
        DeleteTemplateButton.IsEnabled = TemplateBox.SelectedIndex > 0;
        _loading = was;
    }

    private void TemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteTemplateButton.IsEnabled = TemplateBox.SelectedIndex > 0;
        if (_loading || _templates.Find((TemplateBox.SelectedItem as Choice<string>)?.Value) is not BedTemplate template)
        {
            return;
        }

        var missing = new List<string>();
        _rows.Clear();
        foreach (CompositionItem item in template.Items)
        {
            Species? species = BedTemplateLibrary.Resolve(_catalog, item.SpeciesId, item.ScientificName);
            if (species is null)
            {
                missing.Add(item.PopularName.Length > 0 ? item.PopularName : item.ScientificName);
                continue;
            }

            CompositionItem copy = item.Clone();
            copy.SpeciesId = species.Id;
            _rows.Add(new ItemRow(copy, species, _settings));
        }

        _loading = true;
        LayoutBox.SelectedItem = LayoutBox.Items.OfType<Choice<CompositionLayout>>().First(c => c.Value == template.Layout);
        _loading = false;
        TemplateName = template.Name;
        RefreshGrid(_rows.FirstOrDefault());
        Recompute();
        if (missing.Count > 0)
        {
            Warn("Espécies do modelo que não estão no catálogo (ficaram de fora):\n" + string.Join("\n", missing));
        }
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            Warn("Adicione as espécies do canteiro antes de gravar o modelo.");
            return;
        }

        var prompt = new TextPromptWindow("Gravar modelo de canteiro", "Nome do modelo", TemplateName,
            "Fica na biblioteca do escritório (" + Path.GetFileName(_templates.File) + "): aparece em Compor Canteiro em qualquer projeto.") { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        BedTemplate template = Recipe;
        template.Name = prompt.Value;
        template.Description = prompt.Description;
        foreach (CompositionItem item in template.Items)
        {
            if (_catalog.Find(item.SpeciesId) is Species s)
            {
                item.ScientificName = s.ScientificName;
                item.PopularName = s.PopularName;
            }
        }

        try
        {
            _templates.Save(template);
            TemplateName = template.Name;
            RefreshTemplates(template.Id);
            Inform($"Modelo \"{template.Name}\" gravado.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ViridianLog.Error("Falha ao gravar o modelo de canteiro.", ex);
            Warn("Não foi possível gravar o modelo:\n" + ex.Message);
        }
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_templates.Find((TemplateBox.SelectedItem as Choice<string>)?.Value) is not BedTemplate template
            || !Confirm($"Excluir o modelo \"{template.Name}\" da biblioteca do escritório?"))
        {
            return;
        }

        try
        {
            _templates.Delete(template.Id);
            RefreshTemplates(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível excluir o modelo:\n" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ espécies

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var picker = new QuickSpeciesWindow(_catalog, _paths, _settings, "Compor canteiro — adicionar espécie", "Adicionar", false,
            "O papel (fundo, meio, frente, destaque ou base) é sugerido pelo porte e pode ser mudado.", _usage) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedSpecies is not Species species)
        {
            return;
        }

        CompositionRole role = SuggestRole(species);
        CompositionItem item = CompositionItem.For(species, role);
        item.Count = role == CompositionRole.Destaque ? 1 : item.Count;
        int bands = _rows.Count(r => r.Item.IsBand);
        item.SharePercent = bands == 0 ? 100 : Math.Round(100.0 / (bands + 1));
        var row = new ItemRow(item, species, _settings);
        _rows.Add(row);
        RefreshGrid(row);
        Recompute();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is ItemRow row)
        {
            int index = _rows.IndexOf(row);
            _rows.Remove(row);
            RefreshGrid(_rows.Count == 0 ? null : _rows[Math.Clamp(index, 0, _rows.Count - 1)]);
            Recompute();
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int delta)
    {
        if (SelectedRow is not ItemRow row)
        {
            return;
        }

        int index = _rows.IndexOf(row), target = index + delta;
        if (target < 0 || target >= _rows.Count)
        {
            return;
        }

        _rows.RemoveAt(index);
        _rows.Insert(target, row);
        RefreshGrid(row);
        Recompute();
    }

    private void RefreshGrid(ItemRow? select)
    {
        ItemsGrid.ItemsSource = null;
        ItemsGrid.ItemsSource = _rows.ToList();
        ItemsGrid.SelectedItem = select;
        UpdateEditor();
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Valor digitado e clique em outra linha: grava na linha anterior antes de trocar o editor.
        if (!_loading && _editing is ItemRow previous && !ReferenceEquals(previous, SelectedRow) && _rows.Contains(previous) && ReadFields(previous))
        {
            Dispatcher.BeginInvoke(new Action(Recompute));
        }

        UpdateEditor();
    }

    private void UpdateEditor()
    {
        if (!IsInitialized)
        {
            return;
        }

        ItemRow? row = SelectedRow;
        _editing = row;
        bool has = row is not null;
        RoleBox.IsEnabled = AmountBox.IsEnabled = SpacingBox.IsEnabled = RemoveButton.IsEnabled = UpButton.IsEnabled = DownButton.IsEnabled = has;
        OkButton.IsEnabled = _rows.Count > 0;
        bool was = _loading;
        _loading = true;
        if (row is null)
        {
            RoleBox.SelectedIndex = -1;
            AmountBox.Text = SpacingBox.Text = string.Empty;
        }
        else
        {
            RoleBox.SelectedItem = RoleBox.Items.OfType<Choice<CompositionRole>>().First(c => c.Value == row.Item.Role);
            AmountLabel.Text = row.Item.Role switch
            {
                CompositionRole.Destaque => "Quantidade",
                CompositionRole.Base => "—",
                _ => "% da área",
            };
            AmountBox.IsEnabled = row.Item.Role != CompositionRole.Base;
            AmountBox.Text = row.Item.Role switch
            {
                CompositionRole.Destaque => row.Item.Count.ToString(Num.PtBr),
                CompositionRole.Base => string.Empty,
                _ => Num.Format(row.Item.SharePercent, "0.#"),
            };
            SpacingBox.IsEnabled = row.Item.Role != CompositionRole.Base;
            SpacingBox.Text = row.Item.SpacingM > 0 ? Num.Format(row.Item.SpacingM, "0.##") : string.Empty;
        }

        _loading = was;
    }

    private void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SelectedRow is not ItemRow row || RoleBox.SelectedItem is not Choice<CompositionRole> choice)
        {
            return;
        }

        row.Item.Role = choice.Value;
        if (choice.Value == CompositionRole.Destaque && row.Item.Count < 1)
        {
            row.Item.Count = 1;
        }

        RefreshGrid(row);
        Recompute();
    }

    private void AmountBox_LostFocus(object sender, RoutedEventArgs e) => CommitEditor();

    private void SpacingBox_LostFocus(object sender, RoutedEventArgs e) => CommitEditor();

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEditor();
            e.Handled = true;
        }
    }

    /// <summary>Grava os campos do editor na linha que estava sendo editada e refaz o plano se algo mudou.</summary>
    private void CommitEditor()
    {
        if (_loading || _editing is not ItemRow row || !_rows.Contains(row))
        {
            return;
        }

        if (ReadFields(row))
        {
            RefreshGrid(SelectedRow ?? row);
            Recompute();
        }
        else
        {
            UpdateEditor();
        }
    }

    /// <summary>Lê proporção/quantidade e espaçamento para a linha. Valores inválidos são descartados.</summary>
    private bool ReadFields(ItemRow row)
    {
        bool changed = false;
        if (row.Item.Role != CompositionRole.Base
            && Num.TryParse(AmountBox.Text, out double value) && value >= 0 && (row.Item.Role == CompositionRole.Destaque || value <= 100))
        {
            if (row.Item.Role == CompositionRole.Destaque)
            {
                int count = (int)Math.Clamp(Math.Round(value), 0, 500);
                changed |= count != row.Item.Count;
                row.Item.Count = count;
            }
            else
            {
                changed |= Math.Abs(row.Item.SharePercent - value) > 1e-9;
                row.Item.SharePercent = value;
            }
        }

        string text = SpacingBox.Text.Trim();
        double spacing = 0;
        if (text.Length == 0 || (Num.TryParse(text, out spacing) && spacing >= 0.05 && spacing <= 30))
        {
            changed |= Math.Abs(row.Item.SpacingM - spacing) > 1e-9;
            row.Item.SpacingM = spacing;
        }

        return changed;
    }

    private void Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            Recompute();
        }
    }

    private void Reseed_Click(object sender, RoutedEventArgs e)
    {
        _seed = (_seed * 6364136223846793005L + 1442695040888963407L) & 0xFFFFFF;
        Recompute();
    }

    // ------------------------------------------------------------------ plano e prévia

    private void Recompute()
    {
        if (!IsInitialized || _loading)
        {
            return;
        }

        CompositionLayout layout = SelectedLayout;
        Plan = CompositionPlanner.Plan(_polygon, _rows.Select(r => (r.Item, r.Species)).ToList(), layout, layout == CompositionLayout.Encostado ? _back : null, _seed);
        foreach (ItemRow row in _rows)
        {
            row.Planned = Plan.Groups.Where(g => ReferenceEquals(g.Item, row.Item)).Sum(g => g.Points.Count);
            row.IsBase = ReferenceEquals(Plan.BaseItem, row.Item);
        }

        ItemsGrid.Items.Refresh();
        var areas = new List<PreviewArea>();
        if (Plan.BaseSpecies is Species baseSpecies)
        {
            Rgb c = VegetationPalette.Foliage(baseSpecies, _settings.Colors);
            areas.Add(new PreviewArea(_polygon.Rings, Color.FromRgb(c.R, c.G, c.B)));
        }

        var plants = new List<PreviewPlant>();
        foreach (PlannedGroup group in Plan.Groups)
        {
            Rgb c = Rgb.TryParse(group.Species.FlowerColor, out Rgb flower) && group.Item.Role == CompositionRole.Frente
                ? flower
                : VegetationPalette.Foliage(group.Species, _settings.Colors);
            double radius = group.Item.Role == CompositionRole.Destaque ? Math.Max(0.15, group.Species.CrownDiameterM / 2) : group.SpacingM / 2;
            plants.AddRange(group.Points.Select(p => new PreviewPlant(p, radius, Color.FromRgb(c.R, c.G, c.B))));
        }

        Preview.Show(_polygon.Rings, areas, plants, layout == CompositionLayout.Encostado ? _back : null,
            layout == CompositionLayout.Ilha ? "ilha: fundo no centro" : "encostado");

        int total = Plan.PlantCount;
        SummaryText.Text = _rows.Count == 0
            ? "Adicione as espécies do canteiro: destaques (árvores, palmeiras), faixas de fundo, meio e frente, e a forração de base."
            : $"{total} exemplar(es) de {Plan.Groups.Count(g => g.Points.Count > 0)} espécie(s)"
              + (Plan.BaseSpecies is Species b ? $" + forração de base {b.PopularName} em {Plan.AreaM2.ToString("0.0", Num.PtBr)} m²" : string.Empty) + ".";
        WarningText.Text = Plan.Warnings.Count == 0 ? string.Empty : "Atenção: " + string.Join(" ", Plan.Warnings);
        OkButton.IsEnabled = total > 0 || Plan.BaseSpecies is not null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is ItemRow row && _rows.Contains(row) && ReadFields(row))
        {
            Recompute();
        }

        if (Plan is null || (Plan.PlantCount == 0 && Plan.BaseSpecies is null))
        {
            return;
        }

        DialogResult = true;
    }

    /// <summary>Linha da receita na grade.</summary>
    public sealed class ItemRow
    {
        public ItemRow(CompositionItem item, Species species, ViridianSettings settings)
        {
            Item = item;
            Species = species;
            Rgb c = VegetationPalette.Foliage(species, settings.Colors);
            var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            brush.Freeze();
            Swatch = brush;
        }

        public CompositionItem Item { get; }

        public Species Species { get; }

        public Brush Swatch { get; }

        public int Planned { get; set; }

        public bool IsBase { get; set; }

        public string Name => Species.DisplayName;

        public string RoleText => RoleName(Item.Role);

        public string AmountText => Item.Role switch
        {
            CompositionRole.Destaque => Item.Count + " un.",
            CompositionRole.Base => "toda a área",
            _ => Num.Format(Item.SharePercent, "0.#") + "%",
        };

        public string SpacingText => Item.Role == CompositionRole.Base ? string.Empty : Num.Format(CompositionPlanner.Spacing(Item, Species), "0.##");

        public string CountText => Item.Role == CompositionRole.Base ? (IsBase ? "hachura" : "—") : Planned.ToString(Num.PtBr);
    }
}
