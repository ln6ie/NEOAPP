using System.Net;
using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class HealthTests
{
    [Theory]
    [InlineData(200, "{\"status\":\"ok\"}", true)]
    [InlineData(503, "{\"error\":\"loading\"}", false)]
    [InlineData(200, "{}", false)]
    [InlineData(200, "not json", false)]
    [InlineData(500, "error", false)]
    public async Task DistinguishesReadyLoadingAndMalformedResponses(int code, string body, bool ready)
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("http://127.0.0.1:8081/health", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) });
        }));
        var result = await new LlamaHealthClient(http, new Uri("http://127.0.0.1:8081/")).CheckAsync(default);
        Assert.Equal(ready, result.Available);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LlamaHealthClient(http, new Uri("http://127.0.0.1:8081/")).CheckAsync(cancellation.Token));
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
}
