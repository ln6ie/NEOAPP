using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NEO.Core;
using UglyToad.PdfPig;

namespace NEO.Infrastructure;

public static class AttachmentReader
{
    public const int MaxBytes = 12 * 1024 * 1024;
    public const int MaxCharacters = 80000;
    public static async Task<ChatAttachment> ReadAsync(string path, CancellationToken token = default)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new InvalidOperationException("حجم الملف يتجاوز 12 ميغابايت.");
        var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        if (bytes.Length > MaxBytes) throw new InvalidOperationException("حجم الملف يتجاوز 12 ميغابايت.");
        var extension = info.Extension.ToLowerInvariant();
        var mime = extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => null };
        if (mime != null)
        {
            bool valid = mime switch
            {
                "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "image/jpeg" => bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216,
                "image/webp" => bytes.Length > 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
                "image/bmp" => bytes.Length > 2 && bytes[0] == 66 && bytes[1] == 77,
                _ => false
            };
            if (!valid) throw new InvalidOperationException("ملف الصورة غير صالح أو لا يطابق نوعه.");
            return new ChatAttachment(info.Name, mime, bytes);
        }
        var text = await Task.Run(() => Extract(extension, bytes), token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("لم أجد نصاً قابلاً للقراءة في الملف. إذا كان ممسوحاً ضوئياً، أرفقه كصورة.");
        if (text.Length > MaxCharacters) throw new InvalidOperationException("الملف طويل جداً. أرفق جزءاً أقصر من 80 ألف حرف.");
        return new ChatAttachment(info.Name, "text/plain", Text: text);
    }

    private static string Extract(string extension, byte[] bytes)
    {
        if (extension == ".pdf")
        {
            using var pdf = PdfDocument.Open(bytes);
            var result = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                result.AppendLine(page.Text);
                if (result.Length > MaxCharacters) break;
            }
            return result.ToString();
        }
        if (extension == ".docx")
        {
            using var memory = new MemoryStream(bytes);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
            var document = zip.GetEntry("word/document.xml") ?? throw new InvalidOperationException("ملف Word غير صالح.");
            if (document.Length > 2 * 1024 * 1024) throw new InvalidOperationException("محتوى ملف Word طويل جداً.");
            using var stream = document.Open();
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, MaxCharactersInDocument = 2 * 1024 * 1024 });
            var xml = XDocument.Load(reader);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            return string.Join(Environment.NewLine, xml.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
        }
        string[] textTypes = [".txt", ".md", ".csv", ".json", ".xml", ".html", ".css", ".js", ".ts", ".cs", ".py", ".yaml", ".yml", ".log", ".sql"];
        if (!textTypes.Contains(extension)) throw new InvalidOperationException("نوع الملف غير مدعوم. استخدم صورة أو PDF نصياً أو Word أو ملفاً نصياً.");
        var content = new UTF8Encoding(false, true).GetString(bytes);
        if (content.Contains('\0')) throw new InvalidOperationException("الملف ليس نصاً صالحاً.");
        return content.TrimStart('\uFEFF');
    }
}
