using System.Windows.Controls;
using System.Windows.Input;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>
/// Test-Feld (M1). Der Code hier kümmert sich nur um Tastatur und Scrollen.
/// </summary>
public partial class EngineTestView : UserControl
{
    public EngineTestView()
    {
        InitializeComponent();
    }

    /// <summary>Enter sendet, Shift + Enter macht eine neue Zeile.</summary>
    private void OnQuestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        if (DataContext is EngineTestViewModel { SendCommand: var send } && send.CanExecute(null))
        {
            send.Execute(null);
        }
    }

    /// <summary>Beim Streamen immer ans Ende scrollen, damit man die neuen Wörter sieht.</summary>
    private void OnOutputChanged(object sender, TextChangedEventArgs e) => OutputScroller.ScrollToEnd();
}
