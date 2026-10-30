using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace KiNexus.App.Markdown;

/// <summary>
/// Zeigt Markdown an. Text lässt sich markieren und kopieren, Links öffnen den Browser.
/// Während eine Antwort streamt, zeichnet die Ansicht höchstens alle 60 ms neu (das spart Rechenzeit),
/// und Codeblöcke sind einfach. Ist die Antwort fertig, kommen die Codeblöcke mit Syntax-Farben.
/// </summary>
public sealed class MarkdownView : RichTextBox
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(MarkdownView), new PropertyMetadata("", OnContentChanged));

    public static readonly DependencyProperty IsStreamingProperty = DependencyProperty.Register(
        nameof(IsStreaming), typeof(bool), typeof(MarkdownView), new PropertyMetadata(false, OnStreamingChanged));

    public static readonly DependencyProperty FlavorProperty = DependencyProperty.Register(
        nameof(Flavor), typeof(MarkdownFlavor), typeof(MarkdownView), new PropertyMetadata(MarkdownFlavor.Bubble, OnContentChanged));

    private static readonly TimeSpan StreamingInterval = TimeSpan.FromMilliseconds(60);

    private readonly DispatcherTimer _throttle;
    private string? _renderedMarkdown;
    private bool _renderedRich;
    private MarkdownFlavor? _renderedFlavor;

    public MarkdownView()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        FocusVisualStyle = null;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, "Brush.Text");
        SetResourceReference(SelectionBrushProperty, "Brush.AccentDim");
        Document = new FlowDocument();

        _throttle = new DispatcherTimer(DispatcherPriority.Background) { Interval = StreamingInterval };
        _throttle.Tick += (_, _) =>
        {
            _throttle.Stop();
            Render();
        };

        PreviewMouseWheel += CodeBlockView.ScrollParent;
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnRequestNavigate));
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public MarkdownFlavor Flavor
    {
        get => (MarkdownFlavor)GetValue(FlavorProperty);
        set => SetValue(FlavorProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (view.IsStreaming)
        {
            // Nicht bei jedem Wort neu zeichnen, sondern höchstens alle 60 ms.
            if (!view._throttle.IsEnabled)
            {
                view._throttle.Start();
            }
        }
        else
        {
            view.Render();
        }
    }

    private static void OnStreamingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (!(bool)e.NewValue)
        {
            view._throttle.Stop();
            view.Render();
        }
    }

    private void Render()
    {
        var markdown = Markdown ?? "";
        var rich = !IsStreaming;
        // Auch die Variante gehört dazu: WPF setzt Markdown oft vor Flavor. Dann muss nochmals gezeichnet werden.
        if (markdown == _renderedMarkdown && rich == _renderedRich && Flavor == _renderedFlavor)
        {
            return;
        }

        Document = new MarkdownRenderer(Flavor, richCodeBlocks: rich).Render(markdown);
        _renderedMarkdown = markdown;
        _renderedRich = rich;
        _renderedFlavor = Flavor;
    }

    private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Nur Links, die der Benutzer anklickt, und nur http(s) oder mailto (prüft der Renderer).
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
