using System.Net;
using System.Text.Json;
using NEO.Core;
using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class ChatTests
{
    [Fact]
    public async Task AttachmentsUseRealMultimodalContentWithoutLeakingRawProperties()
    {
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var message = body.RootElement.GetProperty("messages")[1];
            var parts = message.GetProperty("content");
            Assert.Equal("text", parts[0].GetProperty("type").GetString());
            Assert.Contains("ملف نصي", parts[1].GetProperty("text").GetString());
            Assert.Equal("data:image/png;base64,AQID", parts[2].GetProperty("image_url").GetProperty("url").GetString());
            Assert.False(message.TryGetProperty("attachments", out _));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"تم\"}}]}") };
        }));
        await new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/")).SendAsync(
            [new ChatMessage("user", "حلل", [new ChatAttachment("test.txt", "text/plain", Text: "ملف نصي"), new ChatAttachment("image.png", "image/png", [1, 2, 3])])], default);
    }

    [Fact]
    public async Task ArabicInstructionPrecedesConversationWithoutChangingUserMessage()
    {
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal(AssistantLanguage.Instructions, messages[0].GetProperty("content").GetString());
            Assert.Equal("user", messages[1].GetProperty("role").GetString());
            Assert.Equal("مرحبا", messages[1].GetProperty("content").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"أهلا\"}}]}") };
        }));
        var reply = await new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/")).SendAsync([new ChatMessage("user", "مرحبا")], default);
        Assert.Equal("أهلا", reply);
    }

    [Fact]
    public async Task ParsesRealChatCompletion()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("POST", request.Method.Method);
            Assert.Equal("http://127.0.0.1:8081/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"NEO-OK\"}}]}")
            });
        }));

        var result = await new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/")).SendAsync(
            [new ChatMessage("user", "hello")], default);

        Assert.Equal("NEO-OK", result);
    }

    [Fact]
    public async Task ExposesServerErrorsWithoutStackTrace()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        })));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/")).SendAsync(
                [new ChatMessage("user", "hello")], default));

        Assert.Contains("HTTP 400", error.Message);
        Assert.Contains("bad request", error.Message);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
