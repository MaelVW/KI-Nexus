using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KiNexus.App.ViewModels;

namespace KiNexus.App.Views;

/// <summary>Hugging Face. Im Code: Enter startet die Suche, und der Token kommt aus dem Passwort-Feld.</summary>
public partial class HuggingFaceView : UserControl
{
    public HuggingFaceView()
    {
        InitializeComponent();
    }

    private HuggingFaceViewModel? ViewModel => DataContext as HuggingFaceViewModel;

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel?.SearchCommand.CanExecute(null) == true)
        {
            e.Handled = true;
            ViewModel.SearchCommand.Execute(null);
        }
    }

    /// <summary>PasswordBox lässt sich absichtlich nicht binden. Der Token geht direkt in den verschlüsselten Speicher.</summary>
    private void OnSaveTokenClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveToken(TokenBox.Password);
        TokenBox.Clear();
    }
}
