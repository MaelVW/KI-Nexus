using System.Windows;
using KiNexus.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace KiNexus.App.Services;

/// <summary>Öffnet Dialoge. So brauchen ViewModels keine Fenster zu kennen.</summary>
public interface IDialogService
{
    void ShowSettings();

    /// <summary>Lässt einen Ordner wählen. Null, wenn abgebrochen.</summary>
    string? PickFolder(string title, string? initialPath);
}

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public void ShowSettings()
    {
        var window = services.GetRequiredService<SettingsWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    public string? PickFolder(string title, string? initialPath)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }
}
