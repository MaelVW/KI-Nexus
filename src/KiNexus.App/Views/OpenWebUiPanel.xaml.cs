using System.Windows.Controls;

namespace KiNexus.App.Views;

/// <summary>Panel von Alternate Launch. Im Code nur: das Log bleibt unten (neueste Zeilen sichtbar).</summary>
public partial class OpenWebUiPanel : UserControl
{
    public OpenWebUiPanel()
    {
        InitializeComponent();
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
