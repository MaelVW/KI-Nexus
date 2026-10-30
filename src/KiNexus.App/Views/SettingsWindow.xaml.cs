using System.Windows;
using System.Windows.Media;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>
/// Dialog für die App-Einstellungen. Die Logik steckt im SettingsViewModel.
/// "Abbrechen" und die Esc-Taste schliessen den Dialog ohne zu speichern (IsCancel).
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => DialogResult = true;
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this, (Color)FindResource("Color.Border"));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
