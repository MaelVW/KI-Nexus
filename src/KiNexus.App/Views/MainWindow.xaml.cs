using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using KiNexus.App.ViewModels;
using KiNexus.Core.Settings;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.Views;

/// <summary>
/// Hauptfenster. Der Code hier kümmert sich nur um das Fenster selbst
/// (Titelleiste, Grösse, Lage). Die Logik steckt im MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;
    private bool _isStateSaved;

    public MainWindow(MainViewModel viewModel, ISettingsService settings, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;

        RestorePlacement(settings.Current.Window);
        SourceInitialized += (_, _) => NativeWindow.ApplyDarkFrame(this, (Color)FindResource("Color.Border"));
        Loaded += (_, _) => UpdateMaximizedLayout();
        StateChanged += (_, _) => UpdateMaximizedLayout();
    }

    private void RestorePlacement(WindowSettings saved)
    {
        Width = saved.Width;
        Height = saved.Height;

        var isVisible = saved.IsOnScreen(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        if (isVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left!.Value;
            Top = saved.Top!.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (saved.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void UpdateMaximizedLayout()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        RootGrid.Margin = isMaximized ? NativeWindow.GetMaximizedOverhang(this) : new Thickness(0);
        MaximizeButton.Content = isMaximized ? Glyphs.Restore : Glyphs.Maximize;
        MaximizeButton.ToolTip = isMaximized ? "Verkleinern" : "Maximieren";
    }

    /// <summary>
    /// Vor dem Schliessen Grösse, Lage und Tab speichern.
    /// Weil das Speichern asynchron ist, brechen wir das erste Schliessen ab und schliessen danach neu.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isStateSaved || e.Cancel)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            await _viewModel.SaveWindowStateAsync(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Fensterzustand konnte nicht gespeichert werden.");
        }
        finally
        {
            _isStateSaved = true;
            // Nicht direkt Close() aufrufen: Wir könnten noch mitten in OnClosing sein.
            _ = Dispatcher.InvokeAsync(Close);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
