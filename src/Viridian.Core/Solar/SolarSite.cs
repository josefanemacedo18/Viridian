using System.Globalization;
using System.Text.RegularExpressions;

namespace Viridian.Core.Solar;

/// <summary>De onde vieram as coordenadas do estudo.</summary>
public enum SiteSource
{
    /// <summary>Localização do projeto no Revit (Gerenciar > Localização).</summary>
    Projeto = 0,

    /// <summary>Digitadas pelo usuário (o projeto não tinha localização confiável).</summary>
    Digitado = 1,
}

/// <summary>Local da obra: latitude e longitude (graus), fuso horário (horas) e nome.</summary>
public sealed record SolarSite(double LatitudeDeg, double LongitudeDeg, double TimeZoneHours, string Name, SiteSource Source)
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Hemisfério sul.</summary>
    public bool South => LatitudeDeg < 0;

    /// <summary>Coordenadas válidas para o estudo (latitude até o círculo polar).</summary>
    public bool IsValid =>
        double.IsFinite(LatitudeDeg) && Math.Abs(LatitudeDeg) <= 66 && double.IsFinite(LongitudeDeg) && Math.Abs(LongitudeDeg) <= 180
        && double.IsFinite(TimeZoneHours) && TimeZoneHours is >= -12 and <= 14;

    /// <summary>Fuso sugerido pela longitude (o usuário confere: fronteiras de fuso seguem divisas, não meridianos).</summary>
    public static double SuggestTimeZone(double longitudeDeg) =>
        double.IsFinite(longitudeDeg) ? Math.Clamp(Math.Round(longitudeDeg / 15.0), -12, 14) : -3;

    public static string Utc(double hours)
    {
        if (Math.Abs(hours) < 1e-9)
        {
            return "UTC";
        }

        int minutes = (int)Math.Round(Math.Abs(hours) * 60);
        string sign = hours < 0 ? "−" : "+";
        return minutes % 60 == 0 ? $"UTC{sign}{minutes / 60}" : $"UTC{sign}{minutes / 60}:{minutes % 60:00}";
    }

    public static string Degrees(double value) => value.ToString("0.0000", Pt).Replace('-', '−') + "°";

    private static readonly Regex Dms = new(
        @"(\d+(?:[.,]\d+)?)\s*[°º]\s*(?:(\d+(?:[.,]\d+)?)\s*['′’]\s*)?(?:(\d+(?:[.,]\d+)?)\s*(?:""|″|”|''|′′)\s*)?([NSLOEW])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Decimal = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

    /// <summary>
    /// Lê coordenadas coladas de mapas: "-22.9068, -43.1729", "-22,9068; -43,1729" ou
    /// 22°54'24.5"S 43°10'22.4"W (S e O/W negativos).
    /// </summary>
    public static bool TryParseCoordinates(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = double.NaN;
        string t = (text ?? string.Empty).Replace('−', '-').Replace('–', '-').Trim();
        if (t.Length == 0)
        {
            return false;
        }

        MatchCollection dms = Dms.Matches(t);
        if (dms.Count == 2)
        {
            double? a = FromDms(dms[0]), b = FromDms(dms[1]);
            if (a is null || b is null)
            {
                return false;
            }

            bool firstIsLat = "NS".Contains(char.ToUpperInvariant(dms[0].Groups[4].Value[0]), StringComparison.Ordinal);
            latitude = firstIsLat ? a.Value : b.Value;
            longitude = firstIsLat ? b.Value : a.Value;
        }
        else
        {
            MatchCollection numbers = Decimal.Matches(t);
            if (numbers.Count != 2 || !TryNumber(numbers[0].Value, out latitude) || !TryNumber(numbers[1].Value, out longitude))
            {
                return false;
            }
        }

        return Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
    }

    private static double? FromDms(Match m)
    {
        if (!TryNumber(m.Groups[1].Value, out double deg))
        {
            return null;
        }

        double min = m.Groups[2].Success && TryNumber(m.Groups[2].Value, out double mm) ? mm : 0;
        double sec = m.Groups[3].Success && TryNumber(m.Groups[3].Value, out double ss) ? ss : 0;
        if (min >= 60 || sec >= 60)
        {
            return null;
        }

        double value = deg + min / 60 + sec / 3600;
        char h = char.ToUpperInvariant(m.Groups[4].Value[0]);
        return h is 'S' or 'O' or 'W' ? -value : value;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>Texto para legendas e relatórios.</summary>
    public string Describe()
    {
        string place = string.IsNullOrWhiteSpace(Name) ? string.Empty : Name.Trim() + " — ";
        string source = Source == SiteSource.Projeto ? "localização do projeto" : "coordenadas digitadas";
        return $"{place}lat. {Degrees(LatitudeDeg)}, long. {Degrees(LongitudeDeg)}, {Utc(TimeZoneHours)} ({source})";
    }

    /// <summary>Hora do relógio em texto (hh:mm), a partir da hora solar do dia.</summary>
    public string ClockText(double solarHour, int dayOfYear)
    {
        double clock = Sun.ClockHour(solarHour, LongitudeDeg, TimeZoneHours, dayOfYear);
        clock = ((clock % 24) + 24) % 24;
        int minutes = (int)Math.Round(clock * 60) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
