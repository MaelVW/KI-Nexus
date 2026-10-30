using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;

namespace KiNexus.App.Markdown;

/// <summary>Wie Markdown aussehen soll.</summary>
public enum MarkdownFlavor
{
    /// <summary>Simpel-Stil: normale Schrift, Codeblöcke mit Syntax-Farben und Kopier-Knopf.</summary>
    Bubble,

    /// <summary>Terminal-Stil: alles in Cascadia Mono, Codeblöcke nur mit Rahmen.</summary>
    Terminal,
}

/// <summary>
/// Macht aus Markdown ein WPF-Dokument (FlowDocument). Ohne Web-Technik (Bauplan, Kapitel 4).
/// Markdig liest den Text und baut einen Baum (AST). Diese Klasse geht den Baum durch und baut daraus WPF-Elemente.
/// </summary>
public sealed class MarkdownRenderer(MarkdownFlavor flavor, bool richCodeBlocks)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    private readonly FontFamily _mono = Resource<FontFamily>("Font.Mono");
    private readonly FontFamily _text = flavor == MarkdownFlavor.Terminal ? Resource<FontFamily>("Font.Mono") : Resource<FontFamily>("Font.Ui");

    public FlowDocument Render(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = _text,
            FontSize = flavor == MarkdownFlavor.Terminal ? 14 : 15,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            LineHeight = flavor == MarkdownFlavor.Terminal ? 21 : 23,
        };

        var tree = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
        foreach (var block in tree)
        {
            if (RenderBlock(block) is { } rendered)
            {
                document.Blocks.Add(rendered);
            }
        }

        // Der letzte Absatz braucht keinen Abstand nach unten.
        if (document.Blocks.LastBlock is { } last)
        {
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        }

        return document;
    }

    private WpfBlock? RenderBlock(MdBlock block) => block switch
    {
        HeadingBlock heading => RenderHeading(heading),
        ParagraphBlock paragraph => WithSpacing(new Paragraph().AddInlines(RenderInlines(paragraph.Inline))),
        FencedCodeBlock fenced => RenderCode(fenced, fenced.Info),
        CodeBlock code => RenderCode(code, null),
        ListBlock list => RenderList(list),
        QuoteBlock quote => RenderQuote(quote),
        ThematicBreakBlock => RenderRule(),
        MdTable table => RenderTable(table),
        HtmlBlock html => WithSpacing(new Paragraph(new Run(html.Lines.ToString()))),
        _ => null,
    };

    private Paragraph RenderHeading(HeadingBlock heading)
    {
        var paragraph = new Paragraph().AddInlines(RenderInlines(heading.Inline));
        paragraph.FontWeight = FontWeights.SemiBold;
        if (flavor == MarkdownFlavor.Bubble)
        {
            paragraph.FontFamily = Resource<FontFamily>("Font.UiDisplay");
            paragraph.FontSize = heading.Level switch { 1 => 22, 2 => 19, 3 => 17, _ => 15 };
        }
        else
        {
            paragraph.Foreground = Resource<Brush>("Brush.Accent");
        }

        paragraph.Margin = new Thickness(0, 6, 0, 6);
        return paragraph;
    }

    private WpfBlock RenderCode(LeafBlock code, string? language)
    {
        var text = code.Lines.ToString().TrimEnd('\n', '\r');
        if (richCodeBlocks && flavor == MarkdownFlavor.Bubble)
        {
            return new BlockUIContainer(new CodeBlockView(text, language)) { Margin = new Thickness(0, 4, 0, 12) };
        }

        // Einfacher Codeblock: Rahmen und Mono-Schrift (Terminal, oder während die Antwort noch streamt).
        var paragraph = new Paragraph(new Run(text))
        {
            FontFamily = _mono,
            FontSize = 13,
            Margin = new Thickness(0),
            LineHeight = 19,
        };
        return new Section(paragraph)
        {
            BorderBrush = Resource<Brush>("Brush.Border"),
            BorderThickness = new Thickness(1),
            Background = Resource<Brush>(flavor == MarkdownFlavor.Terminal ? "Brush.Background" : "Brush.SurfaceLight"),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 12),
        };
    }

    private WpfList RenderList(ListBlock list)
    {
        var wpfList = new WpfList
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(22, 0, 0, 0),
        };
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start))
        {
            wpfList.StartIndex = start;
        }

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
            {
                if (RenderBlock(child) is { } rendered)
                {
                    rendered.Margin = new Thickness(0, 0, 0, 3);
                    listItem.Blocks.Add(rendered);
                }
            }

            wpfList.ListItems.Add(listItem);
        }

        return wpfList;
    }

    private Section RenderQuote(QuoteBlock quote)
    {
        var section = new Section
        {
            BorderBrush = Resource<Brush>("Brush.AccentDim"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 2, 0, 2),
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Resource<Brush>("Brush.TextMuted"),
        };
        foreach (var child in quote)
        {
            if (RenderBlock(child) is { } rendered)
            {
                section.Blocks.Add(rendered);
            }
        }

        return section;
    }

    private WpfBlock RenderRule() => new BlockUIContainer(new System.Windows.Controls.Border
    {
        Height = 1,
        Background = Resource<Brush>("Brush.Border"),
        Margin = new Thickness(0, 6, 0, 6),
    });

    private WpfTable RenderTable(MdTable table)
    {
        var wpfTable = new WpfTable
        {
            CellSpacing = 0,
            BorderBrush = Resource<Brush>("Brush.Border"),
            BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(0, 4, 0, 12),
        };
        var columns = table.OfType<MdTableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (var i = 0; i < columns; i++)
        {
            wpfTable.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        foreach (var row in table.OfType<MdTableRow>())
        {
            var wpfRow = new TableRow();
            if (row.IsHeader)
            {
                wpfRow.FontWeight = FontWeights.SemiBold;
                wpfRow.Background = Resource<Brush>("Brush.SurfaceLight");
            }

            foreach (var cell in row.OfType<MdTableCell>())
            {
                var wpfCell = new TableCell
                {
                    BorderBrush = Resource<Brush>("Brush.Border"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 4, 8, 4),
                };
                foreach (var child in cell)
                {
                    if (RenderBlock(child) is { } rendered)
                    {
                        rendered.Margin = new Thickness(0);
                        wpfCell.Blocks.Add(rendered);
                    }
                }

                wpfRow.Cells.Add(wpfCell);
            }

            group.Rows.Add(wpfRow);
        }

        wpfTable.RowGroups.Add(group);
        return wpfTable;
    }

    private IEnumerable<WpfInline> RenderInlines(ContainerInline? container)
    {
        if (container is null)
        {
            yield break;
        }

        foreach (var inline in container)
        {
            if (RenderInline(inline) is { } rendered)
            {
                yield return rendered;
            }
        }
    }

    private WpfInline? RenderInline(MdInline inline) => inline switch
    {
        LiteralInline literal => new Run(literal.Content.ToString()),
        EmphasisInline emphasis => RenderEmphasis(emphasis),
        CodeInline code => new Run(code.Content)
        {
            FontFamily = _mono,
            FontSize = flavor == MarkdownFlavor.Terminal ? 14 : 13.5,
            Background = Resource<Brush>("Brush.SurfaceLight"),
            Foreground = Resource<Brush>("Brush.Accent"),
        },
        LinkInline { IsImage: false } link => RenderLink(link.Url, RenderInlines(link)),
        LinkInline image => new Run($"[Bild: {image.Url}]"),
        AutolinkInline autolink => RenderLink(autolink.Url, [new Run(autolink.Url)]),
        LineBreakInline { IsHard: true } => new LineBreak(),
        LineBreakInline => new Run(" "),
        HtmlInline html => new Run(html.Tag),
        HtmlEntityInline entity => new Run(entity.Transcoded.ToString()),
        ContainerInline container => new Span().AddInlines(RenderInlines(container)),
        _ => null,
    };

    private Span RenderEmphasis(EmphasisInline emphasis)
    {
        Span span = emphasis.DelimiterChar is '~'
            ? new Span { TextDecorations = TextDecorations.Strikethrough }
            : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
        return span.AddInlines(RenderInlines(emphasis));
    }

    private WpfInline RenderLink(string? url, IEnumerable<WpfInline> content)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return new Span().AddInlines(content);
        }

        var link = new Hyperlink { NavigateUri = uri, ToolTip = uri.ToString(), Foreground = Resource<Brush>("Brush.Accent") };
        link.AddInlines(content);
        return link;
    }

    private static Paragraph WithSpacing(Paragraph paragraph)
    {
        paragraph.Margin = new Thickness(0, 0, 0, 10);
        return paragraph;
    }

    /// <summary>
    /// Farben und Schriften aus Themes/*.xaml. Bei jedem Zeichnen frisch geholt, damit eine neue Akzentfarbe gilt.
    /// </summary>
    private static T Resource<T>(string key) where T : class =>
        (Application.Current.TryFindResource(key) as T)
        ?? throw new InvalidOperationException($"Ressource {key} fehlt.");
}

/// <summary>Kleine Hilfen, damit der Renderer kurz bleibt.</summary>
internal static class FlowDocumentExtensions
{
    public static Paragraph AddInlines(this Paragraph paragraph, IEnumerable<WpfInline> inlines)
    {
        paragraph.Inlines.AddRange(inlines);
        return paragraph;
    }

    public static T AddInlines<T>(this T span, IEnumerable<WpfInline> inlines) where T : Span
    {
        span.Inlines.AddRange(inlines);
        return span;
    }
}
