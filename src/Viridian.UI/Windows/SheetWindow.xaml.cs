using System.Windows;

namespace Viridian.UI.Windows;

/// <summary>Opções escolhidas para a prancha.</summary>
public sealed class SheetOptions
{
    /// <summary>Identificador do tipo de carimbo; -1 = sem carimbo; -2 = Folha Viridian A1 (desenhada pelo plugin).</summary>
    public long TitleBlockId { get; set; } = -1;

    public string Number { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IncludeLegend { get; set; } = true;

    public bool IncludeSchedule { get; set; } = true;

    public bool IncludePhotoBoard { get; set; } = true;

    public bool FitScale { get; set; } = true;

    public double StampReserveMm { get; set; } = 70;
}

/// <summary>Prancha automática: carimbo, número, nome e conteúdo.</summary>
public partial class SheetWindow : ViridianWindow
{
    /// <param name="viewDescription">Vista que irá para a prancha (nome e escala).</param>
    /// <param name="titleBlocks">Carimbos disponíveis (rótulo, identificador).</param>
    /// <param name="suggestedNumber">Próximo número livre.</param>
    /// <param name="hasVegetation">Falso quando não há vegetação para compor a legenda.</param>
    public SheetWindow(string viewDescription, IReadOnlyList<(string Label, long Id)> titleBlocks, string suggestedNumber, bool hasVegetation)
    {
        InitializeComponent();
        ViewText.Text = viewDescription;
        TitleBlockBox.Items.Add(new Choice<long>("Folha Viridian A1 (moldura, carimbo, norte e escala do plugin)", -2));
        TitleBlockBox.Items.Add(new Choice<long>("(sem carimbo — folha A1)", -1));
        foreach ((string label, long id) in titleBlocks)
        {
            TitleBlockBox.Items.Add(new Choice<long>(label, id));
        }

        // Padrão: a Folha Viridian A1 (os carimbos do escritório continuam na lista).
        TitleBlockBox.SelectedIndex = 0;
        NumberBox.Text = suggestedNumber;
        NameBox.Text = "Planta de paisagismo";
        StampBox.Text = "70";
        LegendBox.IsEnabled = hasVegetation;
        ScheduleBox.IsEnabled = hasVegetation;
        if (!hasVegetation)
        {
            LegendBox.IsChecked = false;
            ScheduleBox.IsChecked = false;
            PhotoBoardBox.IsChecked = false;
            PhotoBoardBox.IsEnabled = false;
        }

        Loaded += (_, _) => NumberBox.Focus();
    }

    public SheetOptions Options { get; } = new();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Num.TryParse(StampBox.Text, out double stamp) || stamp < 0 || stamp > 400)
        {
            Warn("Informe a altura do carimbo em milímetros (0 a 400).");
            return;
        }

        if (string.IsNullOrWhiteSpace(NumberBox.Text))
        {
            Warn("Informe o número da prancha.");
            return;
        }

        Options.TitleBlockId = (TitleBlockBox.SelectedItem as Choice<long>)?.Value ?? -1;
        Options.Number = NumberBox.Text.Trim();
        Options.Name = NameBox.Text.Trim();
        Options.IncludeLegend = LegendBox.IsChecked == true;
        Options.IncludeSchedule = ScheduleBox.IsChecked == true;
        Options.IncludePhotoBoard = PhotoBoardBox.IsChecked == true;
        Options.FitScale = FitBox.IsChecked == true;
        Options.StampReserveMm = stamp;
        DialogResult = true;
    }
}
