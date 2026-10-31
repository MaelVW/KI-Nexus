using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>
/// Gespräch-Stil. Im Code: Push-to-Talk-Taste und Orb mit der Maus (gedrückt halten), Esc zum Unterbrechen,
/// Animationen des Orbs je nach Zustand und automatisch nach unten scrollen.
/// </summary>
public partial class ConversationView : UserControl
{
    private bool _stickToBottom = true;
    private bool _orbPressed;

    public ConversationView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ConversationViewModel old)
            {
                old.PropertyChanged -= OnViewModelChanged;
            }

            if (e.NewValue is ConversationViewModel current)
            {
                current.PropertyChanged += OnViewModelChanged;
                UpdateAnimations(current.State);
            }
        };
    }

    private ConversationViewModel? ViewModel => DataContext as ConversationViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e) => Focus();

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            // Fokus, damit die Push-to-Talk-Taste ankommt. Erst nach dem Umschalten, sonst greift er nicht.
            _ = Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);
            if (ViewModel is { } shown)
            {
                await shown.OnShownAsync();
            }
        }
        else if (ViewModel is { } vm)
        {
            await vm.OnHiddenAsync();
        }
    }

    // ----------------------------------------------------------- Tastatur

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Escape && vm.InterruptCommand.CanExecute(null))
        {
            e.Handled = true;
            await vm.InterruptAsync();
            return;
        }

        if (KeyOf(e) != vm.PushToTalkKey || Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        // Auch Wiederholungen schlucken, sonst löst die Leertaste Knöpfe aus.
        e.Handled = true;
        if (!e.IsRepeat)
        {
            if (vm.IsHandsFree)
            {
                await vm.ToggleListeningCommand.ExecuteAsync(null);
            }
            else
            {
                await vm.PushToTalkDownAsync();
            }
        }
    }

    private async void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (ViewModel is { } vm && KeyOf(e) == vm.PushToTalkKey && Keyboard.FocusedElement is not TextBox)
        {
            e.Handled = true;
            await vm.PushToTalkUpAsync();
        }
    }

    /// <summary>Bei Alt-Kombinationen meldet WPF "System", die echte Taste steht in SystemKey.</summary>
    private static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    // ----------------------------------------------------------- Orb mit der Maus

    private async void OnOrbDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (ViewModel is not { } vm)
        {
            return;
        }

        e.Handled = true;
        if (vm.IsHandsFree)
        {
            await vm.ToggleListeningCommand.ExecuteAsync(null);
            return;
        }

        _orbPressed = true;
        Orb.CaptureMouse();
        await vm.PushToTalkDownAsync();
    }

    private async void OnOrbUp(object sender, MouseButtonEventArgs e)
    {
        if (!_orbPressed)
        {
            return;
        }

        _orbPressed = false;
        Orb.ReleaseMouseCapture();
        if (ViewModel is { } vm)
        {
            await vm.PushToTalkUpAsync();
        }
    }

    private async void OnOrbLostCapture(object sender, MouseEventArgs e)
    {
        if (_orbPressed && ViewModel is { } vm)
        {
            _orbPressed = false;
            await vm.PushToTalkUpAsync();
        }
    }

    // ----------------------------------------------------------- Animationen und Scrollen

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationViewModel.State) && ViewModel is { } vm)
        {
            UpdateAnimations(vm.State);
        }
    }

    /// <summary>Zuhören: Schein pulsiert. Verstehen, Denken, Laden: Ring dreht sich.</summary>
    private void UpdateAnimations(ConversationState state)
    {
        var pulse = (Storyboard)Resources["Pulse"];
        var spin = (Storyboard)Resources["Spin"];
        if (state is ConversationState.Listening or ConversationState.Hearing)
        {
            pulse.Begin(this, isControllable: true);
        }
        else
        {
            pulse.Stop(this);
        }

        if (state is ConversationState.Understanding or ConversationState.Thinking or ConversationState.Starting)
        {
            spin.Begin(this, isControllable: true);
        }
        else
        {
            spin.Stop(this);
        }
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
        {
            _stickToBottom = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 24;
        }
        else if (_stickToBottom)
        {
            Scroller.ScrollToEnd();
        }
    }
}
