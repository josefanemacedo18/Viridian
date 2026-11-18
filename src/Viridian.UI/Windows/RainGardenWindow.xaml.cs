using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Model;
using Viridian.Core.Water;

namespace Viridian.UI.Windows;

/// <summary>Dimensionamento do jardim de chuva, com plantas sugeridas por zona.</summary>
public partial class RainGardenWindow : ViridianWindow
{
    private bool _loading;

    public RainGardenWindow(RainGardenSettings settings, double contributingAreaM2, double gardenAreaM2, IEnumerable<Species> catalog, bool hasProject)
    {
        InitializeComponent();
        Settings = settings.Clone();
        Settings.Normalize();
        SurfaceBox.Items.Add(new Choice<double?>("Personalizado", null));
        foreach (RunoffSurface surface in RainGardenSettings.Surfaces)
        {
            SurfaceBox.Items.Add(new Choice<double?>($"{surface.Name} ({Num.Format(surface.Coefficient)})", surface.Coefficient));
        }

        AreaBox.Text = Num.Format(contributingAreaM2, "0.#");
        GardenAreaBox.Text = Num.Format(gardenAreaM2, "0.#");
        Fill(Settings);
        DetailButton.IsEnabled = hasProject;

        (List<Species> bottom, List<Species> slope, List<Species> edge) = RainGardenCalculator.SuggestPlants(catalog, 25);
        BottomList.ItemsSource = bottom.Select(Label).ToList();
        SlopeList.ItemsSource = slope.Select(Label).ToList();
        EdgeList.ItemsSource = edge.Select(Label).ToList();
        Update();
    }

    public RainGardenSettings Settings { get; private set; }

    public double ContributingAreaM2 { get; private set; }

    public double GardenAreaM2 { get; private set; }

    public RainGardenResult? Result { get; private set; }

    /// <summary>Verdadeiro quando o usuário pediu o detalhe em corte.</summary>
    public bool CreateDetail { get; private set; }

    /// <summary>Verdadeiro quando os valores devem ser gravados como padrão.</summary>
    public bool SaveAsDefault { get; private set; }

    private static string Label(Species s) =>
        s.PopularName + (s.Origin == PlantOrigin.Nativa ? " (nativa)" : string.Empty) + (s.WaterloggingEstimated ? " *" : string.Empty);

    private void Fill(RainGardenSettings s)
    {
        _loading = true;
        RunoffBox.Text = Num.Format(s.RunoffCoefficient);
        RainBox.Text = Num.Format(s.RainMm);
        PondingBox.Text = Num.Format(s.PondingCm);
        FreeboardBox.Text = Num.Format(s.FreeboardCm);
        MulchBox.Text = Num.Format(s.MulchCm);
        SubstrateBox.Text = Num.Format(s.SubstrateCm);
        SubstratePorosityBox.Text = Num.Format(s.SubstratePorosity * 100);
        GravelBox.Text = Num.Format(s.GravelCm);
        GravelPorosityBox.Text = Num.Format(s.GravelPorosity * 100);
        InfiltrationBox.Text = Num.Format(s.InfiltrationMmH);
        DrainBox.Text = Num.Format(s.MaxDrainHours);
        SurfaceBox.SelectedItem = SurfaceBox.Items.Cast<Choice<double?>>().FirstOrDefault(c => c.Value is double v && Math.Abs(v - s.RunoffCoefficient) < 1e-9)
                                  ?? SurfaceBox.Items[0];
        _loading = false;
    }

    /// <summary>Lê os campos; nulo quando algum valor está fora da faixa.</summary>
    private (RainGardenSettings Settings, double Area, double Garden, List<string> Problems) Read()
    {
        var problems = new List<string>();
        double Value(TextBox box, string label, double min, double max)
        {
            if (Num.TryParse(box.Text, out double v) && v >= min && v <= max)
            {
                return v;
            }

            problems.Add($"{label}: use um valor entre {Num.Format(min)} e {Num.Format(max)}.");
            return min;
        }

        var s = new RainGardenSettings
        {
            RunoffCoefficient = Value(RunoffBox, "Coeficiente de escoamento", 0.05, 1),
            RainMm = Value(RainBox, "Chuva de projeto", 1, 500),
            PondingCm = Value(PondingBox, "Lâmina d'água", 0, 100),
            FreeboardCm = Value(FreeboardBox, "Borda livre", 0, 50),
            MulchCm = Value(MulchBox, "Cobertura morta", 0, 20),
            SubstrateCm = Value(SubstrateBox, "Substrato", 0, 200),
            SubstratePorosity = Value(SubstratePorosityBox, "Porosidade do substrato", 0, 60) / 100,
            GravelCm = Value(GravelBox, "Brita", 0, 300),
            GravelPorosity = Value(GravelPorosityBox, "Porosidade da brita", 0, 60) / 100,
            InfiltrationMmH = Value(InfiltrationBox, "Infiltração", 0, 2000),
            MaxDrainHours = Value(DrainBox, "Tempo de esvaziamento", 1, 240),
        };
        double area = Value(AreaBox, "Área de contribuição", 0, 1_000_000);
        double garden = Value(GardenAreaBox, "Área do jardim", 0, 1_000_000);
        return (s, area, garden, problems);
    }

    private void Update()
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        (RainGardenSettings s, double area, double garden, List<string> problems) = Read();
        if (problems.Count > 0)
        {
            ResultText.Text = string.Join("\n", problems);
            Result = null;
            return;
        }

        RainGardenResult r = RainGardenCalculator.Calculate(area, garden, s);
        Result = r;
        ResultText.Text = string.Join("\n\n", RainGardenCalculator.Report(area, r, s));
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => Update();

    private void Surface_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && SurfaceBox.SelectedItem is Choice<double?> { Value: double c })
        {
            RunoffBox.Text = Num.Format(c);
        }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        Fill(new RainGardenSettings());
        Update();
    }

    private bool Accept()
    {
        (RainGardenSettings s, double area, double garden, List<string> problems) = Read();
        if (problems.Count > 0)
        {
            Warn(string.Join("\n", problems));
            return false;
        }

        Settings = s;
        ContributingAreaM2 = area;
        GardenAreaM2 = garden;
        Result = RainGardenCalculator.Calculate(area, garden, s);
        return true;
    }

    private void SaveDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (Accept())
        {
            SaveAsDefault = true;
            Inform("Valores gravados como padrão para os próximos jardins de chuva.");
        }
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (!Accept())
        {
            return;
        }

        if (ContributingAreaM2 <= 0)
        {
            Warn("Informe a área de contribuição antes de criar o detalhe.");
            return;
        }

        CreateDetail = true;
        DialogResult = true;
    }
}
