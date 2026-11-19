using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Model;
using Viridian.Core.Solar;

namespace Viridian.UI.Windows;

/// <summary>Opções do estudo solar: local da obra (com aviso quando o projeto não tem localização), período, mapas e faixas de luz.</summary>
public partial class SolarStudyWindow : ViridianWindow
{
    private readonly SolarSite _projectSite;
    private readonly bool _projectUndefined;
    private readonly CheckBox[] _months = new CheckBox[12];

    /// <param name="projectSite">Localização lida do projeto (ou as coordenadas padrão das configurações).</param>
    /// <param name="projectUndefined">O projeto não tem localização definida.</param>
    /// <param name="trueNorthDeg">Ângulo do norte verdadeiro em relação ao norte do projeto (graus).</param>
    public SolarStudyWindow(SolarSite projectSite, bool projectUndefined, double trueNorthDeg, SolarSettings settings)
    {
        InitializeComponent();
        _projectSite = projectSite;
        _projectUndefined = projectUndefined;
        Settings = settings.Clone();
        Settings.Normalize();

        LatBox.Text = Num.Format(projectSite.LatitudeDeg, "0.######");
        LonBox.Text = Num.Format(projectSite.LongitudeDeg, "0.######");
        TzBox.Text = Num.Format(projectSite.TimeZoneHours, "0.##");
        NameBox.Text = projectSite.Name;
        SaveBox.IsChecked = projectUndefined;
        if (projectUndefined)
        {
            SiteStatus.Text = "O projeto não tem localização definida (Gerenciar > Localização). Recomendado: informe as coordenadas da obra abaixo "
                              + "(cole do Google Maps: clique com o botão direito no local e copie os números) e deixe marcada a opção de gravá-las no projeto. "
                              + "Os valores abaixo são os das configurações do Viridian.";
            SiteStatus.SetResourceReference(TextBlock.ForegroundProperty, "VirWarning");
        }
        else if (projectSite.Source == SiteSource.Digitado)
        {
            SiteStatus.Text = "Coordenadas das configurações do Viridian (a opção de usar a localização do projeto está desligada em Configurações > Composição).";
        }
        else
        {
            SiteStatus.Text = "Coordenadas lidas da localização do projeto. Confira se correspondem ao local da obra.";
        }

        NorthText.Text = Math.Abs(trueNorthDeg) < 0.05
            ? "Norte do projeto alinhado ao norte verdadeiro. Se a planta não estiver com o norte para cima, ajuste em Gerenciar > Posição > Girar norte verdadeiro."
            : $"Norte verdadeiro girado {Num.Format(trueNorthDeg, "0.#")}° em relação ao norte do projeto (Gerenciar > Posição): o cálculo já considera esse giro.";

        for (int m = 0; m < 12; m++)
        {
            _months[m] = new CheckBox { Content = MonthSet.Abbreviations[m], Margin = new Thickness(0, 0, 10, 2), MinWidth = 44 };
            System.Windows.Automation.AutomationProperties.SetName(_months[m], "Mapa de " + Sun.MonthNames[m]);
            MonthsPanel.Children.Add(_months[m]);
        }

        IEnumerable<int> chosen = Settings.MapMonths.Count > 0 ? Settings.MapMonths : DefaultMonths(projectSite.LatitudeDeg);
        foreach (int m in chosen)
        {
            _months[m - 1].IsChecked = true;
        }

        AnnualBox.IsChecked = Settings.MapAnnual;
        ShadeSheetsBox.IsChecked = Settings.ShadeSheets;
        FromHourBox.Text = Settings.FromHour.ToString(Num.PtBr);
        ToHourBox.Text = Settings.ToHour.ToString(Num.PtBr);
        PlanHoursBox.Text = string.Join(", ", Settings.PlanHours);
        foreach ((string label, Core.Detailing.GrowthStage stage) in new[]
                 {
                     ("Adulto (maturidade)", Core.Detailing.GrowthStage.Maturidade), ("Aos 6 anos", Core.Detailing.GrowthStage.SeisAnos),
                     ("Aos 3 anos", Core.Detailing.GrowthStage.TresAnos), ("No plantio (mudas)", Core.Detailing.GrowthStage.Plantio),
                 })
        {
            StageBox.Items.Add(new Choice<Core.Detailing.GrowthStage>(label, stage));
        }

        StageBox.SelectedItem = StageBox.Items.Cast<Choice<Core.Detailing.GrowthStage>>().FirstOrDefault(c => c.Value == Settings.Stage) ?? StageBox.Items[0];
        ExportBox.IsChecked = Settings.ExportImages;
        BuildingsBox.IsChecked = Settings.IncludeBuildings;
        FillBands(Settings);
        MonthlyRadio.IsChecked = Settings.Monthly;
        KeyRadio.IsChecked = !Settings.Monthly;
        UpdatePeriod();
        UpdateBands();
    }

