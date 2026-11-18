using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Export;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.UI.Windows;

public enum PavingAction
{
    None,
    Apply,
    CreateType,
    CreateRegion,
    Detail,
}

/// <summary>Dados do projeto que a janela de pisos usa (nada disso existe sem projeto aberto).</summary>
public sealed class PavingWindowOptions
{
    public bool HasProject { get; init; }

    public int SelectedFloors { get; init; }

    public string ProjectTitle { get; init; } = string.Empty;

    public string? InitialId { get; init; }

    /// <summary>Lê as áreas dos pisos do projeto por tipo da biblioteca.</summary>
    public Func<IReadOnlyList<PavingType>, IReadOnlyList<PavingQuantity>>? Quantities { get; init; }
}

/// <summary>Gerador de pisos: biblioteca, editor com prévia em escala real e ações no Revit.</summary>
public partial class PavingWindow : ViridianWindow
{
    private readonly PavingLibrary _library;
    private readonly PavingWindowOptions _options;
    private readonly HashSet<string> _seedIds = PavingDefaults.Create().Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private ObservableCollection<PavingLayer> _layers = new();
    private PavingType? _current;
    private bool _loading;
    private bool _dirty;
    private bool _switching;

    public PavingWindow(PavingLibrary library, PavingWindowOptions options)
    {
        _library = library;
        _options = options;
        InitializeComponent();
        LayerGrid.Language = Language;
        QuantityGrid.Language = Language;

        CategoryFilter.Items.Add(new Choice<PavingCategory?>("Todas as categorias", null));
        foreach (PavingCategory c in Enum.GetValues<PavingCategory>())
        {
            CategoryFilter.Items.Add(new Choice<PavingCategory?>(PavingLabels.Of(c), c));
            CategoryBox.Items.Add(new Choice<PavingCategory>(PavingLabels.Of(c), c));
        }

        CategoryFilter.SelectedIndex = 0;
        foreach (LayingPattern p in Enum.GetValues<LayingPattern>())
        {
            PatternBox.Items.Add(new Choice<LayingPattern>(PavingLabels.Of(p), p));
        }

        LayerKindColumn.ItemsSource = Enum.GetValues<PavingLayerKind>().Select(k => new Choice<PavingLayerKind>(PavingLabels.Of(k), k)).ToList();

        ApplyButton.IsEnabled = options.HasProject && options.SelectedFloors > 0;
        ApplyButton.Content = options.SelectedFloors > 0 ? $"_Aplicar aos {options.SelectedFloors} piso(s) selecionado(s)" : "_Aplicar aos pisos selecionados";
        ApplyButton.ToolTip = options.SelectedFloors > 0 ? "Troca o tipo dos pisos selecionados pelo piso desta biblioteca" : "Selecione pisos no Revit antes de abrir o gerador";
        TypeButton.IsEnabled = RegionButton.IsEnabled = DetailButton.IsEnabled = options.HasProject;
        ExportButton.IsEnabled = options.HasProject;

        RefreshList(options.InitialId);
        RefreshQuantities();
    }

    public PavingAction Action { get; private set; } = PavingAction.None;

    /// <summary>Piso escolhido para a ação (já gravado na biblioteca).</summary>
    public PavingType? Result { get; private set; }

    // ------------------------------------------------------------------ lista

    private void RefreshList(string? selectId)
    {
        string search = SearchBox.Text.Trim();
        PavingCategory? category = (CategoryFilter.SelectedItem as Choice<PavingCategory?>)?.Value;
        var items = _library.Types
            .Where(t => category is null || t.Category == category)
            .Where(t => search.Length == 0 || t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || t.Material.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(t => t.Category).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new Choice<PavingType>($"{t.Name}   ·   {PavingLabels.Of(t.Category)}", t))
            .ToList();
        _switching = true;
        TypeList.ItemsSource = items;
        TypeList.DisplayMemberPath = nameof(Choice<PavingType>.Label);
        Choice<PavingType>? select = items.FirstOrDefault(i => string.Equals(i.Value.Id, selectId, StringComparison.OrdinalIgnoreCase))
                                     ?? (_dirty ? null : items.FirstOrDefault());
        TypeList.SelectedItem = select;
        _switching = false;

        // Com edições não salvas, o filtro só muda a lista: o editor continua com o piso em edição.
        if (select is not null && !_dirty)
        {
            Load(select.Value.Clone());
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshList(_current?.Id);
        }
    }

