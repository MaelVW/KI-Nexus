using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KiNexus.App.Markdown;

/// <summary>Ein Codeblock mit Syntax-Farben und Kopier-Knopf (Simpel-Stil).</summary>
public partial class CodeBlockView : UserControl
{
    private readonly string _code;

    public CodeBlockView(string code, string? language)
    {
        InitializeComponent();
        _code = code;
        LanguageText.Text = string.IsNullOrWhiteSpace(language) ? "Code" : language.Trim();
        Editor.Text = code;
        Editor.SyntaxHighlighting = DarkSyntaxColorizer.HighlightingFor(language);
        Editor.TextArea.TextView.LineTransformers.Add(new DarkSyntaxColorizer());
        Editor.TextArea.SelectionBrush = (System.Windows.Media.Brush)FindResource("Brush.AccentDim");
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;

        // Das Mausrad soll den Chat scrollen, nicht im Codeblock hängen bleiben.
        Editor.PreviewMouseWheel += ScrollParent;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_code);
        CopyText.Text = "Kopiert";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyText.Text = "Kopieren";
        };
        timer.Start();
    }

    internal static void ScrollParent(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element)
        {
            return;
        }

        e.Handled = true;
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement;
        parent?.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }
}
