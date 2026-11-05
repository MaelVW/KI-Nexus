using System.Windows;
using KiNexus.App.Views;
using KiNexus.Core.Chats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace KiNexus.App.Services;

/// <summary>Import einer Datei: kopieren (Original bleibt) oder verschieben (spart Platz).</summary>
public enum ImportMode
{
    Copy,
    Move,
}

/// <summary>Öffnet Dialoge. So brauchen ViewModels keine Fenster zu kennen.</summary>
public interface IDialogService
{
    void ShowSettings();

    /// <summary>Lässt einen Ordner wählen. Null, wenn abgebrochen.</summary>
    string? PickFolder(string title, string? initialPath);

    /// <summary>Lässt Bilder wählen (png, jpg, gif, bmp). Leer, wenn abgebrochen.</summary>
    IReadOnlyList<string> PickImages(string title = "Bilder anhängen");

    /// <summary>Fragt nach, z.B. vor dem Löschen. True = bestätigt.</summary>
    bool Confirm(string title, string message, string confirmText);

    /// <summary>Lässt GGUF-Dateien wählen (Modell und optional mmproj). Leer, wenn abgebrochen.</summary>
    IReadOnlyList<string> PickGgufFiles();

    /// <summary>Fragt beim Import: kopieren oder verschieben? Null = abgebrochen.</summary>
    ImportMode? AskCopyOrMove(string fileName, string sizeText);

    /// <summary>Lässt einen Datensatz wählen (JSONL oder JSON). Null, wenn abgebrochen.</summary>
    string? PickDatasetFile();

    /// <summary>Lässt ein VRM-Modell wählen. Null, wenn abgebrochen.</summary>
    string? PickAvatarFile();
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

    public IReadOnlyList<string> PickImages(string title = "Bilder anhängen")
    {
        var patterns = string.Join(";", AttachmentStore.AllowedExtensions.Select(e => "*" + e));
        var dialog = new OpenFileDialog
        {
            Title = title,
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

    public IReadOnlyList<string> PickGgufFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "GGUF-Modell importieren (optional mit mmproj-Datei)",
            Filter = "GGUF-Modelle (*.gguf)|*.gguf",
            Multiselect = true,
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileNames : [];
    }

    public string? PickDatasetFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Datensatz importieren",
            Filter = "Datensätze (*.jsonl;*.json)|*.jsonl;*.json|Alle Dateien (*.*)|*.*",
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickAvatarFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Avatar wählen (VRM)",
            Filter = "VRM-Modelle (*.vrm)|*.vrm",
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public ImportMode? AskCopyOrMove(string fileName, string sizeText)
    {
        var dialog = new ChoiceDialog(
            "Kopieren oder verschieben?",
            $"{fileName} ({sizeText}) kommt in den Ordner models/.\n\n"
            + "Kopieren: Das Original bleibt, braucht aber doppelt Platz.\n"
            + "Verschieben: Die Datei zieht um und ist danach nur noch in models/. Spart Platz.",
            primaryText: "Kopieren",
            secondaryText: "Verschieben")
        {
            Owner = Application.Current.MainWindow,
        };
        return dialog.ShowDialog() == true ? (dialog.ChoseSecondary ? ImportMode.Move : ImportMode.Copy) : null;
    }
}
