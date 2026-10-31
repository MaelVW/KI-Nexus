using System.Windows;
using System.Windows.Media;

namespace KiNexus.App.Views;

/// <summary>Rückfrage mit zwei Wegen. Der erste (hervorgehoben) ist die Empfehlung.</summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog(string title, string message, string primaryText, string secondaryText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this, (Color)FindResource("Color.Border"));
    }

    /// <summary>True, wenn der zweite Weg gewählt wurde.</summary>
    public bool ChoseSecondary { get; private set; }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        ChoseSecondary = true;
        DialogResult = true;
    }
}
