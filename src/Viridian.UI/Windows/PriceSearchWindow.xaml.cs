using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Viridian.Core.Costs;

namespace Viridian.UI.Windows;

/// <summary>Busca de um item na tabela de preços importada, para vincular à espécie.</summary>
public partial class PriceSearchWindow : ViridianWindow
{
    private const int MaxShown = 300;
    private readonly PriceTable _table;

    public PriceSearchWindow(PriceTable table, string header, string initialSearch)
    {
        _table = table;
        InitializeComponent();
        HeaderText.Text = header;
        SearchBox.Text = initialSearch;
        ItemsGrid.Language = Language;
        Refresh();
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        };
    }

    public PriceItem? Selected => ItemsGrid.SelectedItem as PriceItem;

    private void Refresh()
    {
        IReadOnlyList<PriceItem> items = _table.Search(SearchBox.Text, MaxShown + 1);
        ItemsGrid.ItemsSource = items.Take(MaxShown).ToList();
        CountText.Text = items.Count > MaxShown
            ? $"Mostrando os primeiros {MaxShown} itens; refine a pesquisa."
            : $"{items.Count} item(ns) · {_table.Name}";
        if (items.Count > 0)
        {
            ItemsGrid.SelectedIndex = 0;
        }

        OkButton.IsEnabled = Selected is not null;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => OkButton.IsEnabled = Selected is not null;

    private void ItemsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null)
        {
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not null)
        {
            DialogResult = true;
        }
    }
}
