using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Viridian.Core.Colors;
using Viridian.Core.Composition;
using Viridian.Core.Model;

namespace Viridian.UI.Windows;

/// <summary>O que fazer na vista depois de fechar o painel de floração.</summary>
public enum FloweringPanelAction
{
    Nenhuma,
    Isolar,
    Selecionar,
}

/// <summary>
/// Painel de consulta da floração na planta: por maciço e por espécie isolada da vista, os meses floridos nas cores
/// das flores e os meses sem flor. Pode isolar temporariamente na vista o que floresce em um mês.
/// </summary>
public partial class FloweringPanelWindow : ViridianWindow
{
    private static readonly string[] MonthNames =
        { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro" };

    public FloweringPanelWindow(string viewName, IReadOnlyList<FloweringGroup> groups, int initialMonth)
    {
        InitializeComponent();
        HeaderText.Text = "Floração na planta — " + viewName;
        MonthHeader.ItemsSource = MonthSet.Abbreviations;
        GroupsGrid.ItemsSource = groups.Select(g => new FloweringRow(g)).ToList();
        for (int m = 1; m <= 12; m++)
        {
            MonthBox.Items.Add(new Choice<int>(MonthNames[m - 1], m));
        }

        MonthBox.SelectedIndex = Math.Clamp(initialMonth, 1, 12) - 1;
        GroupsGrid.SelectedIndex = 0;
        UpdateMonth();
    }

    /// <summary>Ação escolhida.</summary>
    public FloweringPanelAction Action { get; private set; }

    /// <summary>Elementos a isolar ou selecionar.</summary>
    public IReadOnlyList<long> ElementIds { get; private set; } = Array.Empty<long>();

    public int Month => (MonthBox.SelectedItem as Choice<int>)?.Value ?? 1;

    private FloweringGroup? SelectedGroup => (GroupsGrid.SelectedItem as FloweringRow)?.Group;

    private void UpdateMonth()
    {
        if (!IsInitialized)
        {
            return;
        }

        FloweringGroup? group = SelectedGroup;
        if (group is null)
        {
            MonthText.Text = string.Empty;
            IsolateButton.IsEnabled = SelectButton.IsEnabled = false;
            return;
        }

        IReadOnlyList<Species> flowering = group.FloweringIn(Month);
        string month = MonthNames[Month - 1];
        MonthText.Text = flowering.Count == 0
            ? $"{group.Title}: nada florido em {month}."
            : $"Em {month}: " + string.Join(", ", flowering.Take(8).Select(s => s.PopularName)) + (flowering.Count > 8 ? $" e mais {flowering.Count - 8}" : string.Empty) + ".";
        bool any = group.ElementsFloweringIn(Month).Count > 0;
        IsolateButton.IsEnabled = SelectButton.IsEnabled = any;
    }

    private void GroupsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMonth();

    private void MonthBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateMonth();

    private void GroupsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedGroup is FloweringGroup group && group.AllElements.Count > 0)
        {
            Finish(FloweringPanelAction.Selecionar, group.AllElements);
        }
    }

    private void Isolate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup is FloweringGroup group)
        {
            Finish(FloweringPanelAction.Isolar, group.ElementsFloweringIn(Month));
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGroup is FloweringGroup group)
        {
            Finish(FloweringPanelAction.Selecionar, group.ElementsFloweringIn(Month));
        }
    }

    private void Finish(FloweringPanelAction action, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        Action = action;
        ElementIds = ids;
        DialogResult = true;
    }

    /// <summary>Linha da grade: título, detalhe, 12 células e os meses sem flor.</summary>
    public sealed class FloweringRow
    {
        private static readonly Brush Discreet = Frozen(new SolidColorBrush(Color.FromRgb(0xDD, 0xDF, 0xD8)));
        private static readonly Brush Empty = Frozen(new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0)));
        private static readonly Brush Line = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)));
        private static readonly Brush NoLine = Frozen(new SolidColorBrush(Colors.Transparent));

        public FloweringRow(FloweringGroup group)
        {
            Group = group;
            Cells = Enumerable.Range(1, 12).Select(m => Cell(group, m)).ToList();
        }

        public FloweringGroup Group { get; }

        public string Title => Group.Title;

        public string Detail => Group.Detail;

        public FontWeight TitleWeight => Group.IsSummary ? FontWeights.SemiBold : FontWeights.Normal;

        public string GapText => Group.GapText;

        public IReadOnlyList<MonthCell> Cells { get; }

        private static MonthCell Cell(FloweringGroup group, int month)
        {
            IReadOnlyList<string> colors = group.ColorsIn(month);
            IReadOnlyList<Species> flowering = group.FloweringIn(month);
            string name = MonthSet.Abbreviations[month - 1];
            if (colors.Count > 0)
            {
                return new MonthCell(Fill(colors), Line, new Thickness(0.6),
                    name + ": " + string.Join(", ", flowering.Take(6).Select(s => s.PopularName)) + (flowering.Count > 6 ? "…" : string.Empty));
            }

            return flowering.Count > 0
                ? new MonthCell(Discreet, NoLine, new Thickness(0), name + ": floração discreta (" + string.Join(", ", flowering.Take(4).Select(s => s.PopularName)) + ")")
                : new MonthCell(Empty, NoLine, new Thickness(0), name + ": sem flor");
        }

        /// <summary>Uma cor, ou faixas verticais quando várias espécies florescem no mês.</summary>
        private static Brush Fill(IReadOnlyList<string> hexes)
        {
            var colors = hexes.Take(4)
                .Select(h => Rgb.TryParse(h, out Rgb c) ? Color.FromRgb(c.R, c.G, c.B) : Color.FromRgb(0xF2, 0xC1, 0x4E))
                .ToList();
            if (colors.Count == 1)
            {
                return Frozen(new SolidColorBrush(colors[0]));
            }

            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i < colors.Count; i++)
            {
                double a = (double)i / colors.Count, b = (double)(i + 1) / colors.Count;
                brush.GradientStops.Add(new GradientStop(colors[i], a));
                brush.GradientStops.Add(new GradientStop(colors[i], b));
            }

            return Frozen(brush);
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }

    public sealed record MonthCell(Brush Fill, Brush Edge, Thickness EdgeThickness, string Tip);
}
