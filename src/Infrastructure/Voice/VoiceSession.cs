using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

public sealed class VoiceSession : IVoiceSession
{
    private const int InputSampleRate = 16000;
    private readonly Uri endpoint;
    private readonly string? microphoneDeviceId;
    private readonly SemaphoreSlim gate = new(1, 1);
    private RealtimeConnection? connection;
    private volatile bool conversationEnabled;
    private readonly AudioInput audioInput;
    private readonly AudioPlayback speaker;
    private readonly StringBuilder responseText = new();
    private CancellationTokenSource? connectionCancellation;
    private TaskCompletionSource<bool>? sessionReady;
    private TaskCompletionSource<bool>? sessionUpdated;
    private int outputSampleRate = 24000;
    private VoiceSessionState state = VoiceSessionState.Disconnected;
    private float inputLevel;
    private float outputLevel;
    private float peakInputLevel;
    private float peakOutputLevel;
    private string? lastError;
    private long receivedAudioBytes;
    private bool disposed;
    private long lastPlaybackLog;
    private long responseStarted;
    private long responseChunks;

    public VoiceSession(Uri endpoint, string? microphoneDeviceId = null)
    {
        this.endpoint = endpoint;
        this.microphoneDeviceId = microphoneDeviceId;
        speaker = new AudioPlayback(SetError);
        speaker.LevelChanged += level => { outputLevel = level; AudioLevelChanged?.Invoke(inputLevel, outputLevel); };
        audioInput = new AudioInput(microphoneDeviceId,
            () => conversationEnabled && state == VoiceSessionState.Listening,
            AppendAudioAsync, CommitInputTurnAsync, HandleInputLevel, SetError);
    }

