using System.Windows;
using System.Windows.Media;

namespace KiNexus.App.Views;

/// <summary>Einfache Rückfrage mit "Abbrechen" (Esc) und einem roten Bestätigen-Knopf.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this, (Color)FindResource("Color.Border"));
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
