using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Viridian.Core.Colors;
using Viridian.Core.Config;
using Viridian.Core.Paving;
using Viridian.Library;

namespace Viridian.UI.Windows;

public enum StoneAction
{
    None,
    Apply,
    FillRegion,
    Border,
    CreateType,
    CreateRegion,
    Detail,
}

/// <summary>Biblioteca de pedras e coberturas soltas: edição, cálculo de compra e ações no Revit por uso.</summary>
public partial class StoneWindow : ViridianWindow
{
    private readonly LooseMaterialLibrary _library;
    private readonly bool _hasProject;
    private readonly int _selectedFloors;
    private readonly HashSet<string> _seedIds = LooseMaterialDefaults.Create().Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<LooseUse, CheckBox> _useBoxes = new();
    private LooseMaterial? _current;
    private bool _loading;
    private bool _dirty;
    private bool _switching;

    public StoneWindow(LooseMaterialLibrary library, bool hasProject, int selectedFloors, string? initialId = null)
    {
        _library = library;
        _hasProject = hasProject;
        _selectedFloors = selectedFloors;
        InitializeComponent();

        KindFilter.Items.Add(new Choice<LooseKind?>("Todos os tipos", null));
        foreach (LooseKind k in Enum.GetValues<LooseKind>())
        {
            KindFilter.Items.Add(new Choice<LooseKind?>(LooseLabels.Of(k), k));
            KindBox.Items.Add(new Choice<LooseKind>(LooseLabels.Of(k), k));
        }

        UseFilter.Items.Add(new Choice<LooseUse>("Todos os usos", LooseUse.Nenhum));
        foreach (LooseUse u in LooseLabels.AllUses)
        {
            UseFilter.Items.Add(new Choice<LooseUse>(LooseLabels.Of(u), u));
            var box = new CheckBox { Content = LooseLabels.Of(u), Margin = new Thickness(0, 0, 14, 4) };
            box.Checked += (_, _) => UsesChanged();
            box.Unchecked += (_, _) => UsesChanged();
            _useBoxes[u] = box;
            UsesPanel.Children.Add(box);
        }

        foreach (LooseOrigin o in Enum.GetValues<LooseOrigin>())
        {
            OriginBox.Items.Add(new Choice<LooseOrigin>(LooseLabels.Of(o), o));
        }

        BorderSideBox.Items.Add(new Choice<StripSide>("Para fora do caminho (à esquerda da linha)", StripSide.Fora));
        BorderSideBox.Items.Add(new Choice<StripSide>("Para dentro do caminho (à direita da linha)", StripSide.Dentro));
        BorderSideBox.Items.Add(new Choice<StripSide>("Centralizada no contorno ou na linha", StripSide.Centro));
        BorderSideBox.SelectedIndex = 0;
        KindFilter.SelectedIndex = 0;
        UseFilter.SelectedIndex = 0;

        ApplyButton.IsEnabled = hasProject && selectedFloors > 0;
        ApplyButton.Content = selectedFloors > 0 ? $"_Aplicar aos {selectedFloors} piso(s) selecionado(s)" : "_Aplicar aos pisos selecionados";
        ApplyButton.ToolTip = selectedFloors > 0 ? "Troca o tipo dos pisos selecionados por este material no uso escolhido" : "Selecione pisos no Revit antes de abrir a janela";
        FillButton.IsEnabled = BorderButton.IsEnabled = TypeButton.IsEnabled = RegionButton.IsEnabled = DetailButton.IsEnabled = hasProject;
        ProjectHint.Text = hasProject
            ? "Cobrir região e Borda de caminho criam pisos do Revit no nível da vista em planta ativa: entram no quantitativo do Gerador de pisos."
            : "Abra um projeto para levar os materiais ao Revit. A biblioteca pode ser editada sem projeto.";

        RefreshList(initialId);
    }

    public StoneAction Action { get; private set; } = StoneAction.None;

    /// <summary>Material escolhido (já gravado na biblioteca).</summary>
    public LooseMaterial? Material { get; private set; }

    /// <summary>Uso escolhido para a ação.</summary>
    public LooseUse Use { get; private set; } = LooseUse.Canteiro;

    /// <summary>Tipo de piso do material no uso escolhido.</summary>
    public PavingType? Paving { get; private set; }

    public double BorderWidthM { get; private set; } = 0.3;

