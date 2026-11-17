using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Viridian.UI;

/// <summary>Janela base do Viridian: carrega o tema, o idioma, a acessibilidade e o vínculo com a janela do Revit.</summary>
public class ViridianWindow : Window
{
    /// <summary>Ampliação e contraste aplicados a todas as janelas (definidos a partir das configurações).</summary>
    public static Core.Config.AccessibilitySettings Accessibility { get; set; } = new();

    public ViridianWindow()
    {
        Resources.MergedDictionaries.Add(new Themes.ViridianTheme());
        if (Accessibility.HighContrast || SystemParameters.HighContrast)
        {
            ApplyHighContrast();
        }

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

        SourceInitialized += (_, _) => ApplyScale();
    }

    /// <summary>Amplia todo o conteúdo da janela (textos, campos e pré-visualizações) e o tamanho dela, sem passar da área de trabalho.</summary>
    private void ApplyScale()
    {
        double scale = Accessibility.TextScale;
        if (scale <= 1.001 || Content is not FrameworkElement root)
        {
            return;
        }

        root.LayoutTransform = new ScaleTransform(scale, scale);
        Rect work = SystemParameters.WorkArea;
        if (SizeToContent is not SizeToContent.WidthAndHeight and not SizeToContent.Width && !double.IsNaN(Width))
        {
            Width = Math.Min(Width * scale, work.Width);
        }

        if (SizeToContent is not SizeToContent.WidthAndHeight and not SizeToContent.Height && !double.IsNaN(Height))
        {
            Height = Math.Min(Height * scale, work.Height);
        }

        MinWidth = Math.Min(MinWidth * scale, work.Width);
        MinHeight = Math.Min(MinHeight * scale, work.Height);
    }

    /// <summary>Troca as cores do tema por cores do sistema em alto contraste (ou por tons mais fortes).</summary>
    private void ApplyHighContrast()
    {
        bool system = SystemParameters.HighContrast;
        Brush Solid(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
        Resources["VirBackground"] = system ? SystemColors.WindowBrush : Brushes.White;
        Resources["VirSurface"] = system ? SystemColors.WindowBrush : Brushes.White;
        Resources["VirText"] = system ? SystemColors.WindowTextBrush : Brushes.Black;
        Resources["VirMuted"] = system ? SystemColors.WindowTextBrush : Solid("#2B332E");
        Resources["VirBorder"] = system ? SystemColors.WindowTextBrush : Solid("#3A4A40");
        Resources["VirGreen"] = system ? SystemColors.HighlightBrush : Solid("#14402C");
        Resources["VirGreenDark"] = system ? SystemColors.WindowTextBrush : Solid("#0B2A1C");
        Resources["VirWarning"] = system ? SystemColors.WindowTextBrush : Solid("#6B3A00");
        Resources["VirError"] = system ? SystemColors.WindowTextBrush : Solid("#7A1010");
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

    /// <summary>Lê um inteiro aceitando separador de milhar brasileiro ("2.000").</summary>
    public static bool TryParseInt(string? text, out int value)
    {
        string t = (text ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

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