    public SolarSettings Settings { get; private set; }

    /// <summary>Local escolhido para o estudo.</summary>
    public SolarSite? Site { get; private set; }

    /// <summary>Gravar as coordenadas na localização do projeto.</summary>
    public bool SaveToProject { get; private set; }

    /// <summary>Inverno e verão do hemisfério (junho e dezembro).</summary>
    public static IReadOnlyList<int> DefaultMonths(double latitude) => latitude < 0 ? new[] { 6, 12 } : new[] { 12, 6 };

    private void FillBands(SolarSettings s)
    {
        FullBox.Text = Num.Format(s.FullSunHours);
        ShadeBox.Text = Num.Format(s.ShadeHours);
        PartBox.Text = Num.Format(s.PartShadeHours);
        TolBox.Text = Num.Format(s.ToleranceHours);
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (!SolarSite.TryParseCoordinates(PasteBox.Text, out double lat, out double lon))
        {
            Warn("Não entendi as coordenadas. Use, por exemplo, \"-22.9068, -43.1729\" ou 22°54'24\"S 43°10'22\"W.");
            return;
        }

        LatBox.Text = Num.Format(lat, "0.######");
        LonBox.Text = Num.Format(lon, "0.######");
        if (_projectUndefined || !Num.TryParse(TzBox.Text, out _))
        {
            TzBox.Text = Num.Format(SolarSite.SuggestTimeZone(lon), "0.##");
        }
    }

    private void SuggestZone_Click(object sender, RoutedEventArgs e)
    {
        if (Num.TryParse(LonBox.Text, out double lon))
        {
            TzBox.Text = Num.Format(SolarSite.SuggestTimeZone(lon), "0.##");
        }
    }

