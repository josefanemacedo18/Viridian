using System.Globalization;
using Viridian.Core.Colors;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;

namespace Viridian.Core.Water;

/// <summary>Estimativa da tolerância ao encharcamento quando o cadastro não informa.</summary>
public static class WaterloggingRules
{
    /// <summary>
    /// Estimativa quando a ficha não informa: só as aquáticas são presumidas tolerantes ao alagamento. A necessidade de
    /// água NÃO indica tolerância ao encharcamento (muitas plantas que pedem rega frequente apodrecem em solo
    /// saturado); sem dado, a tolerância fica não informada.
    /// </summary>
    public static WaterloggingTolerance Estimate(Species species) =>
        species.Category == PlantCategory.Aquatica ? WaterloggingTolerance.Alta : WaterloggingTolerance.NaoInformada;
}

/// <summary>Superfície que escoa para o jardim de chuva, com o coeficiente de escoamento usual.</summary>
public sealed record RunoffSurface(string Name, double Coefficient);

/// <summary>Parâmetros do jardim de chuva (valores de partida editáveis; confira com o manual de drenagem do município).</summary>
public sealed class RainGardenSettings
{
    /// <summary>Chuva de projeto a reter (mm).</summary>
    public double RainMm { get; set; } = 25;

    /// <summary>Coeficiente de escoamento da área de contribuição (0 a 1).</summary>
    public double RunoffCoefficient { get; set; } = 0.9;

    /// <summary>Lâmina d'água sobre o canteiro (cm).</summary>
    public double PondingCm { get; set; } = 15;

    /// <summary>Borda livre acima da lâmina (cm).</summary>
    public double FreeboardCm { get; set; } = 5;

    /// <summary>Cobertura morta (cm).</summary>
    public double MulchCm { get; set; } = 5;

    /// <summary>Substrato filtrante (cm) e porosidade útil (0 a 1).</summary>
    public double SubstrateCm { get; set; } = 30;

    public double SubstratePorosity { get; set; } = 0.25;

    /// <summary>Camada de brita (cm) e porosidade útil (0 a 1).</summary>
    public double GravelCm { get; set; } = 30;

    public double GravelPorosity { get; set; } = 0.40;

    /// <summary>Material da camada drenante (do catálogo de pedras ou digitado).</summary>
    public string GravelName { get; set; } = "Brita";

    /// <summary>Material da cobertura morta (vazio = não especificado).</summary>
    public string MulchName { get; set; } = string.Empty;

    /// <summary>Infiltração do solo de fundo (mm/h); zero quando não foi medida.</summary>
    public double InfiltrationMmH { get; set; }

    /// <summary>Tempo máximo desejado para esvaziar (h).</summary>
    public double MaxDrainHours { get; set; } = 48;

    public static readonly IReadOnlyList<RunoffSurface> Surfaces = new[]
    {
        new RunoffSurface("Telhado", 0.95),
        new RunoffSurface("Pavimento impermeável (concreto, asfalto, porcelanato)", 0.90),
        new RunoffSurface("Intertravado com juntas", 0.70),
        new RunoffSurface("Pedrisco ou brita", 0.35),
        new RunoffSurface("Gramado", 0.20),
    };

    public void Normalize()
    {
        static double C(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
        RainMm = C(RainMm, 1, 500, 25);
        RunoffCoefficient = C(RunoffCoefficient, 0.05, 1, 0.9);
        PondingCm = C(PondingCm, 0, 100, 15);
        FreeboardCm = C(FreeboardCm, 0, 50, 5);
        MulchCm = C(MulchCm, 0, 20, 5);
        SubstrateCm = C(SubstrateCm, 0, 200, 30);
        SubstratePorosity = C(SubstratePorosity, 0, 0.6, 0.25);
        GravelCm = C(GravelCm, 0, 300, 30);
        GravelPorosity = C(GravelPorosity, 0, 0.6, 0.40);
        InfiltrationMmH = C(InfiltrationMmH, 0, 2000, 0);
        MaxDrainHours = C(MaxDrainHours, 1, 240, 48);
        GravelName = string.IsNullOrWhiteSpace(GravelName) ? "Brita" : GravelName.Trim();
        MulchName = (MulchName ?? string.Empty).Trim();
    }

    public RainGardenSettings Clone() => (RainGardenSettings)MemberwiseClone();
}

/// <summary>Resultado do dimensionamento.</summary>
public sealed class RainGardenResult
{
    /// <summary>Volume de escoamento a reter (m³).</summary>
    public double RunoffVolumeM3 { get; init; }

