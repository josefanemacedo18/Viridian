using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Viridian.Core.Colors;
using Viridian.Library;

namespace Viridian.UI.Windows;

/// <summary>
/// Seletor de cor com poucos cliques: paleta de cores comuns com nome (flores, folhagens, frutos ou pedras), cores
/// usadas recentemente e conta-gotas na foto da espécie. O código #RRGGBB continua disponível para quem preferir.
/// </summary>
public partial class ColorPickerWindow : ViridianWindow
{
    private static readonly List<Rgb> Recent = new();
    private BitmapSource? _photo;

    /// <param name="current">Cor atual (#RRGGBB) ou vazio.</param>
    /// <param name="photoFile">Foto da espécie (conta-gotas) ou nulo.</param>
    /// <param name="allowEmpty">Permite "Sem cor" (ex.: espécie sem floração vistosa).</param>
    public ColorPickerWindow(string title, ColorPurpose purpose, string current, string? photoFile, bool allowEmpty)
    {
        InitializeComponent();
        Title = "Viridian — " + title;
        HeaderText.Text = title;
        ClearButton.Visibility = allowEmpty ? Visibility.Visible : Visibility.Collapsed;
        foreach (NamedColor named in ColorChoices.For(purpose))
        {
            PalettePanel.Children.Add(SwatchButton(named.Color, named.Name));
        }

        RecentTitle.Visibility = Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (Rgb color in Recent)
        {
            RecentPanel.Children.Add(SwatchButton(color, NameOf(color, purpose)));
        }

        LoadPhoto(photoFile);
        _purpose = purpose;
        HexBox.Text = Rgb.TryParse(current, out Rgb c) ? c.Hex : string.Empty;
        Show(HexBox.Text);
    }

    private readonly ColorPurpose _purpose;

    /// <summary>Cor escolhida (#RRGGBB) ou vazio ("Sem cor").</summary>
    public string Result { get; private set; } = string.Empty;

    private Button SwatchButton(Rgb color, string name)
    {
        var button = new Button
        {
            Width = 92,
            Height = 54,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            ToolTip = $"{name} ({color.Hex})",
            Background = Brushes.Transparent,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Height = 30,
            Margin = new Thickness(3, 3, 3, 1),
            Background = Brush(color),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        });
        stack.Children.Add(new TextBlock { Text = name, FontSize = 10.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0) });
        button.Content = stack;
        button.Click += (_, _) => HexBox.Text = color.Hex;
        button.MouseDoubleClick += (_, _) =>
        {
            HexBox.Text = color.Hex;
            Ok_Click(button, new RoutedEventArgs());
        };
        return button;
    }

    private static SolidColorBrush Brush(Rgb color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static string NameOf(Rgb color, ColorPurpose purpose)
    {
        NamedColor? exact = ColorChoices.For(purpose).Cast<NamedColor?>().FirstOrDefault(n => n!.Value.Color == color);
        return exact?.Name ?? color.Hex;
    }

    private void LoadPhoto(string? file)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(file);
                image.EndInit();
                image.Freeze();
                _photo = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                PhotoImage.Source = _photo;
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            ViridianLog.Warn("Seletor de cor: foto " + file, ex);
        }

        PhotoPanel.Visibility = Visibility.Collapsed;
        PhotoColumn.Width = new GridLength(0);
    }

    /// <summary>Conta-gotas: a cor mediana de um quadrado de 9 × 9 pixels em volta do ponto clicado.</summary>
    private void Photo_Click(object sender, MouseButtonEventArgs e)
    {
        if (_photo is null || PhotoImage.ActualWidth <= 0 || PhotoImage.ActualHeight <= 0)
        {
            return;
        }

        Point p = e.GetPosition(PhotoImage);
        int cx = (int)(p.X / PhotoImage.ActualWidth * _photo.PixelWidth), cy = (int)(p.Y / PhotoImage.ActualHeight * _photo.PixelHeight);
        const int half = 4;
        int x0 = Math.Clamp(cx - half, 0, _photo.PixelWidth - 1), y0 = Math.Clamp(cy - half, 0, _photo.PixelHeight - 1);
        int w = Math.Min(2 * half + 1, _photo.PixelWidth - x0), h = Math.Min(2 * half + 1, _photo.PixelHeight - y0);
        var pixels = new byte[w * h * 4];
        _photo.CopyPixels(new Int32Rect(x0, y0, w, h), pixels, w * 4, 0);
        var list = new List<(byte, byte, byte, byte)>(w * h);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            list.Add((pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]));
        }

        if (ColorChoices.Sample(list) is Rgb color)
        {
            HexBox.Text = color.Hex;
        }
    }

    private void Hex_Changed(object sender, TextChangedEventArgs e) => Show(HexBox.Text);

    private void Show(string text)
    {
        if (ChosenSwatch is null)
        {
            return;
        }

        if (Rgb.TryParse(text, out Rgb color))
        {
            ChosenSwatch.Background = Brush(color);
            ChosenName.Text = NameOf(color, _purpose) is string n && n != color.Hex ? n : string.Empty;
        }
        else
        {
            ChosenSwatch.Background = Brushes.Transparent;
            ChosenName.Text = string.IsNullOrWhiteSpace(text) ? "Sem cor" : "Use o formato #RRGGBB.";
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Result = string.Empty;
        DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Rgb.TryParse(HexBox.Text, out Rgb color))
        {
            MessageBox.Show(this, "Escolha uma cor na paleta ou na foto, ou digite o código no formato #RRGGBB.", "Viridian", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = color.Hex;
        Recent.Remove(color);
        Recent.Insert(0, color);
        if (Recent.Count > 12)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        DialogResult = true;
    }
}
