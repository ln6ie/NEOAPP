using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NEO.Core;

namespace NEO.Infrastructure;

public sealed class LlamaRealtimeVoiceSession : IVoiceSession
{
    private const int InputSampleRate = 16000;
    private const int BitsPerSample = 16;
    private readonly Uri endpoint;
    private readonly string? microphoneDeviceId;
    private MMDevice? captureDevice;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly VoiceTurnDetector turnDetector = new(1000);
    private readonly PcmInputGain inputGain = new();
    private volatile bool conversationEnabled;
    private readonly object audioGate = new();
    private readonly StringBuilder responseText = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? connectionCancellation;
    private WasapiCapture? capture;
    private Channel<AudioChunk>? audioChannel;
    private Task? sendTask;
    private PcmPlaybackBuffer? playbackBuffer;
    private WasapiOut? playback;
    private TaskCompletionSource<bool>? sessionReady;
    private TaskCompletionSource<bool>? sessionUpdated;
    private int outputSampleRate = 24000;
    private VoiceSessionState state = VoiceSessionState.Disconnected;
    private float inputLevel;
    private float outputLevel;
    private float peakInputLevel;
    private float peakOutputLevel;
    private string? lastError;
    private long capturedBytes;
    private long sentBytes;
    private long receivedAudioBytes;
    private bool disposed;
    private long lastCaptureLog;
    private long lastPlaybackLog;
    private long responseStarted;
    private long responseChunks;
    private CancellationTokenSource playbackCancellation = new();

    public LlamaRealtimeVoiceSession(Uri endpoint, string? microphoneDeviceId = null)
    {
        this.endpoint = endpoint;
        this.microphoneDeviceId = microphoneDeviceId;
    }

    public VoiceSessionState State => state;
    public float InputLevel => inputLevel;
    public float OutputLevel => outputLevel;
    public float PeakInputLevel => peakInputLevel;
    public float PeakOutputLevel => peakOutputLevel;
    public string? LastError => lastError;
    public long CapturedBytes => Interlocked.Read(ref capturedBytes);
    public long SentBytes => Interlocked.Read(ref sentBytes);
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
            turnDetector.Reset();
            SetState(VoiceSessionState.Listening);
            StartCapture();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            conversationEnabled = false;
            if (capture == null) { captureDevice?.Dispose(); captureDevice = null; }
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
        if (socket?.State == WebSocketState.Open)
        {
            try { await SendEventAsync(new { type = "response.cancel" }, cancellationToken).ConfigureAwait(false); }
            catch (WebSocketException) { }
            try { await SendEventAsync(new { type = "input_audio_buffer.clear" }, cancellationToken).ConfigureAwait(false); }
            catch (WebSocketException) { }
        }
        FlushPlayback();
        responseText.Clear();
        if (state != VoiceSessionState.Disconnected) SetState(VoiceSessionState.Interrupted);
        if (socket?.State == WebSocketState.Open) SetState(VoiceSessionState.Ready);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (socket?.State == WebSocketState.Open && state is VoiceSessionState.Ready or VoiceSessionState.Listening or VoiceSessionState.Processing or VoiceSessionState.Speaking)
            return;
        await DisconnectSocketAsync().ConfigureAwait(false);
        SetState(VoiceSessionState.Connecting);
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var ws = new ClientWebSocket { Options = { KeepAliveInterval = TimeSpan.FromSeconds(15) } };
                NEOLog.Write("ws.connect", new { endpoint = endpoint.ToString(), attempt = attempt + 1 });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await ws.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
                NEOLog.Write("ws.connected");
                socket = ws;
                connectionCancellation = new CancellationTokenSource();
                sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                sessionUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = ReceiveLoopAsync(ws, connectionCancellation.Token);
                await sessionReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await SendSessionUpdateAsync(timeout.Token).ConfigureAwait(false);
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

