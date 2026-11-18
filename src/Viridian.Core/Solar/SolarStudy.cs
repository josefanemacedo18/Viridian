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

    /// <summary>Horas abaixo das quais o local é de sombra.</summary>
    public double ShadeHours { get; set; } = 3;

    public void Normalize()
    {
        LatitudeDeg = double.IsFinite(LatitudeDeg) ? Math.Clamp(LatitudeDeg, -66, 66) : -23.55;
        NorthAdjustDeg = double.IsFinite(NorthAdjustDeg) ? Math.Clamp(NorthAdjustDeg, -180, 180) : 0;
        StepMinutes = Math.Clamp(StepMinutes <= 0 ? 30 : StepMinutes, 10, 120);
        GridM = double.IsFinite(GridM) ? Math.Clamp(GridM, 0.25, 20) : 1.0;
        MaxPoints = Math.Clamp(MaxPoints <= 0 ? 900 : MaxPoints, 50, 5000);
        FullSunHours = double.IsFinite(FullSunHours) ? Math.Clamp(FullSunHours, 1, 14) : 6;
        ShadeHours = double.IsFinite(ShadeHours) ? Math.Clamp(ShadeHours, 0, FullSunHours) : 3;
    }
}

/// <summary>Data do estudo (dia e mês; o ano não influi na posição do sol para este fim).</summary>
public sealed record SolarDate(string Name, int Month, int Day)
{
    public int DayOfYear => new DateTime(2025, Month, Day).DayOfYear;
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

/// <summary>Classificação de luz pelo número de horas de sol direto.</summary>
public static class SolarClasses
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static LightNeed Classify(double hours, SolarSettings s) =>
        hours >= s.FullSunHours ? LightNeed.SolPleno : hours >= s.ShadeHours ? LightNeed.MeiaSombra : LightNeed.Sombra;

    /// <summary>Faixas do mapa (limites em horas) e cores, da sombra ao sol forte.</summary>
    public static readonly IReadOnlyList<(double From, double To, string Color, string Label)> Bins = new[]
    {
        (0.0, 2.0, "#34457A", "menos de 2 h"),
        (2.0, 4.0, "#6E8FC0", "2 a 4 h"),
        (4.0, 6.0, "#A8CBA0", "4 a 6 h"),
        (6.0, 8.0, "#F1D46A", "6 a 8 h"),
        (8.0, 99.0, "#E8923A", "8 h ou mais"),
    };

    public static int BinOf(double hours)
    {
        for (int i = 0; i < Bins.Count; i++)
        {
            if (hours < Bins[i].To)
            {
                return i;
            }
        }

        return Bins.Count - 1;
    }

    /// <summary>Observação quando a luz do local não combina com a exigência da espécie (nula quando combina).</summary>
    public static string? Mismatch(Species species, double hours, double winterHours, SolarSettings s)
    {
        string h = hours.ToString("0.#", Pt);
        return species.Light switch
        {
            LightNeed.SolPleno when hours < s.FullSunHours - 1 =>
                $"pede sol pleno e recebe cerca de {h} h de sol direto (média das datas; {winterHours.ToString("0.#", Pt)} h no inverno).",
            LightNeed.MeiaSombra when hours > s.FullSunHours + 2 =>
                $"pede meia-sombra e recebe cerca de {h} h de sol direto: risco de queimar as folhas.",
            LightNeed.MeiaSombra when hours < Math.Max(1, s.ShadeHours - 1.5) =>
                $"pede meia-sombra e recebe só cerca de {h} h de sol direto.",
            LightNeed.Sombra when hours > s.ShadeHours + 1 =>
                $"pede sombra e recebe cerca de {h} h de sol direto.",
            _ => null,
        };
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
        var days = dates.Select(d => Sun.Day(latitudeDeg, d, settings.StepMinutes)
            .Select(x => (Dir: toModel(x.Sun.Direction).Normalized(), x.Hours)).ToList()).ToList();
        foreach (SolarPoint p in points)
        {
            for (int d = 0; d < days.Count; d++)
            {
                double hours = 0;
                foreach ((Vec3 dir, double weight) in days[d])
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

                    if (light > 0.05 && blockedByBuildings is not null && blockedByBuildings(p.Position, dir))
                    {
                        light = 0;
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
