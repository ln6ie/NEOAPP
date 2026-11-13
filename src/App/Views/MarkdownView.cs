using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using NEO.Infrastructure;
using MdTable = Markdig.Extensions.Tables.Table;
using MdRow = Markdig.Extensions.Tables.TableRow;
using MdCell = Markdig.Extensions.Tables.TableCell;
using WpfBlock = System.Windows.Documents.Block;

namespace NEO.Views;

public sealed class MarkdownView : RichTextBox
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().Build();
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(MarkdownView), new PropertyMetadata(string.Empty, Changed));
    private readonly DispatcherTimer refresh;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public MarkdownView()
    {
        IsReadOnly = true; IsDocumentEnabled = true;
        BorderThickness = new Thickness(0); Background = Brushes.Transparent;
        Padding = new Thickness(0); VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        refresh.Tick += (_, _) => { refresh.Stop(); Render(); };
        Loaded += (_, _) => Render();
        Unloaded += (_, _) => refresh.Stop();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        if (view.IsLoaded && !view.refresh.IsEnabled) view.refresh.Start();
    }
    private void Render()
    {
        Document = CreateDocument(Text ?? string.Empty, FlowDirection, FontFamily);
    }
    public static FlowDocument CreateDocument(string text, FlowDirection direction = FlowDirection.RightToLeft, FontFamily? font = null)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontSize = 14, FontFamily = font ?? new FontFamily("Segoe UI"), FlowDirection = direction, LineHeight = 25 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Foreground");
        foreach (var block in Markdown.Parse(text, Pipeline)) document.Blocks.Add(ConvertBlock(block));
        return document;
    }
    private static WpfBlock ConvertBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Paragraph(heading.Inline);
                title.FontSize = Math.Max(16, 27 - heading.Level * 2); title.FontWeight = FontWeights.SemiBold;
                title.Margin = new Thickness(0, 12, 0, 7); return title;
            case ParagraphBlock paragraph: return Paragraph(paragraph.Inline);
            case CodeBlock code:
                return new System.Windows.Documents.Paragraph(new Run(code.Lines.ToString()))
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                    FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Left,
                    Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 12), Background = Resource("CardSurface")
                };
            case ListBlock list:
                var result = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Margin = new Thickness(20, 4, 8, 12), Padding = new Thickness(12, 0, 0, 0) };
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var output = new ListItem();
                    foreach (var child in item) output.Blocks.Add(ConvertBlock(child));
                    result.ListItems.Add(output);
                }
                return result;
            case MdTable table:
                var grid = new System.Windows.Documents.Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 14), FontSize = 13 };
                var rows = table.OfType<MdRow>().ToArray();
                var columns = rows.Length == 0 ? 1 : rows.Max(r => r.Count);
                for (var i = 0; i < columns; i++) grid.Columns.Add(new TableColumn());
                var group = new TableRowGroup(); grid.RowGroups.Add(group);
                foreach (var row in rows)
                {
                    var target = new System.Windows.Documents.TableRow { FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal };
                    if (row.IsHeader) target.Background = Resource("CardSurface");
                    foreach (var cell in row.OfType<MdCell>())
                    {
                        var output = new TableCell { Padding = new Thickness(9), BorderBrush = Resource("SecondaryForeground"), BorderThickness = new Thickness(0, 0, 0, 0.5) };
                        foreach (var child in cell) output.Blocks.Add(ConvertBlock(child));
                        target.Cells.Add(output);
                    }
                    group.Rows.Add(target);
                }
                return grid;
            case ThematicBreakBlock:
                return new System.Windows.Documents.Paragraph { BorderBrush = Resource("SecondaryForeground"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 8, 0, 12) };
            case ContainerBlock container:
                var section = new Section { Margin = new Thickness(0, 4, 0, 10) };
                if (block is QuoteBlock) { section.BorderBrush = Resource("BrandBlue"); section.BorderThickness = new Thickness(0, 0, 3, 0); section.Padding = new Thickness(10); }
                foreach (var child in container) section.Blocks.Add(ConvertBlock(child));
                return section;
            default: return new System.Windows.Documents.Paragraph();
        }
    }
    private static System.Windows.Documents.Paragraph Paragraph(ContainerInline? inline)
    {
        var paragraph = new System.Windows.Documents.Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        if (inline != null) AddInlines(paragraph.Inlines, inline);
        return paragraph;
    }
    private static void AddInlines(InlineCollection output, ContainerInline parent)
    {
        foreach (var inline in parent)
        {
            switch (inline)
            {
                case LiteralInline literal: output.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code: output.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = Resource("CardSurface"), FlowDirection = FlowDirection.LeftToRight }); break;
                case LineBreakInline line: output.Add(line.IsHard ? new LineBreak() : new Run(" ")); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.SemiBold;
                    else span.FontStyle = FontStyles.Italic;
                    AddInlines(span.Inlines, emphasis); output.Add(span); break;
                case LinkInline link:
                    if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    {
                        var hyperlink = new Hyperlink { NavigateUri = uri, Foreground = Resource("BrandBlue") };
                        AddInlines(hyperlink.Inlines, link);
                        hyperlink.RequestNavigate += (_, e) =>
                        {
                            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                            catch (Exception ex) { NEOLog.Error("markdown.link.failed", ex); }
                            e.Handled = true;
                        };
                        output.Add(hyperlink);
                    }
                    else AddInlines(output, link);
                    break;
                case HtmlEntityInline entity: output.Add(new Run(entity.Transcoded.ToString())); break;
                case ContainerInline nested: AddInlines(output, nested); break;
            }
        }
    }
    private static Brush Resource(string key) => Application.Current?.TryFindResource(key) as Brush ?? SystemColors.WindowTextBrush;
}