    private async Task SendSessionUpdateAsync(CancellationToken cancellationToken)
    {
        await SendEventAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                instructions = AssistantLanguage.VoiceInstructions,
                output_modalities = new[] { "text", "audio" },
                audio = new
                {
                    input = new { format = new { type = "audio/pcm", rate = InputSampleRate } },
                    output = new { format = new { type = "audio/pcm", rate = outputSampleRate }, voice = "female" }
                },
                max_output_tokens = 1024
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private void StartCapture()
    {
        if (capture != null) return;
        using var devices = new MMDeviceEnumerator();
        captureDevice = string.IsNullOrEmpty(microphoneDeviceId)
            ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            : devices.GetDevice(microphoneDeviceId);
        NEOLog.Write("mic.device", new { name = captureDevice.FriendlyName, id = captureDevice.ID, muted = captureDevice.AudioEndpointVolume.Mute, volume = captureDevice.AudioEndpointVolume.MasterVolumeLevelScalar });
        if (captureDevice.AudioEndpointVolume.Mute) throw new InvalidOperationException("The selected microphone is muted in Windows. Unmute it or select another microphone in NEO settings.");
        var device = new WasapiCapture(captureDevice);
        // WASAPI shared-mode conversion must produce this exact format. Never label
        // native-rate samples as 16 kHz without resampling them.
        device.WaveFormat = new WaveFormat(InputSampleRate, BitsPerSample, 1);
        device.DataAvailable += OnDataAvailable;
        device.RecordingStopped += OnRecordingStopped;
        capture = device;
        NEOLog.Write("mic.start", new { format = device.WaveFormat.ToString(), targetRate = InputSampleRate });
        audioChannel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        sendTask = SendAudioLoopAsync(audioChannel.Reader, connectionCancellation?.Token ?? CancellationToken.None);
        device.StartRecording();
    }

    private async Task StopCaptureAsync()
    {
        var device = Interlocked.Exchange(ref capture, null);
        if (device != null)
        {
            device.DataAvailable -= OnDataAvailable;
            device.RecordingStopped -= OnRecordingStopped;
            try { device.StopRecording(); } catch (InvalidOperationException) { }
            device.Dispose();
            captureDevice?.Dispose(); captureDevice = null;
            NEOLog.Write("mic.stopped", new { CapturedBytes, SentBytes, PeakInputLevel });
        }
        audioChannel?.Writer.TryComplete();
        try { if (sendTask != null) await sendTask.ConfigureAwait(false); } catch (Exception ex) { SetError(ex); }
        sendTask = null;
        audioChannel = null;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) SetError(e.Exception);
        NEOLog.Write("mic.recording_stopped", new { failed = e.Exception != null });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || audioChannel == null || state != VoiceSessionState.Listening) return;
        var sourceFormat = capture?.WaveFormat;
        var bytes = PcmAudio.ToMono16(e.Buffer, e.BytesRecorded, sourceFormat, InputSampleRate);
        if (bytes.Length == 0) return;
        Interlocked.Add(ref capturedBytes, bytes.Length);
        var level = PcmAudio.Level(bytes);
        inputLevel = level;
        peakInputLevel = Math.Max(peakInputLevel, level);
        var now = Environment.TickCount64;
        if (now - lastCaptureLog >= 1000)
        {
            lastCaptureLog = now;
            NEOLog.Write("mic.level", new { level, peakInputLevel, CapturedBytes, SentBytes, frameBytes = bytes.Length, vadThreshold = turnDetector.Threshold, speechActive = turnDetector.IsSpeechActive });
        }
        AudioLevelChanged?.Invoke(inputLevel, outputLevel);
        var rented = pool.Rent(bytes.Length);
        bytes.CopyTo(rented, 0);
        if (!audioChannel.Writer.TryWrite(new AudioChunk(rented, bytes.Length)))
        {
            pool.Return(rented);
            NEOLog.Write("mic.queue.full", new { lostBytes = bytes.Length });
        }
    }

    private async Task SendAudioLoopAsync(ChannelReader<AudioChunk> reader, CancellationToken cancellationToken)
    {
        var preRoll = new Queue<AudioChunk>();
        var preRollBytes = 0;
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (!conversationEnabled || state != VoiceSessionState.Listening) continue;
            var level = PcmAudio.Level(chunk.Buffer.AsSpan(0, chunk.Count));
                    var wasSpeechActive = turnDetector.IsSpeechActive;
                    var endTurn = turnDetector.Observe(level, chunk.Count * 1000d / (InputSampleRate * 2));
                    if (!wasSpeechActive && turnDetector.IsSpeechActive)
                    {
                        inputGain.Reset();
                        NEOLog.Write("voice.speech.started", new { level, threshold = turnDetector.Threshold, preRollBytes });
                    }
                    if (!turnDetector.IsSpeechActive)
                    {
                        var saved = pool.Rent(chunk.Count);
                        chunk.Buffer.AsSpan(0, chunk.Count).CopyTo(saved);
                        preRoll.Enqueue(new AudioChunk(saved, chunk.Count));
                        preRollBytes += chunk.Count;
                        while (preRollBytes > InputSampleRate * 2 * 0.35 && preRoll.Count > 1)
                        {
                            var old = preRoll.Dequeue(); preRollBytes -= old.Count; pool.Return(old.Buffer);
                        }
                        continue;
                    }
                    while (preRoll.TryDequeue(out var saved))
                    {
                        try
                        {
                            inputGain.Apply(saved.Buffer.AsSpan(0, saved.Count));
                            await SendEventAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(saved.Buffer, 0, saved.Count) }, cancellationToken, logEvent: false).ConfigureAwait(false);
                            Interlocked.Add(ref sentBytes, saved.Count);
                        }
                        finally { pool.Return(saved.Buffer); }
                    }
                    preRollBytes = 0;
                    inputGain.Apply(chunk.Buffer.AsSpan(0, chunk.Count));
                    var audio = Convert.ToBase64String(chunk.Buffer, 0, chunk.Count);
                    await SendEventAsync(new { type = "input_audio_buffer.append", audio }, cancellationToken, logEvent: false).ConfigureAwait(false);
                    Interlocked.Add(ref sentBytes, chunk.Count);
                    if (endTurn)
                    {
                        responseStarted = Environment.TickCount64;
                        responseChunks = 0;
                        NEOLog.Write("voice.turn.commit", new { SentBytes, level, gain = inputGain.Gain, language = AssistantLanguage.SpeechCode });
                        SetState(VoiceSessionState.Processing);
                        await SendEventAsync(new { type = "input_audio_buffer.commit" }, cancellationToken).ConfigureAwait(false);
                        await SendEventAsync(new { type = "response.create", response = new { output_modalities = new[] { "text", "audio" } } }, cancellationToken).ConfigureAwait(false);
                        turnDetector.Reset();
                    }
                }
                finally { pool.Return(chunk.Buffer); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { SetError(ex); }
        finally
        {
            while (preRoll.TryDequeue(out var saved)) pool.Return(saved.Buffer);
            while (reader.TryRead(out var pending)) pool.Return(pending.Buffer);
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        NEOLog.Write("ws.closed", new { result.CloseStatus, result.CloseStatusDescription });
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                await HandleServerEventAsync(Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)), cancellationToken).ConfigureAwait(false);
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
        lock (audioGate)
        {
            if (playbackBuffer == null || playbackBuffer.WaveFormat.SampleRate != outputSampleRate) InitializePlayback();
        }
        // Generation can run many times faster than playback. Apply backpressure instead
        // of silently throwing away the middle of the sentence when the buffer fills.
        PcmPlaybackBuffer target;
        CancellationToken playbackToken;
        lock (audioGate)
        {
            if (!conversationEnabled || disposed) return;
            target = playbackBuffer!;
            playbackToken = playbackCancellation.Token;
            if (playback?.PlaybackState != PlaybackState.Playing) playback?.Play();
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, playbackToken);
        try { await target.EnqueueAsync(bytes, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (playbackToken.IsCancellationRequested) { return; }
        var now = Environment.TickCount64;
        if (now - lastPlaybackLog >= 1000)
        {
            lastPlaybackLog = now;
            NEOLog.Write("speaker.progress", new { responseChunks, ReceivedAudioBytes, target.ConsumedBytes, target.BufferedBytes, bufferedMilliseconds = target.BufferedBytes * 1000d / (outputSampleRate * 2), outputSampleRate });
        }
        outputLevel = PcmAudio.Level(bytes);
        peakOutputLevel = Math.Max(peakOutputLevel, outputLevel);
        AudioLevelChanged?.Invoke(inputLevel, outputLevel);
        if (state != VoiceSessionState.Speaking) SetState(VoiceSessionState.Speaking);
    }

    private void InitializePlayback()
    {
        playback?.Stop(); playback?.Dispose(); playback = null; playbackBuffer = null;
        playbackBuffer = new PcmPlaybackBuffer(outputSampleRate);
        playback = new WasapiOut(AudioClientShareMode.Shared, 50);
        playback.Init(playbackBuffer);
        playback.PlaybackStopped += (_, e) =>
        {
            NEOLog.Write("speaker.stopped", new { failed = e.Exception != null });
            if (e.Exception != null) SetError(e.Exception);
        };
        NEOLog.Write("speaker.initialized", new { outputSampleRate, latencyMilliseconds = 50, bufferSeconds = 2 });
    }

    private async Task ResumeAfterPlaybackAsync()
    {
        while (!disposed && conversationEnabled)
        {
            await Task.Delay(25).ConfigureAwait(false);
            lock (audioGate)
            {
                if (playbackBuffer?.BufferedBytes > 0) continue;
            }
            // BufferedBytes excludes the final audio already submitted to WASAPI.
            await Task.Delay(100).ConfigureAwait(false);
            lock (audioGate) { playback?.Stop(); outputLevel = 0; inputLevel = 0; }
            NEOLog.Write("speaker.drained", new { received = ReceivedAudioBytes, consumed = playbackBuffer?.ConsumedBytes });
            turnDetector.Reset();
            if (conversationEnabled) SetState(VoiceSessionState.Listening);
            AudioLevelChanged?.Invoke(inputLevel, outputLevel);
            return;
        }
    }

    private void FlushPlayback()
    {
        lock (audioGate)
        {
            playbackCancellation.Cancel();
            playbackCancellation.Dispose();
            playbackCancellation = new CancellationTokenSource();
            playbackBuffer?.ClearBuffer();
            playback?.Stop();
            outputLevel = 0;
            AudioLevelChanged?.Invoke(inputLevel, outputLevel);
        }
    }

    private async Task SendEventAsync(object value, CancellationToken cancellationToken, bool logEvent = true)
    {
        var ws = socket ?? throw new InvalidOperationException("Realtime websocket is not connected.");
        if (ws.State != WebSocketState.Open) throw new WebSocketException("Realtime websocket is closed.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (logEvent)
        {
            using var json = JsonDocument.Parse(bytes);
            var eventType = json.RootElement.GetProperty("type").GetString();
            if (eventType != "input_audio_buffer.append") NEOLog.Write("ws.send", new { type = eventType, bytes = bytes.Length });
        }
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
        finally { sendGate.Release(); }
    }

    private async Task DisconnectSocketAsync()
    {
        connectionCancellation?.Cancel();
        if (socket is { State: WebSocketState.Open } ws)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "NEO closing", CancellationToken.None).ConfigureAwait(false); } catch (WebSocketException) { }
        }
        socket?.Dispose(); socket = null;
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
        FlushPlayback();
        playback?.Dispose(); playback = null;
        playbackCancellation.Dispose();
        gate.Dispose();
    }

    private sealed record AudioChunk(byte[] Buffer, int Count);
}
