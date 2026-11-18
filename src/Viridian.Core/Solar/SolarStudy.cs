using System.Globalization;
using Viridian.Core.Detailing;
using Viridian.Core.Geometry;
using Viridian.Core.Model;
using Viridian.Core.Model3D;

namespace Viridian.Core.Solar;

/// <summary>Configurações do estudo solar.</summary>
public sealed class SolarSettings
{
    /// <summary>Usa a latitude da localização do projeto no Revit (Gerenciar > Localização).</summary>
    public bool UseProjectLocation { get; set; } = true;

    /// <summary>Latitude digitada (graus, negativa no hemisfério sul).</summary>
    public double LatitudeDeg { get; set; } = -23.55;

    /// <summary>Longitude digitada (graus, negativa a oeste de Greenwich).</summary>
    public double LongitudeDeg { get; set; } = -46.63;

    /// <summary>Fuso horário digitado (horas em relação ao UTC; Brasília = −3).</summary>
    public double TimeZoneHours { get; set; } = -3;

    /// <summary>Nome do local digitado.</summary>
    public string PlaceName { get; set; } = string.Empty;

    /// <summary>Correção adicional do norte (graus, sentido anti-horário), para conferir com o estudo solar do Revit.</summary>
    public double NorthAdjustDeg { get; set; }

    /// <summary>Intervalo entre as posições do sol (minutos).</summary>
    public int StepMinutes { get; set; } = 30;

    /// <summary>Lado da malha de análise (m); aumentado automaticamente em áreas grandes.</summary>
    public double GridM { get; set; } = 1.0;

    /// <summary>Máximo de pontos analisados na malha.</summary>
    public int MaxPoints { get; set; } = 900;

    /// <summary>Considera edificações e terreno do modelo (e de vínculos) além das copas.</summary>
    public bool IncludeBuildings { get; set; } = true;

    /// <summary>Horas de sol direto a partir das quais o local é de sol pleno.</summary>
    public double FullSunHours { get; set; } = 6;

    /// <summary>Horas a partir das quais o local é de meia-sombra (abaixo: sombra parcial ou sombra).</summary>
    public double ShadeHours { get; set; } = 3;

    /// <summary>Horas a partir das quais o local é de sombra parcial (abaixo: sombra).</summary>
    public double PartShadeHours { get; set; } = 1;

    /// <summary>Folga, em horas, para considerar a planta "no limite" da faixa que pede (e não fora dela).</summary>
    public double ToleranceHours { get; set; } = 1;

    /// <summary>Analisa mês a mês (12 dias típicos); falso = só solstícios e equinócio.</summary>
    public bool Monthly { get; set; } = true;

    /// <summary>Cria o mapa da média do ano.</summary>
    public bool MapAnnual { get; set; } = true;

    /// <summary>Meses (1 a 12) com mapa próprio. Vazio = inverno e verão do hemisfério.</summary>
    public List<int> MapMonths { get; set; } = new();

    public SolarSettings Clone()
    {
        var copy = (SolarSettings)MemberwiseClone();
        copy.MapMonths = new List<int>(MapMonths ?? new List<int>());
        return copy;
    }

    public void Normalize()
    {
        LatitudeDeg = double.IsFinite(LatitudeDeg) ? Math.Clamp(LatitudeDeg, -66, 66) : -23.55;
        NorthAdjustDeg = double.IsFinite(NorthAdjustDeg) ? Math.Clamp(NorthAdjustDeg, -180, 180) : 0;
        StepMinutes = Math.Clamp(StepMinutes <= 0 ? 30 : StepMinutes, 10, 120);
        GridM = double.IsFinite(GridM) ? Math.Clamp(GridM, 0.25, 20) : 1.0;
        MaxPoints = Math.Clamp(MaxPoints <= 0 ? 900 : MaxPoints, 50, 5000);
        FullSunHours = double.IsFinite(FullSunHours) ? Math.Clamp(FullSunHours, 1, 14) : 6;
        ShadeHours = double.IsFinite(ShadeHours) ? Math.Clamp(ShadeHours, 0, FullSunHours) : 3;
        PartShadeHours = double.IsFinite(PartShadeHours) ? Math.Clamp(PartShadeHours, 0, ShadeHours) : Math.Min(1, ShadeHours);
        ToleranceHours = double.IsFinite(ToleranceHours) ? Math.Clamp(ToleranceHours, 0, 3) : 1;
        LongitudeDeg = double.IsFinite(LongitudeDeg) ? Math.Clamp(LongitudeDeg, -180, 180) : -46.63;
        TimeZoneHours = double.IsFinite(TimeZoneHours) ? Math.Clamp(TimeZoneHours, -12, 14) : -3;
        PlaceName ??= string.Empty;
        MapMonths = (MapMonths ?? new List<int>()).Where(m => m is >= 1 and <= 12).Distinct().OrderBy(m => m).ToList();
    }
}

