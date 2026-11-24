using System.Windows;
using System.Windows.Controls;
using Viridian.Core.Detailing;
using Viridian.Core.Model;
using Viridian.Core.Model3D;
using Viridian.Core.Model3D.Profiles;

namespace Viridian.UI.Windows;

/// <summary>
/// Opções da maquete 3D: recorte, motor (botânico ou simples), qualidade, época do ano, flores e frutos, fase de
/// crescimento e apresentação, com a estimativa de peso atualizada a cada escolha.
/// </summary>
public partial class MaqueteWindow : ViridianWindow
{
    private readonly Func<MaqueteEngine, QualityLevel, long> _estimate;
    private readonly int _plants;
    private readonly int _beds;
    private readonly long _warnTriangles;
    private readonly bool _southern;

    /// <param name="settings">Escolhas lembradas (não são alteradas; as novas ficam em <see cref="Settings"/>).</param>
    /// <param name="inPlan">Há uma vista em planta ativa (permite escolher uma região).</param>
    /// <param name="exists">Já existe uma maquete no projeto (será substituída).</param>
    /// <param name="plants">Exemplares do projeto inteiro (para a estimativa).</param>
    /// <param name="beds">Maciços em volume do projeto inteiro.</param>
    /// <param name="estimate">Triângulos estimados do projeto inteiro para o motor e a qualidade.</param>
    /// <param name="southern">Obra no hemisfério sul (pela localização do projeto).</param>
    public MaqueteWindow(ModelSettings settings, bool inPlan, bool exists, int plants, int beds, Func<MaqueteEngine, QualityLevel, long> estimate, bool southern)
    {
        InitializeComponent();
        _estimate = estimate;
        _plants = plants;
        _beds = beds;
        _warnTriangles = settings.WarnTriangles;
        _southern = southern;

        IntroText.Text = (exists ? "A maquete atual será substituída. " : string.Empty)
                         + "A vegetação aparece em volume na vista 3D própria do Viridian; as plantas baixas e as tabelas não mudam. "
                         + "Depois de alterar o plantio, use Atualizar Maquete (as mesmas escolhas são mantidas).";
        RegionRadio.IsEnabled = inPlan;
        RegionHint.Text = inPlan ? string.Empty : "Disponível com uma vista em planta aberta.";
        WholeRadio.IsChecked = true;

        for (int m = 1; m <= 12; m++)
        {
            MonthBox.Items.Add(new Choice<int>(Core.Solar.Sun.MonthNames[m - 1], m));
        }

        MonthBox.SelectedIndex = Math.Clamp(settings.ReferenceMonth, 1, 12) - 1;
        foreach (GrowthStage stage in new[] { GrowthStage.Maturidade, GrowthStage.SeisAnos, GrowthStage.TresAnos, GrowthStage.Plantio })
        {
            StageBox.Items.Add(new Choice<GrowthStage>(stage switch
            {
                GrowthStage.Maturidade => "Adulta (maturidade)",
                GrowthStage.SeisAnos => "Aos 6 anos",
                GrowthStage.TresAnos => "Aos 3 anos",
                _ => "No plantio (mudas)",
            }, stage));
        }

        StageBox.SelectedIndex = 0;
        BotanicalRadio.IsChecked = settings.Engine == MaqueteEngine.Botanico;
        LegacyRadio.IsChecked = settings.Engine == MaqueteEngine.Legado;
        SimpleRadio.IsChecked = settings.Quality == QualityLevel.Simplificada;
        MediumRadio.IsChecked = settings.Quality == QualityLevel.Intermediaria;
        DetailedRadio.IsChecked = settings.Quality == QualityLevel.Detalhada;
        SeasonBox.IsChecked = settings.UseSeasonality;
        FruitsBox.IsChecked = settings.ShowFruits;
        VariationBox.IsChecked = settings.Variation;
        FlowersBox.IsChecked = settings.ShowFlowers;
        TerrainBox.IsChecked = settings.FollowTerrain;
        EdgesBox.IsChecked = settings.ShowEdges;
        Settings = settings;
        Refresh();
    }

    /// <summary>Escolhas confirmadas (para gravar como padrão da próxima vez).</summary>
    public ModelSettings Settings { get; private set; }

    public bool WholeProject => WholeRadio.IsChecked == true;