    private void TypeList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_switching || TypeList.SelectedItem is not Choice<PavingType> choice)
        {
            return;
        }

        if (_dirty && _current is not null && !string.Equals(_current.Id, choice.Value.Id, StringComparison.OrdinalIgnoreCase)
            && !Confirm($"Descartar as alterações em \"{_current.Name}\"?"))
        {
            _switching = true;
            TypeList.SelectedItem = (TypeList.ItemsSource as IEnumerable<Choice<PavingType>>)?.FirstOrDefault(i => i.Value.Id == _current.Id);
            _switching = false;
            return;
        }

        Load(choice.Value.Clone());
    }

    // ------------------------------------------------------------------ editor

    private void Load(PavingType type)
    {
        _loading = true;
        _current = type;
        NameBox.Text = type.Name;
        CategoryBox.SelectedItem = CategoryBox.Items.Cast<Choice<PavingCategory>>().FirstOrDefault(c => c.Value == type.Category);
        PatternBox.SelectedItem = PatternBox.Items.Cast<Choice<LayingPattern>>().FirstOrDefault(c => c.Value == type.Pattern);
        MaterialBox.Text = type.Material;
        LengthBox.Text = Num.Format(type.LengthMm);
        WidthBox.Text = Num.Format(type.WidthMm);
        ThicknessBox.Text = Num.Format(type.ThicknessMm);
        JointBox.Text = Num.Format(type.JointMm);
        AngleBox.Text = Num.Format(type.AngleDeg);
        SlopeBox.Text = Num.Format(type.SlopePercent);
        WasteBox.Text = Num.Format(type.WastePercent);
        CastBox.IsChecked = type.CastInPlace;
        ColorBox.Text = type.Color;
        JointColorBox.Text = type.JointColor;
        NotesBox.Text = type.Notes;
        ReferencesBox.Text = type.References;
        PriceCodeBox.Text = type.PriceItemCode;
        ServiceCodeBox.Text = type.ServiceItemCode;
        _layers = new ObservableCollection<PavingLayer>(type.Layers.Select(l => l.Clone()));
        _layers.CollectionChanged += (_, _) => MarkDirty();
        LayerGrid.ItemsSource = _layers;
        RestoreButton.IsEnabled = _seedIds.Contains(type.Id);
        _loading = false;
        _dirty = false;
        UpdatePreview();
    }

    /// <summary>Lê os campos; devolve nulo e mostra os problemas quando algum valor é inválido.</summary>
    private PavingType? Collect(bool showProblems)
    {
        if (_current is null)
        {
            return null;
        }

        var problems = new List<string>();
        double Number(TextBox box, string label)
        {
            if (Num.TryParse(box.Text, out double value))
            {
                return value;
            }

            problems.Add($"Valor inválido em \"{label}\".");
            return double.NaN;
        }

        LayerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        PavingType t = _current.Clone();
        t.Name = NameBox.Text.Trim();
        t.Category = (CategoryBox.SelectedItem as Choice<PavingCategory>)?.Value ?? t.Category;
        t.Pattern = (PatternBox.SelectedItem as Choice<LayingPattern>)?.Value ?? t.Pattern;
        t.Material = MaterialBox.Text.Trim();
        t.LengthMm = Number(LengthBox, "Comprimento");
        t.WidthMm = Number(WidthBox, "Largura");
        t.ThicknessMm = Number(ThicknessBox, "Espessura");
        t.JointMm = Number(JointBox, "Junta");
        t.AngleDeg = AngleBox.Text.Trim().Length == 0 ? 0 : Number(AngleBox, "Rotação");
        t.SlopePercent = Number(SlopeBox, "Caimento");
        t.WastePercent = Number(WasteBox, "Perda");
        t.CastInPlace = CastBox.IsChecked == true;
        // Cor inválida fica como foi digitada para a validação apontar (em vez de virar preto).
        t.Color = Rgb.TryParse(ColorBox.Text.Trim(), out _) ? ColorHex.Normalize(ColorBox.Text.Trim()) : ColorBox.Text.Trim();
        t.JointColor = Rgb.TryParse(JointColorBox.Text.Trim(), out _) ? ColorHex.Normalize(JointColorBox.Text.Trim()) : JointColorBox.Text.Trim();
        t.Notes = NotesBox.Text.Trim();
        t.References = ReferencesBox.Text.Trim();
        t.PriceItemCode = PriceCodeBox.Text.Trim();
        t.ServiceItemCode = ServiceCodeBox.Text.Trim();
        t.Layers = _layers.Select(l => l.Clone()).ToList();
        if (problems.Count == 0)
        {
            problems.AddRange(t.Validate());
        }

        ProblemText.Text = string.Join("\n", problems);
        if (problems.Count > 0)
        {
            if (showProblems)
            {
                Warn(string.Join("\n", problems));
            }

            return null;
        }

        return t;
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading || _current is null)
        {
            return;
        }

        _dirty = true;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        SetSwatch(ColorSwatch, ColorBox.Text);
        SetSwatch(JointSwatch, JointColorBox.Text);
        PavingType? t = Collect(false);
        Swatch.Show(t);
        SwatchText.Text = t is null
            ? "Corrija os valores para ver a prévia."
            : $"Amostra de 2,0 m de largura · {PavingDetail.Size(t)} · {PavingLabels.Of(t.Pattern)}\nEspessura total: {PavingDetail.Cm(t.TotalThicknessMm)}"
              + (_dirty ? "   (alterações não salvas)" : string.Empty);
    }

    private static void SetSwatch(Border border, string text)
    {
        border.Background = Rgb.TryParse(text.Trim(), out Rgb c) ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;
    }

    // ------------------------------------------------------------------ camadas

    private void LayerGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.EditingElement is TextBox box && e.Column.DisplayIndex >= 2)
        {
            if (box.Text.Trim().Length == 0)
            {
                box.Text = "0";
            }
            else if (!Num.TryParse(box.Text, out double value) || value < 0)
            {
                e.Cancel = true;
                Warn("Digite a medida em milímetros (0 ou mais).");
                return;
            }
            else
            {
                box.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        Dispatcher.BeginInvoke(new Action(MarkDirty));
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        LayerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _layers.Add(new PavingLayer { Name = "Nova camada", Kind = PavingLayerKind.Base, ThicknessMm = 50 });
        LayerGrid.SelectedIndex = _layers.Count - 1;
    }

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        if (LayerGrid.SelectedItem is PavingLayer layer)
        {
            LayerGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _layers.Remove(layer);
        }
    }

    private void UpLayer_Click(object sender, RoutedEventArgs e) => MoveLayer(-1);

    private void DownLayer_Click(object sender, RoutedEventArgs e) => MoveLayer(1);

    private void MoveLayer(int delta)
    {
        if (LayerGrid.SelectedItem is not PavingLayer layer)
        {
            return;
        }

        LayerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        int index = _layers.IndexOf(layer);
        int target = index + delta;
        if (target >= 0 && target < _layers.Count)
        {
            _layers.Move(index, target);
            LayerGrid.SelectedIndex = target;
        }
    }

    // ------------------------------------------------------------------ biblioteca

    private bool SaveCurrent()
    {
        PavingType? t = Collect(true);
        if (t is null)
        {
            return false;
        }

        if (_library.Types.Any(o => !string.Equals(o.Id, t.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(o.Name, t.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            Warn($"Já existe um piso chamado \"{t.Name}\". Use outro nome.");
            return false;
        }

        t.IsUserDefined = true; // editado pelo escritório: não é sobrescrito por versões novas do plugin
        try
        {
            _library.Save(t);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Warn("Não foi possível gravar a biblioteca de pisos:\n" + ex.Message);
            return false;
        }

        _dirty = false;
        _current = t;
        RefreshList(t.Id);
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SaveCurrent())
        {
            RefreshQuantities();
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !Confirm("Descartar as alterações do piso atual?"))
        {
            return;
        }

        var t = new PavingType
        {
            Name = UniqueName("Novo piso"),
            Category = _current?.Category ?? PavingCategory.Cimenticio,
            Material = string.Empty,
            Layers = _current?.Layers.Select(l => l.Clone()).ToList() ?? new List<PavingLayer>(),
            IsUserDefined = true,
        };
        Load(t);
        _dirty = true;
        UpdatePreview();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        PavingType? t = Collect(true);
        if (t is null)
        {
            return;
        }

        t.Id = Guid.NewGuid().ToString("N");
        t.Name = UniqueName(t.Name + " (cópia)");
        t.IsUserDefined = true;
        Load(t);
        _dirty = true;
        UpdatePreview();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !Confirm($"Excluir \"{_current.Name}\" da biblioteca? Pisos do Revit que já usam o tipo não são alterados."))
        {
            return;
        }

        try
        {
            _library.Delete(_current.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível gravar a biblioteca de pisos:\n" + ex.Message);
            return;
        }

        _dirty = false;
        _current = null;
        RefreshList(null);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !_seedIds.Contains(_current.Id) || !Confirm($"Voltar \"{_current.Name}\" aos valores padrão do Viridian?"))
        {
            return;
        }

        try
        {
            PavingType? restored = _library.Restore(_current.Id);
            _dirty = false;
            RefreshList(restored?.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Warn("Não foi possível gravar a biblioteca de pisos:\n" + ex.Message);
        }
    }

    private string UniqueName(string name)
    {
        string candidate = name;
        for (int i = 2; _library.Types.Any(t => string.Equals(t.Name, candidate, StringComparison.CurrentCultureIgnoreCase)); i++)
        {
            candidate = $"{name} {i}";
        }

        return candidate;
    }

    // ------------------------------------------------------------------ projeto

    private void RefreshQuantities()
    {
        if (!_options.HasProject || _options.Quantities is null)
        {
            QuantityGrid.ItemsSource = null;
            return;
        }

        try
        {
            QuantityGrid.ItemsSource = _options.Quantities(_library.Types);
        }
        catch (Exception ex)
        {
            ViridianLog.Warn("Quantitativo de pisos", ex);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (QuantityGrid.ItemsSource is not IReadOnlyList<PavingQuantity> rows || rows.Count == 0)
        {
            Inform("Nenhum piso do projeto usa tipos da biblioteca ainda. Crie o tipo de piso e aplique-o aos pisos do Revit.");
            return;
        }

        var dialog = new SaveFileDialog { Filter = "Planilha do Excel (*.xlsx)|*.xlsx", FileName = "Quantitativo de pisos.xlsx" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            XlsxWriter.Write(dialog.FileName, new[] { PavingSheets.Build(rows, _options.ProjectTitle, DateTime.Now) });
            if (Confirm("Planilha exportada:\n" + dialog.FileName + "\n\nAbrir agora?"))
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível gravar a planilha:\n" + ex.Message);
        }
    }

    private void Finish(PavingAction action)
    {
        if (_dirty && !SaveCurrent())
        {
            return;
        }

        PavingType? t = _current is null ? null : _library.Find(_current.Id);
        if (t is null)
        {
            Warn("Escolha um piso da biblioteca.");
            return;
        }

        Result = t.Clone();
        Action = action;
        DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Finish(PavingAction.Apply);

    private void Type_Click(object sender, RoutedEventArgs e) => Finish(PavingAction.CreateType);

    private void Region_Click(object sender, RoutedEventArgs e) => Finish(PavingAction.CreateRegion);

    private void Detail_Click(object sender, RoutedEventArgs e) => Finish(PavingAction.Detail);
}
