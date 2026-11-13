namespace NEO.Core;

public sealed record ChatAttachment(string Name, string MediaType, byte[]? ImageBytes = null, string? Text = null);

public sealed record ChatMessage(string Role, string Content, IReadOnlyList<ChatAttachment>? Attachments = null)
{
    public string DisplayRole => Role == "user" ? "أنت" : "نيو";
    public string AttachmentSummary => Attachments is null ? string.Empty : string.Join(" · ", Attachments.Select(a => a.Name));
}

public interface IChatClient
{
    Task<string> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
