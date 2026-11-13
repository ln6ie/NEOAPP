using System.Windows.Documents;
using NEO.Views;

namespace NEO.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public void RendersArabicHeadingsListsCodeAndTablesAsNativeDocument()
    {
        var document = MarkdownView.CreateDocument("# عنوان\n\nفقرة **مهمة**\n\n- أولاً\n- ثانياً\n\n| الاسم | العدد |\n|---|---|\n| مثال | 2 |\n\n```cs\nvar x = 1;\n```");
        Assert.Contains(document.Blocks.OfType<Paragraph>(), p => p.FontSize > 20);
        Assert.Equal(2, Assert.Single(document.Blocks.OfType<System.Windows.Documents.List>()).ListItems.Count);
        var table = Assert.Single(document.Blocks.OfType<Table>());
        Assert.Equal(2, table.RowGroups[0].Rows.Count);
        Assert.Equal(2, table.RowGroups[0].Rows[1].Cells.Count);
        Assert.Contains(document.Blocks.OfType<Paragraph>(), p => p.FontFamily.Source.Contains("Consolas"));
    }

    [Fact]
    public void UnsafeLinksAndRemoteImagesAreNotExecutedOrFetched()
    {
        var document = MarkdownView.CreateDocument("[bad](javascript:alert(1)) ![remote](https://example.com/image.png)");
        Assert.Empty(document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>()));
    }
}