    public GrowthStage Stage => (StageBox.SelectedItem as Choice<GrowthStage>)?.Value ?? GrowthStage.Maturidade;

    public MaqueteEngine Engine => LegacyRadio.IsChecked == true ? MaqueteEngine.Legado : MaqueteEngine.Botanico;

    public QualityLevel Quality => SimpleRadio.IsChecked == true ? QualityLevel.Simplificada : DetailedRadio.IsChecked == true ? QualityLevel.Detalhada : QualityLevel.Intermediaria;

    /// <summary>Mês de referência (zero = sem estação).</summary>
    public int Month => Engine == MaqueteEngine.Botanico && SeasonBox.IsChecked == true ? (MonthBox.SelectedItem as Choice<int>)?.Value ?? 1 : 0;

    /// <summary>Estado da maquete com as escolhas (sem a região, escolhida depois na planta).</summary>
    public MaqueteState ToState() => new()
    {
        Stage = Stage,
        Engine = Engine,
        Quality = Quality,
        Month = Month,
        Flowers = FlowersBox.IsChecked == true,
        Fruits = Engine == MaqueteEngine.Botanico && FruitsBox.IsChecked == true,
        Variation = VariationBox.IsChecked != false,
    };

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    private void Month_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (EstimateText is null || SeasonHint is null)
        {
            return;
        }

        bool botanical = Engine == MaqueteEngine.Botanico;
        BotanicalPanel.IsEnabled = botanical;
        BotanicalPanel.Opacity = botanical ? 1 : 0.55;
        MonthBox.IsEnabled = botanical && SeasonBox.IsChecked == true;
        string hemisphere = _southern ? "hemisfério sul" : "hemisfério norte (meses de folhagem, flor e fruto deslocados em seis)";
        SeasonHint.Text = SeasonBox.IsChecked == true
            ? $"Obra no {hemisphere}, pela localização do projeto. Decíduas ficam sem folhas e semidecíduas com folhagem rala nos meses da ficha; "
              + "flores e frutos aparecem só na sua época (da ficha ou estimada pelo gênero — o resumo informa quantas foram estimadas)."
            : "Sem estação: folhagem cheia e flores nas espécies de floração vistosa, como nas versões anteriores.";

        long triangles = _estimate(Engine, Quality);
        string scope = WholeProject ? "Projeto inteiro" : "Projeto inteiro (a região escolhida terá menos)";
        string text = $"{scope}: {_plants} exemplar(es) e {_beds} maciço(s) em volume. ";
        text += botanical
            ? $"Estimativa: cerca de {Thousands(triangles)} triângulos no nível {ProfileLabels.Of(Quality)}."
            : "Gerador simples: volumes leves, sem estação nem frutos.";
        bool heavy = botanical && triangles > _warnTriangles;
        if (heavy)
        {
            text += " A vista 3D pode ficar lenta: prefira o nível Simplificado ou uma região menor.";
            EstimateText.SetResourceReference(TextBlock.ForegroundProperty, "VirWarning");
        }
        else
        {
            EstimateText.SetResourceReference(TextBlock.ForegroundProperty, "VirText");
        }

        EstimateText.Text = text;
    }

    private static string Thousands(long value) => value >= 1_000_000
        ? Num.Format(value / 1_000_000.0, "0.#") + " milhões de"
        : value >= 1000 ? Num.Format(value / 1000.0, "0") + " mil" : value.ToString(Num.PtBr);

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (StageBox.SelectedItem is null)
        {
            Warn("Escolha a fase de crescimento.");
            return;
        }

        var s = new ModelSettings
        {
            MaxPlants = Settings.MaxPlants,
            FollowTerrain = TerrainBox.IsChecked == true,
            TerrainSearchM = Settings.TerrainSearchM,
            TrunkColor = Settings.TrunkColor,
            ShowFlowers = FlowersBox.IsChecked == true,
            Engine = Engine,
            Quality = Quality,
            UseSeasonality = SeasonBox.IsChecked == true,
            ReferenceMonth = (MonthBox.SelectedItem as Choice<int>)?.Value ?? Settings.ReferenceMonth,
            ShowFruits = FruitsBox.IsChecked == true,
            Variation = VariationBox.IsChecked != false,
            ShowEdges = EdgesBox.IsChecked == true,
            WarnTriangles = Settings.WarnTriangles,
        };
        s.Normalize();
        Settings = s;
        DialogResult = true;
    }
}
