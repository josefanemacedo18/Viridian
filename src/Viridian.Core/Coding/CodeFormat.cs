using System.Globalization;
using Viridian.Core.Model;

namespace Viridian.Core.Coding;

/// <summary>Formação e leitura dos códigos de identificação (espécies, exemplares e maciços).</summary>
public static class CodeFormat
{
    /// <summary>Monta um código sequencial como AR-001.</summary>
    public static string Sequential(string prefix, int number, int digits = 3) =>
        $"{prefix}-{Math.Max(0, number).ToString(new string('0', Math.Clamp(digits, 1, 8)), CultureInfo.InvariantCulture)}";

    public static bool TryParseSequential(string? code, string prefix, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string text = code.Trim();
        string head = prefix + "-";
        if (!text.StartsWith(head, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(text.AsSpan(head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Próximo código de espécie livre dentro da categoria.</summary>
    public static string NextSpeciesCode(IEnumerable<string> existingCodes, PlantCategory category)
    {
        string prefix = Labels.Prefix(category);
        int max = 0;
        foreach (string code in existingCodes)
        {
            if (TryParseSequential(code, prefix, out int number) && number > max)
            {
                max = number;
            }
        }

        return Sequential(prefix, max + 1);
    }
}

/// <summary>
/// Formato do código de localização dos exemplares no projeto.
/// Aceita os marcadores {CAT} (prefixo da categoria), {ESP} (código da espécie) e {N} (número sequencial).
/// </summary>
public sealed class InstanceCodeFormat
{
    public const string DefaultTemplate = "{CAT}.{N}";

    public string Template { get; set; } = DefaultTemplate;

    public int Digits { get; set; } = 4;

    public bool IsValid => !string.IsNullOrWhiteSpace(Template) && Template.Contains("{N}", StringComparison.Ordinal);

    public void Normalize()
    {
        if (!IsValid)
        {
            Template = DefaultTemplate;
        }

        Digits = Math.Clamp(Digits, 1, 8);
    }

    /// <summary>Parte fixa do código antes do número (define a série de numeração).</summary>
    public string Head(PlantCategory category, string speciesCode)
    {
        string template = IsValid ? Template : DefaultTemplate;
        int index = template.IndexOf("{N}", StringComparison.Ordinal);
        return Expand(template[..index], category, speciesCode);
    }

    /// <summary>Parte fixa do código depois do número.</summary>
    public string Tail(PlantCategory category, string speciesCode)
    {
        string template = IsValid ? Template : DefaultTemplate;
        int index = template.IndexOf("{N}", StringComparison.Ordinal);
        return Expand(template[(index + 3)..], category, speciesCode);
    }

    public string Format(PlantCategory category, string speciesCode, int number)
    {
        string digits = Math.Max(0, number).ToString(new string('0', Math.Clamp(Digits, 1, 8)), CultureInfo.InvariantCulture);
        return Head(category, speciesCode) + digits + Tail(category, speciesCode);
    }

    /// <summary>Extrai o número de um código que pertença à série informada.</summary>
    public bool TryParse(string? code, PlantCategory category, string speciesCode, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string text = code.Trim();
        string head = Head(category, speciesCode);
        string tail = Tail(category, speciesCode);
        if (text.Length <= head.Length + tail.Length
            || !text.StartsWith(head, StringComparison.OrdinalIgnoreCase)
            || !text.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(
            text.AsSpan(head.Length, text.Length - head.Length - tail.Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static string Expand(string text, PlantCategory category, string speciesCode) =>
        text.Replace("{CAT}", Labels.Prefix(category), StringComparison.Ordinal)
            .Replace("{ESP}", speciesCode ?? string.Empty, StringComparison.Ordinal);
}

/// <summary>Distribui números sequenciais por série, continuando a partir dos códigos já existentes.</summary>
public sealed class InstanceCodeAllocator
{
    private readonly InstanceCodeFormat _format;
    private readonly Dictionary<string, int> _next = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public InstanceCodeAllocator(InstanceCodeFormat format)
    {
        _format = format;
    }

    /// <summary>Registra um código já existente no projeto para que não seja reutilizado.</summary>
    public void Register(string? code, PlantCategory category, string speciesCode)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _used.Add(code.Trim());
        if (_format.TryParse(code, category, speciesCode, out int number))
        {
            string series = SeriesKey(category, speciesCode);
            if (!_next.TryGetValue(series, out int next) || number >= next)
            {
                _next[series] = number + 1;
            }
        }
    }

    public bool IsUsed(string? code) => !string.IsNullOrWhiteSpace(code) && _used.Contains(code.Trim());

    /// <summary>Gera o próximo código livre da série.</summary>
    public string Next(PlantCategory category, string speciesCode)
    {
        string series = SeriesKey(category, speciesCode);
        int number = _next.TryGetValue(series, out int next) ? next : 1;
        string code = _format.Format(category, speciesCode, number);
        while (_used.Contains(code))
        {
            number++;
            code = _format.Format(category, speciesCode, number);
        }

        _next[series] = number + 1;
        _used.Add(code);
        return code;
    }

    private string SeriesKey(PlantCategory category, string speciesCode) =>
        _format.Head(category, speciesCode) + "|" + _format.Tail(category, speciesCode);
}
