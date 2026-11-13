using System.Net;
using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

public sealed class LlamaHealthClient(HttpClient http, Uri endpoint) : IHealthClient
{
    public async Task<ConnectionStatus> CheckAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var response = await http.GetAsync(new Uri(endpoint, "health"), deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return new(false, "llama.cpp is loading or unavailable. Retry when the model is ready.");
            if (!response.IsSuccessStatusCode)
                return new(false, $"llama.cpp health returned HTTP {(int)response.StatusCode}.");
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: deadline.Token).ConfigureAwait(false);
            return json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "ok"
                ? new(true, "llama.cpp is ready. Text chat and realtime voice are available.")
                : new(false, "The server did not report a ready llama.cpp health status.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, "llama.cpp health check timed out."); }
        catch (HttpRequestException ex)
        { return new(false, $"Cannot reach llama.cpp: {ex.Message}"); }
        catch (JsonException)
        { return new(false, "The server returned an invalid health response."); }
    }
}