    public StripSide BorderSide { get; private set; } = StripSide.Fora;

    // ------------------------------------------------------------------ lista

    private void RefreshList(string? selectId)
    {
        string search = SearchBox.Text.Trim();
        LooseKind? kind = (KindFilter.SelectedItem as Choice<LooseKind?>)?.Value;
        LooseUse use = (UseFilter.SelectedItem as Choice<LooseUse>)?.Value ?? LooseUse.Nenhum;
        var items = _library.Materials
            .Where(m => kind is null || m.Kind == kind)
            .Where(m => use == LooseUse.Nenhum || m.Uses.HasFlag(use))
            .Where(m => search.Length == 0 || m.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || m.Rock.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new Choice<LooseMaterial>($"{m.Name}   ·   {m.GrainText}", m))
            .ToList();
        _switching = true;
        MaterialList.ItemsSource = items;
        MaterialList.DisplayMemberPath = nameof(Choice<LooseMaterial>.Label);
        Choice<LooseMaterial>? select = items.FirstOrDefault(i => string.Equals(i.Value.Id, selectId, StringComparison.OrdinalIgnoreCase))
                                        ?? (_dirty ? null : items.FirstOrDefault());
        MaterialList.SelectedItem = select;
        _switching = false;
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

    private void MaterialList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_switching || MaterialList.SelectedItem is not Choice<LooseMaterial> choice)
        {
            return;
        }

        if (_dirty && _current is not null && !string.Equals(_current.Id, choice.Value.Id, StringComparison.OrdinalIgnoreCase)
            && !Confirm($"Descartar as alterações em \"{_current.Name}\"?"))
        {
            _switching = true;
            MaterialList.SelectedItem = (MaterialList.ItemsSource as IEnumerable<Choice<LooseMaterial>>)?.FirstOrDefault(i => i.Value.Id == _current.Id);
            _switching = false;
            return;
        }

