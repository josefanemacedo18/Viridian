using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Viridian.UI.Windows;

/// <summary>Item da lista de vistas exportáveis.</summary>
public sealed class ExportViewItem : INotifyPropertyChanged
{
    private bool _selected;

    public ExportViewItem(long id, string label, bool isSheet, bool selected)
    {
        Id = id;
        Label = label;
        IsSheet = isSheet;
        _selected = selected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; }

    public string Label { get; }

    public bool IsSheet { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }
}

/// <summary>Opções escolhidas na exportação.</summary>
public sealed class CadExportOptions
{
    public string Folder { get; set; } = string.Empty;

    public IReadOnlyList<long> ViewIds { get; set; } = Array.Empty<long>();

    /// <summary>Ano da versão do DWG (2018, 2013, 2010 ou 2007).</summary>
    public int Version { get; set; } = 2018;

    public bool Meters { get; set; } = true;

    public bool Merged { get; set; } = true;

    public bool SharedCoordinates { get; set; }

    public bool Library { get; set; } = true;

    public bool LibraryWholeCatalog { get; set; }

    public bool LibraryFill { get; set; } = true;
}

/// <summary>Exportação para CAD: DWG das vistas e pranchas e biblioteca de blocos DXF.</summary>
public partial class CadExportWindow : ViridianWindow
{
    private readonly List<ExportViewItem> _items;

    public CadExportWindow(IReadOnlyList<ExportViewItem> items, string defaultFolder)
    {
        _items = items.ToList();
        InitializeComponent();
        ViewList.ItemsSource = _items;
        FolderBox.Text = defaultFolder;
        foreach (int version in new[] { 2018, 2013, 2010, 2007 })
        {
            VersionBox.Items.Add(new Choice<int>("AutoCAD " + version, version));
        }

        VersionBox.SelectedIndex = 0;
        UnitBox.Items.Add(new Choice<bool>("Metros", true));
        UnitBox.Items.Add(new Choice<bool>("Milímetros", false));
        UnitBox.SelectedIndex = 0;
    }

    public CadExportOptions Options { get; } = new();

    private void SelectSheets_Click(object sender, RoutedEventArgs e)
    {
        foreach (ExportViewItem item in _items)
        {
            item.Selected = item.IsSheet || item.Selected;
        }
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (ExportViewItem item in _items)
        {
            item.Selected = false;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Pasta para os arquivos de CAD" };
        if (Directory.Exists(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string folder = FolderBox.Text.Trim();
        if (folder.Length == 0 || !Path.IsPathRooted(folder))
        {
            Warn("Escolha a pasta de destino (caminho completo).");
            return;
        }

        List<long> ids = _items.Where(i => i.Selected).Select(i => i.Id).ToList();
        if (ids.Count == 0 && LibraryBox.IsChecked != true)
        {
            Warn("Marque ao menos uma vista ou prancha, ou a biblioteca de blocos.");
            return;
        }

        Options.Folder = folder;
        Options.ViewIds = ids;
        Options.Version = (VersionBox.SelectedItem as Choice<int>)?.Value ?? 2018;
        Options.Meters = (UnitBox.SelectedItem as Choice<bool>)?.Value ?? true;
        Options.Merged = MergedBox.IsChecked == true;
        Options.SharedCoordinates = SharedBox.IsChecked == true;
        Options.Library = LibraryBox.IsChecked == true;
        Options.LibraryWholeCatalog = CatalogRadio.IsChecked == true;
        Options.LibraryFill = FillBox.IsChecked == true;
        DialogResult = true;
    }
}