/// <summary>Data do estudo (dia e mês; o ano não influi na posição do sol para este fim).</summary>
public sealed record SolarDate(string Name, int Month, int Day)
{
    public int DayOfYear => new DateTime(2025, Month, Day).DayOfYear;

    /// <summary>Rótulo curto para colunas de quadros (mês abreviado ou a data).</summary>
    public string Short => Name.StartsWith("Solstício de inverno", StringComparison.Ordinal) ? "inverno"
        : Name.StartsWith("Solstício de verão", StringComparison.Ordinal) ? "verão"
        : Name.StartsWith("Equinócio", StringComparison.Ordinal) ? "equinócio"
        : MonthSet.Abbreviations[Month - 1];
}

/// <summary>Posição do sol: azimute a partir do norte (sentido horário) e altura acima do horizonte, em graus.</summary>
public readonly record struct SunPosition(double AzimuthDeg, double AltitudeDeg)
{
    public bool IsUp => AltitudeDeg > 0.5;

    /// <summary>Direção para o sol no sistema norte verdadeiro (X leste, Y norte, Z acima).</summary>
    public Vec3 Direction
    {
        get
        {
            double az = Geo.DegToRad(AzimuthDeg), alt = Geo.DegToRad(AltitudeDeg);
            return new Vec3(Math.Sin(az) * Math.Cos(alt), Math.Cos(az) * Math.Cos(alt), Math.Sin(alt));
        }
    }
}

