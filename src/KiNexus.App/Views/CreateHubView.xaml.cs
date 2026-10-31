using System.Windows;
using System.Windows.Controls;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>Create-Hub. Im Code nur Drag &amp; Drop: GGUF-Dateien gehen an den Import.</summary>
public partial class CreateHubView : UserControl
{
    public CreateHubView()
    {
        InitializeComponent();
    }

    private CreateHubViewModel? Hub => DataContext as CreateHubViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && !(Hub?.Import.IsImporting ?? true) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Hub is { } hub && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            e.Handled = true;
            await hub.ImportDroppedFilesAsync(files);
        }
    }
}