        Load(choice.Value.Clone());
    }

    // ------------------------------------------------------------------ editor

    private void Load(LooseMaterial m)
    {
        _loading = true;
        _current = m;
        NameBox.Text = m.Name;
        KindBox.SelectedItem = KindBox.Items.Cast<Choice<LooseKind>>().FirstOrDefault(c => c.Value == m.Kind);
        OriginBox.SelectedItem = OriginBox.Items.Cast<Choice<LooseOrigin>>().FirstOrDefault(c => c.Value == m.Origin);
        RockBox.Text = m.Rock;
        GrainMinBox.Text = Num.Format(m.GrainMinMm, "0.#");
        GrainMaxBox.Text = Num.Format(m.GrainMaxMm, "0.#");
        ColorBox.Text = m.Color;
        GrainColorBox.Text = m.GrainColor;
        foreach ((LooseUse u, CheckBox box) in _useBoxes)
        {
            box.IsChecked = m.Uses.HasFlag(u);
        }

        LayerBox.Text = Num.Format(m.LayerMm);
        DensityBox.Text = Num.Format(m.DensityTM3);
        VoidsBox.Text = Num.Format(m.PorosityPercent);
        WasteBox.Text = Num.Format(m.WastePercent);
        BagKgBox.Text = Num.Format(m.BagKg);
        BagLitersBox.Text = Num.Format(m.BagLiters);
        PriceCodeBox.Text = m.PriceItemCode;
        NotesBox.Text = m.Notes;
        ReferencesBox.Text = m.References;
        RestoreButton.IsEnabled = _seedIds.Contains(m.Id);
        _loading = false;
        _dirty = false;
        RefreshUses(m.Uses);
        UpdatePreview();
    }

    private LooseUse CheckedUses() => _useBoxes.Where(kv => kv.Value.IsChecked == true).Aggregate(LooseUse.Nenhum, (acc, kv) => acc | kv.Key);

    private void UsesChanged()
    {
        if (_loading)
        {
            return;
        }

        RefreshUses(CheckedUses());
        MarkDirty();
    }

    /// <summary>Lista de usos para as ações: só os marcados, mantendo o escolhido quando possível.</summary>
    private void RefreshUses(LooseUse uses)
    {
        LooseUse previous = (UseBox.SelectedItem as Choice<LooseUse>)?.Value ?? LooseUse.Nenhum;
        bool loading = _loading;
        _loading = true;
        UseBox.Items.Clear();
        foreach (LooseUse u in LooseLabels.AllUses.Where(u => uses.HasFlag(u)))
        {
            UseBox.Items.Add(new Choice<LooseUse>(LooseLabels.Of(u), u));
        }

        UseBox.SelectedItem = UseBox.Items.Cast<Choice<LooseUse>>().FirstOrDefault(c => c.Value == previous) ?? UseBox.Items.Cast<Choice<LooseUse>>().FirstOrDefault();
        _loading = loading;
    }

    private LooseUse SelectedUse => (UseBox.SelectedItem as Choice<LooseUse>)?.Value ?? LooseLabels.AllUses.FirstOrDefault(u => CheckedUses().HasFlag(u));

    /// <summary>Lê os campos; devolve nulo (e mostra os problemas, se pedido) quando algum valor é inválido.</summary>
    private LooseMaterial? Collect(bool showProblems)
    {
        if (_current is null)
        {
            return null;
        }

        var problems = new List<string>();
        double Number(TextBox box, string label)
        {
            if (box.Text.Trim().Length == 0)
            {
                return 0;
            }

            if (Num.TryParse(box.Text, out double value))
            {
                return value;
            }

            problems.Add($"Valor inválido em \"{label}\".");
            return double.NaN;
        }

        LooseMaterial m = _current.Clone();
        m.Name = NameBox.Text.Trim();
        m.Kind = (KindBox.SelectedItem as Choice<LooseKind>)?.Value ?? m.Kind;
        m.Origin = (OriginBox.SelectedItem as Choice<LooseOrigin>)?.Value ?? m.Origin;
        m.Rock = RockBox.Text.Trim();
        m.GrainMinMm = Number(GrainMinBox, "Grão mínimo");
        m.GrainMaxMm = Number(GrainMaxBox, "Grão máximo");
        m.Color = Rgb.TryParse(ColorBox.Text.Trim(), out _) ? ColorHex.Normalize(ColorBox.Text.Trim()) : ColorBox.Text.Trim();
        m.GrainColor = Rgb.TryParse(GrainColorBox.Text.Trim(), out _) ? ColorHex.Normalize(GrainColorBox.Text.Trim()) : GrainColorBox.Text.Trim();
        m.Uses = CheckedUses();
        m.LayerMm = Number(LayerBox, "Camada usual");
        m.DensityTM3 = Number(DensityBox, "Densidade");
        m.PorosityPercent = Number(VoidsBox, "Vazios");
        m.WastePercent = Number(WasteBox, "Perda e compactação");
        m.BagKg = Number(BagKgBox, "Saco (kg)");
        m.BagLiters = Number(BagLitersBox, "Saco (litros)");
        m.PriceItemCode = PriceCodeBox.Text.Trim();
        m.Notes = NotesBox.Text.Trim();
        m.References = ReferencesBox.Text.Trim();
        if (problems.Count == 0)
        {
            problems.AddRange(m.Validate());
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

        return m;
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => MarkDirty();

    private void Use_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void Area_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized && !_loading)
        {
            UpdatePreview();
        }
    }

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
        SetSwatch(GrainSwatch, GrainColorBox.Text);
        LooseMaterial? m = Collect(false);
        LooseUse use = SelectedUse;
        if (m is null || use == LooseUse.Nenhum)
        {
            Swatch.Show(null);
            SwatchText.Text = m is null ? "Corrija os valores para ver a prévia." : "Marque ao menos um uso.";
            PurchaseText.Text = string.Empty;
            return;
        }

        PavingType t = LooseMaterialUse.ToPaving(m, use);
        Swatch.Show(t);
        string layers = t.Layers.Count == 0 ? "sem camadas sob o material" : string.Join("; ", t.Layers.Select(l => l.Kind == PavingLayerKind.Manta ? l.Name : $"{l.Name} {PavingDetail.Cm(l.ThicknessMm)}"));
        SwatchText.Text = $"Amostra de 1,0 m · {LooseMaterialUse.Description(m)}\n{LooseLabels.Of(use)}: camada de {PavingDetail.Cm(t.ThicknessMm)}; {layers}."
                          + (_dirty ? "\n(alterações não salvas)" : string.Empty);

        if (!Num.TryParse(AreaBox.Text, out double area) || area < 0)
        {
            PurchaseText.Text = "Digite a área em m².";
            return;
        }

        (double volume, double? weight, int? bags) = LooseMaterialUse.Purchase(m, use, area);
        string text = $"{Num.Format(volume, "0.00")} m³ com {Num.Format(m.WastePercent, "0")}% de perda e compactação";
        if (weight is double w)
        {
            text += $" · {Num.Format(w, "0.00")} t";
        }

        if (bags is int b)
        {
            text += m.BagKg > 0 ? $" · {b} sacos de {Num.Format(m.BagKg, "0.#")} kg" : $" · {b} sacos de {Num.Format(m.BagLiters, "0.#")} L";
        }

        PurchaseText.Text = text + ".";
    }

    private static void SetSwatch(Border border, string text)
    {
        border.Background = Rgb.TryParse(text.Trim(), out Rgb c) ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) : Brushes.Transparent;
    }

    // ------------------------------------------------------------------ biblioteca

    private bool SaveCurrent()
    {
        LooseMaterial? m = Collect(true);
        if (m is null)
        {
            return false;
        }

        if (_library.Materials.Any(o => !string.Equals(o.Id, m.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(o.Name, m.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            Warn($"Já existe um material chamado \"{m.Name}\". Use outro nome.");
            return false;
        }

        m.IsUserDefined = true; // editado pelo escritório: não é sobrescrito por versões novas do plugin
        try
        {
            _library.Save(m);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Warn("Não foi possível gravar a biblioteca de pedras:\n" + ex.Message);
            return false;
        }

        _dirty = false;
        _current = m;
        RefreshList(m.Id);
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !Confirm("Descartar as alterações do material atual?"))
        {
            return;
        }

        LooseMaterial m = _current?.Clone() ?? new LooseMaterial();
        m.Id = Guid.NewGuid().ToString("N");
        m.Name = UniqueName("Nova pedra");
        m.Notes = string.Empty;
        m.IsUserDefined = true;
        Load(m);
        _dirty = true;
        UpdatePreview();
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        LooseMaterial? m = Collect(true);
        if (m is null)
        {
            return;
        }

        m.Id = Guid.NewGuid().ToString("N");
        m.Name = UniqueName(m.Name + " (cópia)");
        m.IsUserDefined = true;
        Load(m);
        _dirty = true;
        UpdatePreview();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !Confirm($"Excluir \"{_current.Name}\" da biblioteca? Pisos do Revit que já usam o material não são alterados."))
        {
            return;
        }

        try
        {
            _library.Delete(_current.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Não foi possível gravar a biblioteca de pedras:\n" + ex.Message);
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
            LooseMaterial? restored = _library.Restore(_current.Id);
            _dirty = false;
            RefreshList(restored?.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Warn("Não foi possível gravar a biblioteca de pedras:\n" + ex.Message);
        }
    }

    private string UniqueName(string name)
    {
        string candidate = name;
        for (int i = 2; _library.Materials.Any(m => string.Equals(m.Name, candidate, StringComparison.CurrentCultureIgnoreCase)); i++)
        {
            candidate = $"{name} {i}";
        }

        return candidate;
    }

    // ------------------------------------------------------------------ ações

    private void Finish(StoneAction action)
    {
        if (_dirty && !SaveCurrent())
        {
            return;
        }

        LooseMaterial? m = _current is null ? null : _library.Find(_current.Id);
        if (m is null)
        {
            Warn("Escolha um material da biblioteca.");
            return;
        }

        LooseUse use = SelectedUse;
        if (use == LooseUse.Nenhum)
        {
            Warn("Marque ao menos um uso para o material.");
            return;
        }

        if (action == StoneAction.Border)
        {
            if (!Num.TryParse(BorderWidthBox.Text, out double cm) || cm < 5 || cm > 500)
            {
                Warn("Largura da borda: use um valor entre 5 e 500 cm.");
                return;
            }

            BorderWidthM = cm / 100;
            BorderSide = (BorderSideBox.SelectedItem as Choice<StripSide>)?.Value ?? StripSide.Fora;
        }

        Material = m.Clone();
        Use = use;
        Paving = LooseMaterialUse.ToPaving(m, use);
        Action = action;
        DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.Apply);

    private void Fill_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.FillRegion);

    private void Border_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.Border);

    private void Type_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.CreateType);

    private void Region_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.CreateRegion);

    private void Detail_Click(object sender, RoutedEventArgs e) => Finish(StoneAction.Detail);
}