/// <summary>Posição do sol pela hora solar (meio-dia solar = sol no ponto mais alto), sem depender do fuso horário.</summary>
public static class Sun
{
    /// <summary>Declinação solar (graus) pela série de Spencer/NOAA.</summary>
    public static double DeclinationDeg(int dayOfYear)
    {
        double g = Geo.TwoPi / 365.0 * (dayOfYear - 1);
        double d = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g)
                   + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        return Geo.RadToDeg(d);
    }

    public static SunPosition Position(double latitudeDeg, int dayOfYear, double solarHour)
    {
        double phi = Geo.DegToRad(latitudeDeg);
        double delta = Geo.DegToRad(DeclinationDeg(dayOfYear));
        double omega = Geo.DegToRad(15 * (solarHour - 12));
        double sinAlt = Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Cos(omega);
        double alt = Math.Asin(Math.Clamp(sinAlt, -1, 1));
        double cosAz = (Math.Sin(delta) - Math.Sin(alt) * Math.Sin(phi)) / Math.Max(1e-9, Math.Cos(alt) * Math.Cos(phi));
        double az = Geo.RadToDeg(Math.Acos(Math.Clamp(cosAz, -1, 1)));
        if (omega > 0)
        {
            az = 360 - az; // tarde: sol a oeste
        }

        return new SunPosition(az, Geo.RadToDeg(alt));
    }

    public static readonly IReadOnlyList<string> MonthNames = new[]
    {
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro",
    };

    /// <summary>
    /// Dias típicos de cada mês (Klein, 1977): o dia cuja radiação extraterrestre é a média do mês. Representam bem
    /// o mês inteiro no cálculo das horas de sol.
    /// </summary>
    public static IReadOnlyList<SolarDate> MonthlyDates()
    {
        int[] days = { 17, 16, 16, 15, 15, 11, 17, 16, 15, 15, 14, 10 };
        return Enumerable.Range(0, 12).Select(i => new SolarDate(MonthNames[i], i + 1, days[i])).ToList();
    }

    /// <summary>Equação do tempo (minutos): diferença entre a hora solar verdadeira e a média (NOAA).</summary>
    public static double EquationOfTimeMinutes(int dayOfYear)
    {
        double g = Geo.TwoPi / 365.0 * (dayOfYear - 1);
        return 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
    }

    /// <summary>Hora legal (do relógio, sem horário de verão) correspondente à hora solar, pela longitude e pelo fuso.</summary>
    public static double ClockHour(double solarHour, double longitudeDeg, double timeZoneHours, int dayOfYear) =>
        solarHour - (longitudeDeg / 15.0 - timeZoneHours) - EquationOfTimeMinutes(dayOfYear) / 60.0;

    /// <summary>Nascer e pôr do sol em hora solar (com a refração da atmosfera); nulo na noite ou no dia polar.</summary>
    public static (double Sunrise, double Sunset)? SunriseSunset(double latitudeDeg, int dayOfYear)
    {
        double phi = Geo.DegToRad(latitudeDeg);
        double delta = Geo.DegToRad(DeclinationDeg(dayOfYear));
        double cos = (Math.Sin(Geo.DegToRad(-0.833)) - Math.Sin(phi) * Math.Sin(delta)) / (Math.Cos(phi) * Math.Cos(delta));
        if (cos is > 1 or < -1)
        {
            return null;
        }

        double half = Geo.RadToDeg(Math.Acos(cos)) / 15.0;
        return (12 - half, 12 + half);
    }

    /// <summary>Datas usuais: solstícios e equinócio, com os nomes certos para o hemisfério.</summary>
    public static IReadOnlyList<SolarDate> KeyDates(double latitudeDeg)
    {
        bool south = latitudeDeg < 0;
        return new[]
        {
            new SolarDate(south ? "Solstício de inverno (21/06)" : "Solstício de inverno (21/12)", south ? 6 : 12, 21),
            new SolarDate("Equinócio (21/03)", 3, 21),
            new SolarDate(south ? "Solstício de verão (21/12)" : "Solstício de verão (21/06)", south ? 12 : 6, 21),
        };
    }

    /// <summary>Posições do sol acima do horizonte ao longo do dia, com o peso em horas de cada amostra.</summary>
    public static List<(SunPosition Sun, double Hours)> Day(double latitudeDeg, SolarDate date, int stepMinutes)
    {
        var result = new List<(SunPosition, double)>();
        double step = Math.Clamp(stepMinutes, 5, 180) / 60.0;
        for (double h = step / 2; h < 24; h += step)
        {
            SunPosition p = Position(latitudeDeg, date.DayOfYear, h);
            if (p.IsUp)
            {
                result.Add((p, step));
            }
        }

        return result;
    }
}

/// <summary>Copa que faz sombra: elipsoide com opacidade (copas deixam passar parte da luz).</summary>
public sealed record CrownObstacle(string Id, Vec3 Center, double Radius, double HalfHeight, double Opacity)
{
    /// <summary>Verdadeiro quando o raio a partir de <paramref name="origin"/> na direção <paramref name="dir"/> atravessa a copa.</summary>
    public bool Blocks(Vec3 origin, Vec3 dir)
    {
        var o = new Vec3((origin.X - Center.X) / Radius, (origin.Y - Center.Y) / Radius, (origin.Z - Center.Z) / HalfHeight);
        var d = new Vec3(dir.X / Radius, dir.Y / Radius, dir.Z / HalfHeight);
        double a = d.Dot(d), b = 2 * o.Dot(d), c = o.Dot(o) - 1;
        double disc = b * b - 4 * a * c;
        if (disc < 0)
        {
            return false;
        }

        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a), t2 = (-b + sq) / (2 * a);
        return t2 > 1e-6 && (t1 > 1e-6 || c < 0); // à frente do ponto (ou o ponto está dentro da copa)
    }

    /// <summary>Copa adulta (ou na fase escolhida) da espécie plantada em <paramref name="ground"/>.</summary>
    public static CrownObstacle? For(string id, Species species, Vec3 ground, GrowthStage stage)
    {
        (double crown, double height) = GrowthModel.Size(species, stage);
        double r = Math.Max(0.1, crown / 2);
        return species.Category switch
        {
            PlantCategory.Arvore => new CrownObstacle(id, ground + new Vec3(0, 0, height * 0.66), r, Math.Max(0.2, height * 0.34), 0.85),
            PlantCategory.Palmeira => new CrownObstacle(id, ground + new Vec3(0, 0, Math.Max(0.3, height - r * 0.2)), r, Math.Max(0.2, r * 0.4), 0.55),
            PlantCategory.Arbusto or PlantCategory.Trepadeira when height >= 0.8 => new CrownObstacle(id, ground + new Vec3(0, 0, height / 2), r, height / 2, 0.9),
            _ => null,
        };
    }
}