    public VoiceSessionState State => state;
    public float InputLevel => inputLevel;
    public float OutputLevel => outputLevel;
    public float PeakInputLevel => peakInputLevel;
    public float PeakOutputLevel => peakOutputLevel;
    public string? LastError => lastError;
    public long CapturedBytes => audioInput.CapturedBytes;
    public long SentBytes => audioInput.SentBytes;
    public long ReceivedAudioBytes => Interlocked.Read(ref receivedAudioBytes);
    public event Action<VoiceSessionState>? StateChanged;
    public event Action<float, float>? AudioLevelChanged;
    public event Action<string>? TextDelta;
    public event Action<VoiceResponse>? ResponseCompleted;

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        NEOLog.Write("voice.start.requested");
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state is VoiceSessionState.Speaking or VoiceSessionState.Processing)
                await CancelCoreAsync(cancellationToken).ConfigureAwait(false);
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            if (state == VoiceSessionState.Listening) return;
            conversationEnabled = true;
            SetState(VoiceSessionState.Listening);
            audioInput.Start();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            conversationEnabled = false;
            SetError(ex);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        NEOLog.Write("voice.stop.requested");
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            conversationEnabled = false;
            await CancelCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetError(ex);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CancelCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task CancelCoreAsync(CancellationToken cancellationToken)
    {
        NEOLog.Write("voice.cancel");
        conversationEnabled = false;
        await StopCaptureAsync().ConfigureAwait(false);
        if (connection?.IsOpen == true)
        {
            try { await connection.SendControlAsync("response.cancel", cancellationToken).ConfigureAwait(false); }
            catch (WebSocketException) { }
            try { await connection.SendControlAsync("input_audio_buffer.clear", cancellationToken).ConfigureAwait(false); }
            catch (WebSocketException) { }
        }
        speaker.Stop();
        responseText.Clear();
        if (state != VoiceSessionState.Disconnected) SetState(VoiceSessionState.Interrupted);
        if (connection?.IsOpen == true) SetState(VoiceSessionState.Ready);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (connection?.IsOpen == true && state is VoiceSessionState.Ready or VoiceSessionState.Listening or VoiceSessionState.Processing or VoiceSessionState.Speaking)
            return;
        await DisconnectSocketAsync().ConfigureAwait(false);
        SetState(VoiceSessionState.Connecting);
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var client = new RealtimeConnection(endpoint, (name, data) => NEOLog.Write(name, data));
                NEOLog.Write("ws.connect", new { endpoint = endpoint.ToString(), attempt = attempt + 1 });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                NEOLog.Write("ws.connected");
                connection = client;
                connectionCancellation = new CancellationTokenSource();
                sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                sessionUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = ReceiveLoopAsync(client, connectionCancellation.Token);
                await sessionReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await client.SendSessionUpdateAsync(InputSampleRate, outputSampleRate, timeout.Token).ConfigureAwait(false);
                // Do not stream microphone frames until llama.cpp has acknowledged
                // the requested audio format and voice settings.
                await sessionUpdated.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                SetState(VoiceSessionState.Ready);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                NEOLog.Error("ws.connect.failed", ex);
                await DisconnectSocketAsync().ConfigureAwait(false);
                if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Could not connect to llama.cpp realtime voice endpoint.", last);
    }

    private void HandleInputLevel(float level)
    {
        inputLevel = level;
        peakInputLevel = Math.Max(peakInputLevel, level);
        AudioLevelChanged?.Invoke(inputLevel, outputLevel);
    }

    private async Task AppendAudioAsync(string audio, CancellationToken cancellationToken)
    {
        var current = connection ?? throw new WebSocketException("Realtime connection was lost while streaming microphone audio.");
        await current.AppendAudioAsync(audio, connectionCancellation?.Token ?? cancellationToken).ConfigureAwait(false);
    }

    private async Task CommitInputTurnAsync(CancellationToken cancellationToken)
    {
        var current = connection ?? throw new WebSocketException("Realtime connection was lost before the voice turn completed.");
        responseStarted = Environment.TickCount64;
        responseChunks = 0;
        NEOLog.Write("voice.turn.commit", new { SentBytes, language = Language.SpeechCode });
        SetState(VoiceSessionState.Processing);
        var token = connectionCancellation?.Token ?? cancellationToken;
        await current.SendControlAsync("input_audio_buffer.commit", token).ConfigureAwait(false);
        await current.CreateResponseAsync(token).ConfigureAwait(false);
    }

    private async Task StopCaptureAsync()
    {
        await audioInput.StopAsync().ConfigureAwait(false);
        NEOLog.Write("voice.capture.stopped", new { CapturedBytes, SentBytes, PeakInputLevel });
    }

    private async Task ReceiveLoopAsync(RealtimeConnection client, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (client.IsOpen && !cancellationToken.IsCancellationRequested)
            {
                var message = await client.ReceiveMessageAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (message == null) return;
                await HandleServerEventAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SetError(ex); }
        finally
        {
            sessionReady?.TrySetException(new InvalidOperationException("Realtime websocket closed before session.created."));
            if (!disposed) _ = StopCaptureAsync();
            if (!disposed && state != VoiceSessionState.Error) SetState(VoiceSessionState.Disconnected);
        }
    }

    private async Task HandleServerEventAsync(string json, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement)) return;
        var type = typeElement.GetString();
        if (type is not "response.output_audio.delta" and not "response.output_text.delta")
            NEOLog.Write("ws.event", new { type });
        switch (type)
        {
            case "session.created":
                UpdateRates(root);
                sessionReady?.TrySetResult(true);
                break;
            case "session.updated":
                UpdateRates(root);
                sessionUpdated?.TrySetResult(true);
                break;
            case "response.output_audio.delta":
                if (root.TryGetProperty("delta", out var delta))
                {
                    var bytes = Convert.FromBase64String(delta.GetString() ?? string.Empty);
                    await PlayAudioAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
                break;
            case "response.output_text.delta":
                if (root.TryGetProperty("delta", out var text))
                {
                    var value = text.GetString() ?? string.Empty;
                    lock (responseText) responseText.Append(value);
                    TextDelta?.Invoke(value);
                }
                break;
            case "response.done":
                var cancelled = root.TryGetProperty("response", out var response) && response.TryGetProperty("status", out var status) && status.GetString() == "cancelled";
                string finalText;
                lock (responseText) { finalText = responseText.ToString(); responseText.Clear(); }
                _ = ResumeAfterPlaybackAsync();
                ResponseCompleted?.Invoke(new VoiceResponse(finalText, cancelled));
                NEOLog.Write("voice.response.done", new { cancelled, textCharacters = finalText.Length, responseChunks, ReceivedAudioBytes });
                break;
            case "error":
                var message = root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? "llama.cpp realtime error." : "llama.cpp realtime error.";
                SetError(new InvalidOperationException(message));
                break;
        }
    }

    private void UpdateRates(JsonElement root)
    {
        string? selectedVoice = null;
        if (root.TryGetProperty("session", out var session) && session.TryGetProperty("audio", out var audio))
        {
            if (audio.TryGetProperty("output", out var output))
            {
                if (output.TryGetProperty("format", out var format) && format.TryGetProperty("rate", out var rate) && rate.TryGetInt32(out var parsed) && parsed > 0)
                    outputSampleRate = parsed;
                if (output.TryGetProperty("voice", out var voice)) selectedVoice = voice.GetString();
            }
        }
        NEOLog.Write("voice.session.format", new { inputSampleRate = InputSampleRate, outputSampleRate, selectedVoice });
    }

    private async Task PlayAudioAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0) return;
        Interlocked.Add(ref receivedAudioBytes, bytes.Length);
        responseChunks++;
        if (responseChunks == 1) NEOLog.Write("speaker.first_audio", new { waitMilliseconds = Environment.TickCount64 - responseStarted, bytes = bytes.Length, outputSampleRate });
        if (!conversationEnabled || disposed) return;
        // Generation can outpace the device; AudioPlayback applies backpressure.
        await speaker.EnqueueAsync(bytes, outputSampleRate, cancellationToken).ConfigureAwait(false);
        var now = Environment.TickCount64;
        if (now - lastPlaybackLog >= 1000)
        {
            lastPlaybackLog = now;
            NEOLog.Write("speaker.progress", new { responseChunks, ReceivedAudioBytes, speaker.ConsumedBytes, speaker.BufferedBytes, bufferedMilliseconds = speaker.BufferedBytes * 1000d / (outputSampleRate * 2), outputSampleRate });
        }
        outputLevel = PcmAudio.Level(bytes);
        peakOutputLevel = Math.Max(peakOutputLevel, outputLevel);
        AudioLevelChanged?.Invoke(inputLevel, outputLevel);
        if (state != VoiceSessionState.Speaking) SetState(VoiceSessionState.Speaking);
    }

    private async Task ResumeAfterPlaybackAsync()
    {
        while (!disposed && conversationEnabled)
        {
            await Task.Delay(25).ConfigureAwait(false);
            if (speaker.BufferedBytes > 0) continue;
            // BufferedBytes excludes the final audio already submitted to WASAPI.
            await Task.Delay(100).ConfigureAwait(false);
            speaker.Stop();
            outputLevel = 0; inputLevel = 0;
            NEOLog.Write("speaker.drained", new { received = ReceivedAudioBytes, consumed = speaker.ConsumedBytes });
            if (conversationEnabled) SetState(VoiceSessionState.Listening);
            AudioLevelChanged?.Invoke(inputLevel, outputLevel);
            return;
        }
    }

    private async Task DisconnectSocketAsync()
    {
        connectionCancellation?.Cancel();
        var current = connection;
        connection = null;
        if (current != null) await current.DisposeAsync().ConfigureAwait(false);
        connectionCancellation?.Dispose(); connectionCancellation = null;
    }

    private void SetState(VoiceSessionState next)
    {
        if (state == next) return;
        NEOLog.Write("voice.state", new { previous = state.ToString(), next = next.ToString() });
        state = next;
        StateChanged?.Invoke(next);
    }

    private void SetError(Exception ex)
    {
        lastError = ex.Message;
        NEOLog.Error("voice.error", ex);
        SetState(VoiceSessionState.Error);
    }

    private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(disposed, this); }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        NEOLog.Write("voice.dispose");
        disposed = true;
        conversationEnabled = false;
        await StopCaptureAsync().ConfigureAwait(false);
        await DisconnectSocketAsync().ConfigureAwait(false);
        speaker.Dispose();
        gate.Dispose();
    }
}
