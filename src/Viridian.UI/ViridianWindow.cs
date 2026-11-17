using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Viridian.UI;

/// <summary>Janela base do Viridian: carrega o tema, o idioma e o vínculo com a janela do Revit.</summary>
public class ViridianWindow : Window
{
    public ViridianWindow()
    {
        Resources.MergedDictionaries.Add(new Themes.ViridianTheme());

        Language = XmlLanguage.GetLanguage("pt-BR");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        UseLayoutRounding = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (TryFindResource("VirBackground") is Brush background)
        {
            Background = background;
        }

        if (TryFindResource("VirText") is Brush foreground)
        {
            Foreground = foreground;
        }
    }

    /// <summary>Define a janela principal do Revit como proprietária, mantendo o diálogo sempre à frente dela.</summary>
    public void SetOwnerHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            new WindowInteropHelper(this).Owner = handle;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    protected void Warn(string message) =>
        MessageBox.Show(this, message, "Viridian", MessageBoxButton.OK, MessageBoxImage.Warning);

    protected void Inform(string message) =>
        MessageBox.Show(this, message, "Viridian", MessageBoxButton.OK, MessageBoxImage.Information);

    protected bool Confirm(string message) =>
        MessageBox.Show(this, message, "Viridian", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

/// <summary>Leitura e escrita de números na notação brasileira (vírgula decimal), aceitando também ponto.</summary>
public static class Num
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        string t = (text ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (t.Length == 0)
        {
            return false;
        }

        // "1.234,5" → 1234.5 ; "1,5" → 1.5 ; "1.5" → 1.5
        if (t.Contains(',', StringComparison.Ordinal))
        {
            t = t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static bool TryParseInt(string? text, out int value) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (!TryParse(text, out double number))
        {
            return false;
        }

        try
        {
            value = (decimal)number;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static string Format(double value, string format = "0.##") => value.ToString(format, PtBr);

    /// <summary>Lê um número ou devolve o valor padrão quando o campo está vazio ou inválido.</summary>
    public static double ParseOr(string? text, double fallback) => TryParse(text, out double value) ? value : fallback;
}

/// <summary>Item de lista com texto de apresentação e valor associado.</summary>
public sealed class Choice<T>
{
    public Choice(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public T Value { get; }

    public override string ToString() => Label;
}