    /// <summary>Armazenamento por m² de jardim (m³/m² = altura equivalente em m).</summary>
    public double StorageDepthM { get; init; }

    /// <summary>Área de jardim necessária (m²).</summary>
    public double RequiredAreaM2 { get; init; }

    /// <summary>Área disponível informada (m²); zero quando não há área escolhida.</summary>
    public double AvailableAreaM2 { get; init; }

    /// <summary>Volume que a área disponível consegue reter (m³).</summary>
    public double CapacityM3 => AvailableAreaM2 * StorageDepthM;

    /// <summary>Profundidade total de escavação (m): camadas, lâmina e borda livre.</summary>
    public double ExcavationDepthM { get; init; }

    /// <summary>Tempo para esvaziar pela infiltração no fundo (h); nulo sem ensaio de infiltração.</summary>
    public double? DrainHours { get; init; }

    public bool AreaSufficient => AvailableAreaM2 <= 0 || CapacityM3 >= RunoffVolumeM3 - 1e-9;

    public List<string> Warnings { get; } = new();

    /// <summary>Recomenda dreno de fundo quando o solo infiltra devagar ou não foi ensaiado.</summary>
    public bool NeedsUnderdrain { get; init; }
}

/// <summary>Dimensionamento simplificado de jardim de chuva (biorretenção) pelo volume a reter.</summary>
public static class RainGardenCalculator
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="contributingAreaM2">Área que escoa para o jardim (telhados, pisos), em m².</param>
    /// <param name="availableAreaM2">Área do jardim desenhado (0 quando ainda não há).</param>
    public static RainGardenResult Calculate(double contributingAreaM2, double availableAreaM2, RainGardenSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s = s.Clone();
        s.Normalize();
        double area = double.IsFinite(contributingAreaM2) ? Math.Max(0, contributingAreaM2) : 0;
        double available = double.IsFinite(availableAreaM2) ? Math.Max(0, availableAreaM2) : 0;

        // V = A × C × P ; armazenamento = lâmina + camadas × porosidade.
        double volume = area * s.RunoffCoefficient * s.RainMm / 1000;
        double storage = s.PondingCm / 100 + s.SubstrateCm / 100 * s.SubstratePorosity + s.GravelCm / 100 * s.GravelPorosity;
        double required = storage > 1e-9 ? volume / storage : double.PositiveInfinity;
        double depth = (s.FreeboardCm + s.PondingCm + s.MulchCm + s.SubstrateCm + s.GravelCm) / 100;
        double? drain = s.InfiltrationMmH > 0 ? storage * 1000 / s.InfiltrationMmH : null;
        var result = new RainGardenResult
        {
            RunoffVolumeM3 = volume,
            StorageDepthM = storage,
            RequiredAreaM2 = required,
            AvailableAreaM2 = available,
            ExcavationDepthM = depth,
            DrainHours = drain,
            NeedsUnderdrain = drain is null || drain > s.MaxDrainHours,
        };

        if (area <= 0)
        {
            result.Warnings.Add("Informe a área de contribuição (telhados e pisos que escoam para o jardim).");
        }

        if (storage <= 1e-9)
        {
            result.Warnings.Add("Sem lâmina nem camadas porosas o jardim não retém água.");
        }

        if (available > 0 && !result.AreaSufficient)
        {
            result.Warnings.Add($"A área escolhida ({available.ToString("N1", Pt)} m²) retém {result.CapacityM3.ToString("N2", Pt)} m³, menos que os {volume.ToString("N2", Pt)} m³ do escoamento: aumente a área, a lâmina ou as camadas, ou preveja extravasor para o excedente.");
        }

        if (drain is null)
        {
            result.Warnings.Add("Infiltração do solo não informada: faça o ensaio de infiltração. Até lá, preveja dreno de fundo ligado à rede.");
        }
        else if (drain > s.MaxDrainHours)
        {
            result.Warnings.Add($"Com {s.InfiltrationMmH.ToString("0.#", Pt)} mm/h o jardim levaria cerca de {drain.Value.ToString("0", Pt)} h para esvaziar (acima de {s.MaxDrainHours.ToString("0", Pt)} h): preveja dreno de fundo.");
        }

        return result;
    }

    /// <summary>Texto do memorial de cálculo, em linhas.</summary>
    public static List<string> Report(double contributingAreaM2, RainGardenResult r, RainGardenSettings s)
    {
        string N(double v, string f = "N2") => v.ToString(f, Pt);
        var lines = new List<string>
        {
            $"Área de contribuição: {N(contributingAreaM2, "N1")} m² · coeficiente de escoamento {N(s.RunoffCoefficient)} · chuva de projeto {N(s.RainMm, "0.#")} mm",
            $"Volume a reter: {N(contributingAreaM2, "N1")} × {N(s.RunoffCoefficient)} × {N(s.RainMm / 1000, "0.###")} = {N(r.RunoffVolumeM3)} m³",
            $"Armazenamento por m²: lâmina {N(s.PondingCm, "0.#")} cm + substrato {N(s.SubstrateCm, "0.#")} cm × {N(s.SubstratePorosity)} + {s.GravelName.ToLowerInvariant()} {N(s.GravelCm, "0.#")} cm × {N(s.GravelPorosity)} = {N(r.StorageDepthM, "0.###")} m³/m²",
            $"Área de jardim necessária: {N(r.RequiredAreaM2, "N1")} m²" + (r.AvailableAreaM2 > 0 ? $" · área escolhida {N(r.AvailableAreaM2, "N1")} m² ({(r.AreaSufficient ? "suficiente" : "insuficiente")})" : string.Empty),
            $"Escavação total: {N(r.ExcavationDepthM * 100, "0")} cm (borda livre, lâmina, cobertura morta, substrato e {s.GravelName.ToLowerInvariant()})",
            r.DrainHours is double h ? $"Esvaziamento pela infiltração: cerca de {N(h, "0")} h" : "Esvaziamento: depende do ensaio de infiltração do solo",
        };
        lines.AddRange(r.Warnings.Select(w => "Atenção: " + w));
        lines.Add("Valores de partida editáveis: confirme a chuva de projeto, os coeficientes e o ensaio de infiltração com o manual de drenagem do município e com o responsável técnico.");
        return lines;
    }

    /// <summary>Espécies indicadas para cada zona do jardim de chuva: fundo (alagamento), talude e borda.</summary>
    public static (List<Species> Bottom, List<Species> Slope, List<Species> Edge) SuggestPlants(IEnumerable<Species> catalog, int max = 12)
    {
        bool Low(Species s) => s.Category is not (PlantCategory.Arvore or PlantCategory.Palmeira or PlantCategory.Gramado)
                               && s.Origin != PlantOrigin.ExoticaInvasora;
        IOrderedEnumerable<Species> Order(IEnumerable<Species> list) =>
            list.OrderByDescending(s => s.Origin == PlantOrigin.Nativa).ThenBy(s => s.WaterloggingEstimated).ThenBy(s => s.PopularName, StringComparer.CurrentCultureIgnoreCase);
        var all = catalog.Where(Low).ToList();
        return (
            // Fundo: alaga e seca. Aquáticas só as de brejo que aguentam secar entre as chuvas (tolerância à seca cadastrada).
            Order(all.Where(s => s.EffectiveWaterlogging == WaterloggingTolerance.Alta
                                 && (s.Category != PlantCategory.Aquatica || s.Drought is DroughtTolerance.Media or DroughtTolerance.Alta))).Take(max).ToList(),
            Order(all.Where(s => s.EffectiveWaterlogging == WaterloggingTolerance.Media)).Take(max).ToList(),
            Order(catalog.Where(s => s.EffectiveWaterlogging is WaterloggingTolerance.Media or WaterloggingTolerance.Baixa
                                     && s.Category is PlantCategory.Arbusto or PlantCategory.Arvore && s.Origin != PlantOrigin.ExoticaInvasora)).Take(max).ToList());
    }

    /// <summary>Corte do jardim de chuva com camadas, lâmina, extravasor, dreno de fundo e plantas por zona.</summary>
    public static Drawing Detail(RainGardenSettings s, RainGardenResult r, double widthM = 2.0)
    {
        s = s.Clone();
        s.Normalize();
        widthM = Math.Clamp(double.IsFinite(widthM) ? widthM : 2.0, 1.0, 6.0);
        var g = new Drawing { Title = "Jardim de chuva" };
        double free = s.FreeboardCm / 100, pond = s.PondingCm / 100, mulch = s.MulchCm / 100, sub = s.SubstrateCm / 100, gravel = s.GravelCm / 100;
        double top = 0;                       // nível do terreno
        double soilTop = -(free + pond);       // superfície do canteiro
        double bottom = soilTop - mulch - sub - gravel;
        double slope = (free + pond) * 1.5;   // talude 1:1,5 até a superfície do canteiro
        double half = widthM / 2;
        var notes = new List<(Point2, string)>();

        // Terreno e escavação com taludes.
        g.Line(new Point2(-half - 0.6, top), new Point2(-half - slope, top), heavy: true);
        g.Line(new Point2(half + slope, top), new Point2(half + 0.6, top), heavy: true);
        g.Line(new Point2(-half - slope, top), new Point2(-half, soilTop), heavy: true);
        g.Line(new Point2(half + slope, top), new Point2(half, soilTop), heavy: true);
        for (double x = -half - 0.55; x < -half - slope; x += 0.08)
        {
            g.Line(new Point2(x, top), new Point2(x - 0.04, top - 0.04));
        }

        // Camadas (de cima para baixo).
        double y = soilTop;
        void Layer(double h, Rgb color, string name, double targetX)
        {
            if (h <= 0)
            {
                return;
            }

            var box = new List<Point2> { new(-half, y - h), new(half, y - h), new(half, y), new(-half, y) };
            g.Fills.Add(new DrawFill(box, color));
            g.Polyline(box, true);
            g.Dimension(new Point2(-half, y), new Point2(-half, y - h), 0.12, Cm(h));
            notes.Add((new Point2(targetX, y - h / 2), name));
            y -= h;
        }

        string mulchName = s.MulchName.Length > 0 ? $"COBERTURA MORTA ({s.MulchName.ToUpperInvariant()})" : "COBERTURA MORTA";
        Layer(mulch, new Rgb(0x9C, 0x74, 0x4E), $"{mulchName}, e = {Cm(mulch)}", -half * 0.2);
        Layer(sub, new Rgb(0x6F, 0x55, 0x3A), $"SUBSTRATO FILTRANTE (AREIA GROSSA E COMPOSTO ORGÂNICO), e = {Cm(sub)}", half * 0.1);
        g.Line(new Point2(-half, y), new Point2(half, y), dashed: true, heavy: true);
        notes.Add((new Point2(half * 0.35, y), "MANTA GEOTÊXTIL"));
        Layer(gravel, new Rgb(0xA8, 0x9D, 0x88), $"{s.GravelName.ToUpperInvariant()} (RESERVATÓRIO), e = {Cm(gravel)}", half * 0.55);

        // Fundo e solo natural.
        g.Line(new Point2(-half, bottom), new Point2(half, bottom), heavy: true);
        for (double x = -half + 0.05; x < half; x += 0.1)
        {
            g.Line(new Point2(x, bottom), new Point2(x - 0.05, bottom - 0.05));
        }

        notes.Add((new Point2(half * 0.8, bottom - 0.02), "SOLO NATURAL (ENSAIO DE INFILTRAÇÃO)"));

        // Lâmina d'água e extravasor.
        double water = soilTop + pond;
        double waterHalf = half + pond * 1.5;
        g.Fills.Add(new DrawFill(new List<Point2> { new(-half, soilTop), new(half, soilTop), new(waterHalf, water), new(-waterHalf, water) }, new Rgb(0xBF, 0xDC, 0xF0)));
        g.Line(new Point2(-waterHalf, water), new Point2(waterHalf, water), dashed: true);
        g.Dimension(new Point2(half + 0.05, soilTop), new Point2(half + 0.05, water), -0.35, Cm(pond));
        notes.Add((new Point2(0, water), $"LÂMINA MÁXIMA DE {Cm(pond)} · BORDA LIVRE DE {Cm(free)}"));
        double pipeX = half + slope * 0.55;
        g.Polyline(new List<Point2> { new(pipeX, water), new(pipeX + 0.12, water), new(pipeX + 0.12, water - 0.06), new(pipeX, water - 0.06) }, true, heavy: true);
        notes.Add((new Point2(pipeX + 0.06, water - 0.03), "EXTRAVASOR NA COTA DA LÂMINA, LIGADO À REDE"));

        if (r.NeedsUnderdrain && gravel > 0.06)
        {
            var c = new Point2(0, bottom + Math.Min(0.08, gravel / 3));
            g.Polyline(Drawing.Ellipse(c, 0.05, 0.05, 20), true, heavy: true);
            notes.Add((c, "DRENO DE FUNDO: TUBO PERFURADO Ø 100 mm ENVOLTO EM MANTA"));
        }

        // Plantas por zona (silhuetas simples): fundo tolerante a alagamento, taludes e bordas.
        void Tuft(double x, double baseY, double h)
        {
            for (int i = -2; i <= 2; i++)
            {
                g.Line(new Point2(x, baseY), new Point2(x + i * h * 0.12, baseY + h * (1 - Math.Abs(i) * 0.12)));
            }
        }

        Tuft(-half * 0.5, soilTop, 0.5);
        Tuft(0, soilTop, 0.6);
        Tuft(half * 0.5, soilTop, 0.5);
        Tuft(-half - slope * 0.5, soilTop * 0.5, 0.4);
        Tuft(half + slope * 0.5, soilTop * 0.5, 0.4);
        notes.Add((new Point2(-half * 0.5, soilTop + 0.4), "FUNDO: ESPÉCIES TOLERANTES A ALAGAMENTO"));
        notes.Add((new Point2(-half - slope * 0.5, soilTop * 0.5 + 0.3), "TALUDES E BORDAS: TOLERÂNCIA MÉDIA OU BAIXA"));

        g.Notes(notes, half + slope + 0.9, 0.09);
        g.Text(new Point2(-half - 0.6, 0.85), "JARDIM DE CHUVA — CORTE ESQUEMÁTICO", -1, 1.4);
        g.Text(new Point2(-half - 0.6, 0.72), $"Escavação total {Cm(r.ExcavationDepthM)} · armazenamento {r.StorageDepthM.ToString("0.###", Pt)} m³/m² · volume a reter {r.RunoffVolumeM3.ToString("N2", Pt)} m³", -1, 0.95);
        g.Text(new Point2(-half - 0.6, bottom - 0.25), "Valores de referência: confirme com o manual de drenagem do município, o ensaio de infiltração e o responsável técnico.", -1, 0.8);
        return g;
    }

    private static string Cm(double m) => Math.Round(m * 100).ToString("0", Pt) + " cm";
}
