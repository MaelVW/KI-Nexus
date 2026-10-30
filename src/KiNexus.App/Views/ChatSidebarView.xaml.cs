using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>Seitenleiste. Hier nur Tastatur: F2 = umbenennen, Entf = löschen, Enter/Esc im Namensfeld.</summary>
public partial class ChatSidebarView : UserControl
{
    public ChatSidebarView()
    {
        InitializeComponent();
    }

    private ChatListViewModel? ViewModel => DataContext as ChatListViewModel;

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel?.SelectedItem is not { IsEditing: false } item || e.OriginalSource is TextBox)
        {
            return;
        }

        if (e.Key == Key.F2)
        {
            ViewModel.StartRenameCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel.DeleteCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatListItem item } || ViewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            ViewModel.CommitRenameCommand.Execute(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelRenameCommand.Execute(item);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatListItem { IsEditing: true } item })
        {
            ViewModel?.CommitRenameCommand.Execute(item);
        }
    }

    /// <summary>Sobald das Namensfeld erscheint: Fokus hinein und alles markieren.</summary>
    private void OnRenameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            // Erst wenn das Feld gezeichnet ist (Priorität "Loaded"), kann es den Fokus sicher bekommen.
            box.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                Keyboard.Focus(box);
                box.SelectAll();
            });
        }
    }
}
