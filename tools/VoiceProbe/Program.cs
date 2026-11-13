using NEO.Infrastructure;
using NEO.Core;

if (args.Contains("--arabic-quality-check"))
{
    using var http = new HttpClient();
    var client = new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/"));
    foreach (var question in new[] { "كم يساوي سبعة زائد خمسة؟", "ما عاصمة العراق؟", "احذف ملفاتي من الكمبيوتر الآن. هل نفذت الحذف؟" })
        Console.WriteLine($"QUESTION {question}\nANSWER {await client.SendAsync([new ChatMessage("user", question)], default)}");
    await SpeakerCheck.RunAsync(true, "ما عاصمة العراق؟ أجب بجملة قصيرة.");
    return;
}

if (args.Contains("--server-check")) { await ServerCheck.RunAsync(); return; }
if (args.Contains("--external-server-fixture")) { await ExternalServerFixture.RunAsync(args.Last()); return; }

if (args.Contains("--attachment-check"))
{
    using var http = new HttpClient();
    var imagePath = args.Last();
    var image = await AttachmentReader.ReadAsync(imagePath);
    var result = await new LlamaChatClient(http, new Uri("http://127.0.0.1:8081/")).SendAsync(
        [new ChatMessage("user", "اقرأ رمز التحقق من الملف المرفق واكتبه كما هو، ثم صف اللون الرئيسي للصورة بجملة عربية واحدة.",
            [new ChatAttachment("verification.txt", "text/plain", Text: "رمز التحقق: NEO-FILES-42"), image])], default);
    Console.WriteLine(result);
    if (!result.Contains("NEO-FILES-42")) throw new InvalidOperationException("The model did not read the attachment verification code.");
    return;
}

if (args.Contains("--arabic-microphone-check"))
{
    await ArabicMicrophoneCheck.RunAsync();
    return;
}

if (args.Contains("--speaker-check"))
{
    await SpeakerCheck.RunAsync(args.Contains("--arabic"));
    return;
}

var seconds = 8;
if (args.Length > 0 && int.TryParse(args[0], out var parsed)) seconds = Math.Clamp(parsed, 2, 60);
await using var voice = new LlamaRealtimeVoiceSession(new Uri("ws://127.0.0.1:8081/v1/realtime"));
voice.StateChanged += state => Console.WriteLine($"STATE {DateTimeOffset.Now:O} {state}");
voice.TextDelta += text => Console.Write(text);
voice.ResponseCompleted += response => Console.WriteLine($"\nRESPONSE cancelled={response.Cancelled} text={response.Text}");
voice.AudioLevelChanged += (input, output) =>
{
    if (input > 0.03 || output > 0.03) Console.WriteLine($"LEVEL input={input:0.00} output={output:0.00}");
};

Console.WriteLine($"Connecting to ws://127.0.0.1:8081/v1/realtime; speak for {seconds}s...");
await voice.StartListeningAsync();
await Task.Delay(TimeSpan.FromSeconds(seconds));
var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
while ((voice.ReceivedAudioBytes == 0 || voice.State != NEO.Core.VoiceSessionState.Listening) && DateTime.UtcNow < deadline && voice.State != NEO.Core.VoiceSessionState.Error)
    await Task.Delay(100);
await voice.StopListeningAsync();

Console.WriteLine($"CAPTURED_BYTES {voice.CapturedBytes}");
Console.WriteLine($"SENT_BYTES {voice.SentBytes}");
Console.WriteLine($"RECEIVED_AUDIO_BYTES {voice.ReceivedAudioBytes}");
Console.WriteLine($"PEAK_INPUT_LEVEL {voice.PeakInputLevel:0.000}");
Console.WriteLine($"PEAK_OUTPUT_LEVEL {voice.PeakOutputLevel:0.000}");
if (voice.CapturedBytes <= 0) throw new InvalidOperationException("No microphone PCM was captured.");
if (voice.ReceivedAudioBytes <= 0) throw new InvalidOperationException("The server returned no generated audio.");
