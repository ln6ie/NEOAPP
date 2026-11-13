using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace NEO.Infrastructure;

public static class NEOLog
{
    private static readonly Channel<string> Lines = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private static Task? writer;
    private static long dropped;
    private static readonly string Run = Guid.NewGuid().ToString("N")[..8];

    public static void Start(string path)
    {
        if (writer != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        writer = Task.Run(async () =>
        {
            await foreach (var line in Lines.Reader.ReadAllAsync())
            {
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                        File.Move(path, path + ".previous", true);
                    await File.AppendAllTextAsync(path, line + Environment.NewLine);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Trace.WriteLine($"NEO log write failed: {ex.Message}"); }
            }
        });
        Write("app.log.started", new { path, process = Environment.ProcessId });
    }

    public static void Write(string name, object? data = null)
    {
        if (writer == null) return;
        var line = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, run = Run, thread = Environment.CurrentManagedThreadId, name, data, dropped = Interlocked.Read(ref dropped) });
        if (!Lines.Writer.TryWrite(line)) Interlocked.Increment(ref dropped);
    }

    public static void Error(string name, Exception error) => Write(name, new { type = error.GetType().FullName, error.Message, error.StackTrace });

    public static async Task StopAsync()
    {
        Lines.Writer.TryComplete();
        if (writer != null) await writer.ConfigureAwait(false);
    }
}
