using System.Windows;
using System.Windows.Controls;
using KiNexus.Core.Kis;

namespace KiNexus.App.Views;

public partial class SystemPromptView : UserControl
{
    public SystemPromptView() => InitializeComponent();

    /// <summary>
    /// Vorlage an der Stelle des Cursors einfügen (oder statt der Markierung). Das geht nur hier in der Ansicht,
    /// das ViewModel kennt den Cursor nicht.
    /// </summary>
    private void OnInsertTemplate(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PromptTemplate template })
        {
            return;
        }

        var start = PromptEditor.SelectionStart;
        var before = PromptEditor.Text[..start];
        var text = before.Length > 0 && !before.EndsWith('\n') ? "\n" + template.Text : template.Text;
        PromptEditor.SelectedText = text;
        PromptEditor.Select(start + text.Length, 0);
        PromptEditor.Focus();
    }

    /// <summary>Beim Test-Chat immer die neuste Zeile zeigen, auch während die Antwort wächst.</summary>
    private void OnTestScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0)
        {
            TestScroll.ScrollToEnd();
        }
    }
}