/// <summary>Ponto analisado e as horas de sol direto em cada data.</summary>
public sealed class SolarPoint
{
    public Vec3 Position { get; init; }

    /// <summary>Copa a ignorar (a própria planta, quando o ponto é uma planta).</summary>
    public string IgnoreId { get; init; } = string.Empty;

    public double[] Hours { get; init; } = Array.Empty<double>();

    public double Average => Hours.Length == 0 ? 0 : Hours.Average();
}

/// <summary>Faixas de luz pelo número de horas de sol direto no dia.</summary>
public enum SolarBand
{
    Sombra = 0,
    SombraParcial = 1,
    MeiaSombra = 2,
    SolPleno = 3,
    SolPlenoForte = 4,
}

/// <summary>Como a luz do local atende a exigência da espécie.</summary>
public enum LightFit
{
    Atende = 0,
    Limite = 1,
    NaoAtende = 2,
}

/// <summary>Classificação de luz pelo número de horas de sol direto.</summary>
public static class SolarClasses
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Colors = { "#34457A", "#6E8FC0", "#A8CBA0", "#F1D46A", "#E8923A" };

    public static string Hours(double h) => h.ToString("0.#", Pt);

    /// <summary>Classe de luz (as três da ficha da espécie) pelas horas de sol.</summary>
    public static LightNeed Classify(double hours, SolarSettings s) =>
        hours >= s.FullSunHours ? LightNeed.SolPleno : hours >= s.ShadeHours ? LightNeed.MeiaSombra : LightNeed.Sombra;

    public static SolarBand Band(double hours, SolarSettings s) =>
        hours >= s.FullSunHours + 2 ? SolarBand.SolPlenoForte
        : hours >= s.FullSunHours ? SolarBand.SolPleno
        : hours >= s.ShadeHours ? SolarBand.MeiaSombra
        : hours >= s.PartShadeHours ? SolarBand.SombraParcial
        : SolarBand.Sombra;

    /// <summary>Faixas do mapa e dos quadros (limites em horas), da sombra ao sol forte.</summary>
    public static IReadOnlyList<(double From, double To, string Color, string Label)> Bins(SolarSettings s)
    {
        string full = Hours(s.FullSunHours), strong = Hours(s.FullSunHours + 2), shade = Hours(s.ShadeHours), part = Hours(s.PartShadeHours);
        return new[]
        {
            (0.0, s.PartShadeHours, Colors[0], $"Sombra (menos de {part} h)"),
            (s.PartShadeHours, s.ShadeHours, Colors[1], $"Sombra parcial ({part} a {shade} h)"),
            (s.ShadeHours, s.FullSunHours, Colors[2], $"Meia-sombra ({shade} a {full} h)"),
            (s.FullSunHours, s.FullSunHours + 2, Colors[3], $"Sol pleno ({full} a {strong} h)"),
            (s.FullSunHours + 2, 99.0, Colors[4], $"Sol pleno forte ({strong} h ou mais)"),
        };
    }

    public static int BinOf(double hours, SolarSettings s) => (int)Band(hours, s);

    public static string ColorOf(double hours, SolarSettings s) => Colors[BinOf(hours, s)];

    /// <summary>Faixa de horas que a espécie pede; nula quando a ficha não informa a luz.</summary>
    public static (double Min, double Max)? Range(LightNeed need, SolarSettings s) => need switch
    {
        LightNeed.SolPleno => (s.FullSunHours, double.PositiveInfinity),
        LightNeed.MeiaSombra => (s.ShadeHours, s.FullSunHours),
        LightNeed.Sombra => (0, s.ShadeHours),
        _ => null,
    };

    /// <summary>Exigência da espécie com o critério em horas, por exemplo "sol pleno (6 h ou mais)".</summary>
    public static string NeedText(LightNeed need, SolarSettings s) => need switch
    {
        LightNeed.SolPleno => $"sol pleno ({Hours(s.FullSunHours)} h ou mais)",
        LightNeed.MeiaSombra => $"meia-sombra ({Hours(s.ShadeHours)} a {Hours(s.FullSunHours)} h)",
        LightNeed.Sombra => $"sombra (menos de {Hours(s.ShadeHours)} h)",
        _ => "luz não informada",
    };

    public static LightFit? Fit(LightNeed need, double hours, SolarSettings s)
    {
        if (Range(need, s) is not (double min, double max))
        {
            return null;
        }

        if (hours >= min - 1e-9 && hours <= max + 1e-9)
        {
            return LightFit.Atende;
        }

        double distance = hours < min ? min - hours : hours - max;
        return distance <= s.ToleranceHours + 1e-9 ? LightFit.Limite : LightFit.NaoAtende;
    }
}

