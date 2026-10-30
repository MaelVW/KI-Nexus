using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>
/// Terminal-Stil. Der Code hier verteilt nur die Tasten:
/// Enter = ausführen, Pfeil hoch/runter = frühere Eingaben, Tab = vervollständigen, Strg + C = abbrechen.
/// </summary>
public partial class TerminalChatView : UserControl
{
    public TerminalChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is TerminalViewModel old) { old.ScrollRequested -= OnScrollRequested; }
            if (e.NewValue is TerminalViewModel terminal) { terminal.ScrollRequested += OnScrollRequested; }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    InputBox.Focus();
                    Scroller.ScrollToEnd();
                });
            }
        };
    }

    private TerminalViewModel? Terminal => DataContext as TerminalViewModel;

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (Terminal is not { } terminal)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                terminal.ExecuteCommand.Execute(null);
                break;
            case Key.Up:
                terminal.HistoryUp();
                InputBox.CaretIndex = InputBox.Text.Length;
                break;
            case Key.Down:
                terminal.HistoryDown();
                InputBox.CaretIndex = InputBox.Text.Length;
                break;
            case Key.Tab:
                terminal.Complete();
                InputBox.CaretIndex = InputBox.Text.Length;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control && InputBox.SelectionLength == 0:
                // Wie im echten Terminal: Strg + C ohne Markierung bricht ab.
                terminal.Interrupt();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnScrollRequested(object? sender, EventArgs e) => Scroller.ScrollToEnd();

    /// <summary>Klick irgendwo ins Terminal setzt den Cursor in die Eingabezeile (ausser beim Markieren von Text).</summary>
    private void OnTerminalClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not TextBox && e.OriginalSource is FrameworkElement { TemplatedParent: not TextBoxBase })
        {
            InputBox.Focus();
        }
    }
}
