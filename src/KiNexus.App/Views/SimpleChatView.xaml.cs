using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>
/// Simpel-Stil. Der Code hier kümmert sich nur um die Bedienung:
/// Enter/Esc im Eingabefeld, Bilder per Drag &amp; Drop, automatisch nach unten scrollen.
/// </summary>
public partial class SimpleChatView : UserControl
{
    /// <summary>Nur nach unten mitscrollen, wenn man schon unten war (nicht beim Zurücklesen stören).</summary>
    private bool _stickToBottom = true;

    public SimpleChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ChatSessionViewModel old) { old.Messages.CollectionChanged -= OnMessagesChanged; }
            if (e.NewValue is ChatSessionViewModel session) { session.Messages.CollectionChanged += OnMessagesChanged; }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Dispatcher.BeginInvoke(() => InputBox.Focus());
            }
        };
    }

    private ChatSessionViewModel? Session => DataContext as ChatSessionViewModel;

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (Session is not { } session)
        {
            return;
        }

        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (session.SendCommand.CanExecute(null))
            {
                _stickToBottom = true;
                session.SendCommand.Execute(null);
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (session.IsBusy) { session.Stop(); }
            else if (session.IsEditingLast) { session.CancelEditCommand.Execute(null); }
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _stickToBottom = true;
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            // Der Benutzer hat selbst gescrollt.
            _stickToBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 24;
        }
        else if (_stickToBottom)
        {
            Scroller.ScrollToEnd();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Session?.CanAttachImages == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            Session?.AddImages(files);
        }
    }
}