/// <summary>Avaliação da luz de uma planta ao longo das datas analisadas.</summary>
public sealed class PlantLight
{
    public LightNeed Need { get; init; }

    public IReadOnlyList<SolarDate> Dates { get; init; } = Array.Empty<SolarDate>();

    public IReadOnlyList<double> Hours { get; init; } = Array.Empty<double>();

    public IReadOnlyList<LightFit> Fits { get; init; } = Array.Empty<LightFit>();

    public double Mean => Hours.Count == 0 ? 0 : Hours.Average();

    public int Failing => Fits.Count(f => f == LightFit.NaoAtende);

    public int Meeting => Fits.Count(f => f != LightFit.NaoAtende);

    /// <summary>Atende quando não há data fora da faixa; "parcial" quando falha na minoria das datas.</summary>
    public LightFit Verdict => Fits.Count == 0 ? LightFit.Atende
        : Failing == 0 ? (Fits.Count(f => f == LightFit.Limite) * 2 > Fits.Count ? LightFit.Limite : LightFit.Atende)
        : Failing * 2 > Fits.Count ? LightFit.NaoAtende : LightFit.Limite;

    public static PlantLight? Evaluate(LightNeed need, IReadOnlyList<double> hours, IReadOnlyList<SolarDate> dates, SolarSettings s)
    {
        if (SolarClasses.Range(need, s) is null || hours.Count == 0)
        {
            return null;
        }

        return new PlantLight
        {
            Need = need,
            Dates = dates,
            Hours = hours.ToList(),
            Fits = hours.Select(h => SolarClasses.Fit(need, h, s) ?? LightFit.Atende).ToList(),
        };
    }

    /// <summary>Datas em que não atende, em texto ("mar–out" no estudo mensal).</summary>
    public string FailingText()
    {
        var failing = Enumerable.Range(0, Fits.Count).Where(i => Fits[i] == LightFit.NaoAtende).ToList();
        if (failing.Count == 0)
        {
            return string.Empty;
        }

        bool monthly = Dates.Count == 12 && Dates.Select(d => d.Month).Distinct().Count() == 12;
        if (monthly)
        {
            MonthSet set = MonthSet.None;
            foreach (int i in failing)
            {
                set = set.With(Dates[i].Month);
            }

            return set.ToString();
        }

        return string.Join(", ", failing.Select(i => Dates[i].Short));
    }

    /// <summary>Texto da avaliação: exigência com o critério, horas recebidas e as datas em que não atende.</summary>
    public string Describe(SolarSettings s)
    {
        int min = Enumerable.Range(0, Hours.Count).OrderBy(i => Hours[i]).First();
        int max = Enumerable.Range(0, Hours.Count).OrderByDescending(i => Hours[i]).First();
        string text = $"pede {SolarClasses.NeedText(Need, s)} e recebe em média {SolarClasses.Hours(Mean)} h de sol direto";
        if (Hours.Count > 1)
        {
            text += $" (mín. {SolarClasses.Hours(Hours[min])} h em {Dates[min].Short}; máx. {SolarClasses.Hours(Hours[max])} h em {Dates[max].Short})";
        }

        text += Failing == 0 ? "." : $"; não atende em {Failing} de {Fits.Count} {(Fits.Count == 12 ? "meses" : "datas")}: {FailingText()}.";
        return text;
    }
}

