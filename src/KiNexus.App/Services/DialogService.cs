using System.Windows;
using KiNexus.App.Views;
using KiNexus.Core.Chats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace KiNexus.App.Services;

/// <summary>Öffnet Dialoge. So brauchen ViewModels keine Fenster zu kennen.</summary>
public interface IDialogService
{
    void ShowSettings();

    /// <summary>Lässt einen Ordner wählen. Null, wenn abgebrochen.</summary>
    string? PickFolder(string title, string? initialPath);

    /// <summary>Lässt Bilder wählen (png, jpg, gif, bmp). Leer, wenn abgebrochen.</summary>
    IReadOnlyList<string> PickImages();

    /// <summary>Fragt nach, z.B. vor dem Löschen. True = bestätigt.</summary>
    bool Confirm(string title, string message, string confirmText);
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

    public IReadOnlyList<string> PickImages()
    {
        var patterns = string.Join(";", AttachmentStore.AllowedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = "Bilder anhängen",
            Filter = $"Bilder ({patterns})|{patterns}",
            Multiselect = true,
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileNames : [];
    }

    public bool Confirm(string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(title, message, confirmText) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }
}
