using System.Net.Http.Json;
using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

public sealed class LlamaChatClient(HttpClient http, Uri endpoint) : IChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0) throw new ArgumentException("At least one message is required.", nameof(messages));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await http.PostAsJsonAsync(new Uri(endpoint, "v1/chat/completions"), new
        {
            model = "local", messages = new[] { new ChatMessage("system", AssistantLanguage.Instructions) }.Concat(messages).Select(ToWireMessage).ToArray(), stream = false, temperature = 0, max_tokens = 2048
        }, JsonOptions, deadline.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"llama.cpp chat returned HTTP {(int)response.StatusCode}: {Trim(body)}");
        try
        {
            using var json = JsonDocument.Parse(body);
            var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            return string.IsNullOrWhiteSpace(content) ? "The model returned an empty response." : content.Trim();
        }
        catch (JsonException ex) { throw new InvalidOperationException("llama.cpp returned an invalid chat response.", ex); }
        catch (KeyNotFoundException ex) { throw new InvalidOperationException("llama.cpp returned a chat response without message content.", ex); }
    }

    private static string Trim(string body) => body.Length <= 500 ? body : body[..500] + "…";

    private static object ToWireMessage(ChatMessage message)
    {
        if (message.Attachments is not { Count: > 0 }) return new { role = message.Role, content = (object)message.Content };
        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(message.Content)) parts.Add(new { type = "text", text = message.Content });
        foreach (var attachment in message.Attachments)
        {
            if (attachment.ImageBytes != null)
                parts.Add(new { type = "image_url", image_url = new { url = $"data:{attachment.MediaType};base64,{Convert.ToBase64String(attachment.ImageBytes)}" } });
            else parts.Add(new { type = "text", text = $"ملف مرفق: {attachment.Name}\n--- بداية محتوى الملف ---\n{attachment.Text}\n--- نهاية محتوى الملف ---" });
        }
        return new { role = message.Role, content = (object)parts };
    }
}
