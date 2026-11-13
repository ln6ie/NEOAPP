using System.IO.Compression;
using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class AttachmentTests
{
    [Fact]
    public async Task ExtractsArabicTextAndWordParagraphs()
    {
        var directory = Directory.CreateTempSubdirectory("NEO-tests-");
        try
        {
            var textPath = Path.Combine(directory.FullName, "note.txt");
            await File.WriteAllTextAsync(textPath, "نص عربي");
            Assert.Equal("نص عربي", (await AttachmentReader.ReadAsync(textPath)).Text);
            var word = Path.Combine(directory.FullName, "note.docx");
            using (var zip = ZipFile.Open(word, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("word/document.xml");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>مرحبا</w:t></w:r></w:p></w:body></w:document>");
            }
            Assert.Equal("مرحبا", (await AttachmentReader.ReadAsync(word)).Text);
        }
        finally { directory.Delete(true); }
    }
    [Fact]
    public async Task RejectsUnsupportedBinaryAndFakeImages()
    {
        var directory = Directory.CreateTempSubdirectory("NEO-tests-");
        try
        {
            var binary = Path.Combine(directory.FullName, "program.exe");
            await File.WriteAllBytesAsync(binary, [0, 1, 2, 3]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => AttachmentReader.ReadAsync(binary));
            var image = Path.Combine(directory.FullName, "fake.png");
            await File.WriteAllTextAsync(image, "not an image");
            await Assert.ThrowsAsync<InvalidOperationException>(() => AttachmentReader.ReadAsync(image));
        }
        finally { directory.Delete(true); }
    }
}
