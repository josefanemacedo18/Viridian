using System.Windows;

namespace Viridian.UI.Windows;

/// <summary>Pede um nome (e uma descrição opcional), por exemplo ao gravar um modelo de canteiro.</summary>
public partial class TextPromptWindow : ViridianWindow
{
    public TextPromptWindow(string header, string label, string initial, string hint = "", string description = "")
    {
        InitializeComponent();
        HeaderText.Text = header;
        NameLabel.Text = label;
        NameBox.Text = initial;
        DetailBox.Text = description;
        HintText.Text = hint;
        HintText.Visibility = hint.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string Value => NameBox.Text.Trim();

    public string Description => DetailBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            Warn("Informe o nome.");
            NameBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
