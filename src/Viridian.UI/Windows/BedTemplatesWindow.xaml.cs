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
using Viridian.Library;
using Viridian.UI.Controls;

namespace Viridian.UI.Windows;

/// <summary>Escolha de um desenho de canteiro do escritório para inserir no projeto, com prévia e rotação.</summary>
public partial class BedTemplatesWindow : ViridianWindow
{
    private readonly CatalogService _catalog;
    private readonly ViridianSettings _settings;
    private readonly BedTemplateLibrary _templates;

    public BedTemplatesWindow(CatalogService catalog, ViridianSettings settings, BedTemplateLibrary templates)
    {
        _catalog = catalog;
        _settings = settings;
        _templates = templates;
        InitializeComponent();
        FileText.Text = "Biblioteca: " + templates.File;
        FileText.ToolTip = templates.File;
        Refresh(null);
    }

    public BedTemplate? Selected => TemplatesGrid.SelectedItem as BedTemplate;

    public double AngleDeg => Num.TryParse(AngleBox.Text, out double a) && double.IsFinite(a) ? a : 0;

    private void Refresh(string? selectId)
    {
        List<BedTemplate> drawings = _templates.Templates.Where(t => t.IsDrawing).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        TemplatesGrid.ItemsSource = drawings;
        TemplatesGrid.SelectedItem = drawings.FirstOrDefault(t => t.Id == selectId) ?? drawings.FirstOrDefault();
        if (drawings.Count == 0)
        {
            SummaryText.Text = "Nenhum desenho de canteiro na biblioteca. Use Salvar Canteiro em um canteiro pronto para criar o primeiro.";
        }

        ShowSelected();
    }

    private void ShowSelected()
    {
        BedTemplate? template = Selected;
        OkButton.IsEnabled = DeleteButton.IsEnabled = template is not null;
        if (template?.Drawing is not TemplateDrawing drawing)
        {
            Preview.Show(Array.Empty<IReadOnlyList<Point2>>(), Array.Empty<PreviewArea>(), Array.Empty<PreviewPlant>());
            SpeciesText.Text = string.Empty;
            return;
        }

        SummaryText.Text = template.Name + " — " + template.Summary + (template.Description.Length > 0 ? "\n" + template.Description : string.Empty);
        double angle = AngleDeg * Math.PI / 180;
        IReadOnlyList<Point2> Turn(IEnumerable<Point2> ring) => ring.Select(p => TemplateDrawing.Place(p, Point2.Zero, angle)).ToList();

        var missing = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var areas = new List<PreviewArea>();
        foreach (TemplateMassing m in drawing.Massings)
        {
            Species? s = BedTemplateLibrary.Resolve(_catalog, m.SpeciesId, m.ScientificName);
            if (s is null)
            {
                missing.Add(m.PopularName);
            }

            Rgb c = s is null ? new Rgb(200, 200, 200) : VegetationPalette.MassingBackground(s, _settings.Colors);
            areas.Add(new PreviewArea(m.Rings.Select(Turn).ToList(), Color.FromRgb(c.R, c.G, c.B)));
        }

        var plants = new List<PreviewPlant>();
        foreach (TemplatePlant p in drawing.Plants)
        {
            Species? s = BedTemplateLibrary.Resolve(_catalog, p.SpeciesId, p.ScientificName);
            if (s is null)
            {
                missing.Add(p.PopularName);
                continue;
            }

            Rgb c = VegetationPalette.Foliage(s, _settings.Colors);
            plants.Add(new PreviewPlant(TemplateDrawing.Place(new Point2(p.X, p.Y), Point2.Zero, angle), Math.Max(0.1, s.CrownDiameterM / 2), Color.FromRgb(c.R, c.G, c.B)));
        }

        Preview.Show(drawing.Outline.Select(Turn).ToList(), areas, plants, null, "+ centro de inserção no meio");
        SpeciesText.Text = missing.Count == 0
            ? "Todas as espécies estão no catálogo."
            : "Fora do catálogo deste computador (não serão inseridas): " + string.Join(", ", missing);
    }

    private void TemplatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void AngleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            ShowSelected();
        }
    }

    private void TemplatesGrid_DoubleClick(object sender, MouseButtonEventArgs e) => Ok_Click(sender, e);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not BedTemplate template || !Confirm($"Excluir o modelo \"{template.Name}\" da biblioteca do escritório?"))
        {
            return;
        }

        try
        {
            _templates.Delete(template.Id);
            Refresh(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível excluir o modelo:\n" + ex.Message);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null)
        {
            return;
        }

        if (!Num.TryParse(AngleBox.Text, out double angle) || !double.IsFinite(angle))
        {
            Warn("Informe a rotação em graus (por exemplo 0, 90 ou -45).");
            AngleBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