    private void Field_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdateBands();
        }
    }

    private void Period_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            UpdatePeriod();
        }
    }

    private void UpdatePeriod()
    {
        bool monthly = MonthlyRadio.IsChecked == true;
        MonthsPanel.IsEnabled = monthly;
        MonthsLabel.IsEnabled = monthly;
        MapsHint.Text = monthly
            ? "Cada mapa é uma cópia da planta; a planta original não muda. O calendário de luz (todos os meses) é criado sempre."
            : "Com solstícios e equinócio, os mapas são o da média e o do solstício de inverno (pior caso), e os quadros de sombras são das três datas. O calendário de luz mostra as três datas.";
    }

    private void UpdateBands()
    {
        (SolarSettings? s, List<string> problems) = ReadBands();
        if (s is null)
        {
            BandsText.Text = string.Join("\n", problems);
            return;
        }

        BandsText.Text = string.Join("\n", SolarClasses.Bins(s).Select(b => b.Label))
                         + $"\n\nNo limite: até {SolarClasses.Hours(s.ToleranceHours)} h fora da faixa que a espécie pede.";
    }

    private (SolarSettings? Settings, List<string> Problems) ReadBands()
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

        double full = Value(FullBox, "Sol pleno", 1, 14);
        double shade = Value(ShadeBox, "Meia-sombra", 0, 14);
        double part = Value(PartBox, "Sombra parcial", 0, 14);
        double tol = Value(TolBox, "Tolerância", 0, 3);
        if (problems.Count == 0 && !(part <= shade && shade <= full))
        {
            problems.Add("As faixas devem crescer: sombra parcial ≤ meia-sombra ≤ sol pleno.");
        }

        if (problems.Count > 0)
        {
            return (null, problems);
        }

        SolarSettings s = Settings.Clone();
        s.FullSunHours = full;
        s.ShadeHours = shade;
        s.PartShadeHours = part;
        s.ToleranceHours = tol;
        return (s, problems);
    }

    private void DefaultBands_Click(object sender, RoutedEventArgs e) => FillBands(new SolarSettings());

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();
        if (!Num.TryParse(LatBox.Text, out double lat) || Math.Abs(lat) > 66)
        {
            problems.Add("Latitude: use um valor entre −66 e 66 graus (o estudo vale até o círculo polar; sul negativo).");
        }

        if (!Num.TryParse(LonBox.Text, out double lon) || Math.Abs(lon) > 180)
        {
            problems.Add("Longitude: use um valor entre −180 e 180 graus (oeste negativo; no Brasil, entre −74 e −28).");
        }

        if (!Num.TryParse(TzBox.Text, out double tz) || tz is < -12 or > 14)
        {
            problems.Add("Fuso horário: use um valor entre −12 e 14 (Brasília = −3).");
        }

        (SolarSettings? s, List<string> bandProblems) = ReadBands();
        problems.AddRange(bandProblems);
        if (!int.TryParse(FromHourBox.Text.Trim(), out int fromHour) || !int.TryParse(ToHourBox.Text.Trim(), out int toHour) || fromHour is < 0 or > 23 || toHour < fromHour || toHour > 23)
        {
            problems.Add("Horário do quadro de sombras: use horas inteiras de 0 a 23, a primeira menor que a última (ex.: das 7 às 17).");
            fromHour = 7;
            toHour = 17;
        }

        var planHours = new List<int>();
        foreach (string part in PlanHoursBox.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim().TrimEnd('h', 'H'), out int h) && h is >= 0 and <= 23)
            {
                planHours.Add(h);
            }
            else
            {
                problems.Add($"Sombras na planta: \"{part}\" não é uma hora (use, por exemplo, 9, 12, 15).");
                break;
            }
        }
        if (problems.Count > 0 || s is null)
        {
            Warn(string.Join("\n", problems));
            return;
        }

        if (lat > 6 && lon < -28 && lon > -75 && !Confirm("A latitude é positiva (hemisfério norte), mas a longitude é do Brasil. No Brasil, quase toda latitude é negativa (sul). Continuar assim mesmo?"))
        {
            return;
        }

        bool changed = Math.Abs(lat - _projectSite.LatitudeDeg) > 1e-6 || Math.Abs(lon - _projectSite.LongitudeDeg) > 1e-6
                       || Math.Abs(tz - _projectSite.TimeZoneHours) > 1e-6;
        SaveToProject = SaveBox.IsChecked == true;
        SiteSource source = SaveToProject || (!_projectUndefined && !changed) ? SiteSource.Projeto : SiteSource.Digitado;
        Site = new SolarSite(lat, lon, tz, NameBox.Text.Trim(), source);

        s.Monthly = MonthlyRadio.IsChecked == true;
        s.MapAnnual = AnnualBox.IsChecked == true;
        s.MapMonths = Enumerable.Range(1, 12).Where(m => _months[m - 1].IsChecked == true).ToList();
        s.IncludeBuildings = BuildingsBox.IsChecked == true;
        s.ShadeSheets = ShadeSheetsBox.IsChecked == true;
        s.FromHour = fromHour;
        s.ToHour = toHour;
        s.PlanHours = planHours.Distinct().OrderBy(h => h).ToList();
        s.Stage = (StageBox.SelectedItem as Choice<Core.Detailing.GrowthStage>)?.Value ?? Core.Detailing.GrowthStage.Maturidade;
        s.ExportImages = ExportBox.IsChecked == true;
        if (source == SiteSource.Digitado || _projectUndefined)
        {
            // Próximos projetos sem localização partem destas coordenadas.
            s.LatitudeDeg = lat;
            s.LongitudeDeg = lon;
            s.TimeZoneHours = tz;
            s.PlaceName = Site.Name;
        }

        s.Normalize();
        Settings = s;
        DialogResult = true;
    }
}