/// <summary>Sugestões (apenas sugestões) de espécies do catálogo para a luz que o local recebe.</summary>
public static class SolarSuggestions
{
    /// <summary>Mesma categoria, luz compatível com a média do local, porte parecido; nativas primeiro; sem invasoras.</summary>
    public static List<Species> For(Species current, double meanHours, IEnumerable<Species> catalog, SolarSettings s, int max = 3)
    {
        LightNeed wanted = SolarClasses.Classify(meanHours, s);
        double height = Math.Max(0.1, current.AdultHeightM), crown = Math.Max(0.1, current.CrownDiameterM);
        return catalog
            .Where(c => c.Id != current.Id && c.Category == current.Category && c.Light == wanted && c.Origin != PlantOrigin.ExoticaInvasora)
            .OrderBy(c => Math.Abs(Math.Log(Math.Max(0.1, c.AdultHeightM) / height)) + Math.Abs(Math.Log(Math.Max(0.1, c.CrownDiameterM) / crown))
                          - (c.Origin == PlantOrigin.Nativa ? 0.35 : 0))
            .ThenBy(c => c.PopularName, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .ToList();
    }
}

/// <summary>Cálculo das horas de sol direto em pontos, com sombra das copas e de obstáculos externos (edificações).</summary>
public static class SolarStudy
{
    /// <param name="toModel">Converte a direção do sol (norte verdadeiro) para as coordenadas do modelo.</param>
    /// <param name="blockedByBuildings">Teste de obstrução por edificações e terreno (nulo para ignorar).</param>
    public static void Compute(
        IReadOnlyList<SolarPoint> points,
        IReadOnlyList<CrownObstacle> crowns,
        double latitudeDeg,
        IReadOnlyList<SolarDate> dates,
        SolarSettings settings,
        Func<Vec3, Vec3> toModel,
        Func<Vec3, Vec3, bool>? blockedByBuildings,
        CancellationToken cancel = default)
    {
        // Chave de cache por posição do sol arredondada (2° de azimute, 1,5° de altura): as trajetórias de meses
        // simétricos (jan/nov, fev/out...) quase coincidem e a consulta às edificações, que é a parte cara, não se repete.
        var days = dates.Select(d => Sun.Day(latitudeDeg, d, settings.StepMinutes)
            .Select(x => (Dir: toModel(x.Sun.Direction).Normalized(), x.Hours,
                Key: ((int)Math.Round(x.Sun.AzimuthDeg / 2.0), (int)Math.Round(x.Sun.AltitudeDeg / 1.5)))).ToList()).ToList();
        foreach (SolarPoint p in points)
        {
            var buildings = new Dictionary<(int, int), bool>();
            for (int d = 0; d < days.Count; d++)
            {
                double hours = 0;
                foreach ((Vec3 dir, double weight, (int, int) key) in days[d])
                {
                    cancel.ThrowIfCancellationRequested();
                    double light = 1;
                    foreach (CrownObstacle c in crowns)
                    {
                        if (c.Id != p.IgnoreId && c.Blocks(p.Position, dir))
                        {
                            light *= 1 - c.Opacity;
                            if (light < 0.05)
                            {
                                break;
                            }
                        }
                    }

                    if (light > 0.05 && blockedByBuildings is not null)
                    {
                        if (!buildings.TryGetValue(key, out bool blocked))
                        {
                            blocked = blockedByBuildings(p.Position, dir);
                            buildings[key] = blocked;
                        }

                        if (blocked)
                        {
                            light = 0;
                        }
                    }

                    hours += weight * light;
                }

                p.Hours[d] = hours;
            }
        }
    }

    /// <summary>Malha de pontos dentro do contorno, com o lado aumentado para respeitar o máximo de pontos.</summary>
    public static (List<Point2> Points, double Cell) Grid(Polygon2 region, double cell, int maxPoints)
    {
        double size = Math.Max(0.1, cell);
        double area = Math.Max(region.Area, 1e-6);
        if (area / (size * size) > maxPoints)
        {
            size = Math.Sqrt(area / maxPoints);
        }

        for (int attempt = 0; attempt < 6; attempt++)
        {
            var result = new List<Point2>();
            Bounds2 b = region.Bounds;
            for (double y = b.MinY + size / 2; y < b.MaxY; y += size)
            {
                for (double x = b.MinX + size / 2; x < b.MaxX; x += size)
                {
                    var p = new Point2(x, y);
                    if (region.Contains(p))
                    {
                        result.Add(p);
                    }
                }
            }

            if (result.Count <= maxPoints)
            {
                return (result.Count > 0 ? result : new List<Point2> { region.InteriorPoint() }, size);
            }

            size *= 1.15;
        }

        return (new List<Point2> { region.InteriorPoint() }, size);
    }
}
