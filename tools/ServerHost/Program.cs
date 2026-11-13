using System.Diagnostics;
using System.Runtime.InteropServices;

// A private hidden console lets Ctrl+C reach ONLY our child, never the user's console.
var input = new StreamReader(Console.OpenStandardInput());
var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
var errors = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
Native.FreeConsole();
if (!Native.AllocConsole()) throw new InvalidOperationException("Cannot allocate private server console.");
Native.ShowWindow(Native.GetConsoleWindow(), 0);
Native.Handler handler = _ => true;
Native.SetConsoleCtrlHandler(handler, true);
try
{
    if (args.Length < 1) throw new ArgumentException("Server executable missing.");
    var start = new ProcessStartInfo(args[0]) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = Path.GetDirectoryName(args[0])!, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in args.Skip(1)) start.ArgumentList.Add(argument);
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Server did not start.");
    await output.WriteLineAsync($"NEO_SERVER_PID={child.Id}");
    var stdout = Pump(child.StandardOutput, output);
    var stderr = Pump(child.StandardError, errors);
    var exited = child.WaitForExitAsync();
    var stop = input.ReadLineAsync(); // Stop command OR parent pipe EOF after parent exit/crash.
    if (await Task.WhenAny(exited, stop) != exited)
    {
        Native.GenerateConsoleCtrlEvent(0, 0);
        await errors.WriteLineAsync("NEO_SERVER_STOP=CTRL_C");
        try { await exited.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException)
        {
            await errors.WriteLineAsync("NEO_SERVER_STOP=OWNED_PROCESS_TIMEOUT");
            if (!child.HasExited) child.Kill();
            await exited;
        }
    }
    await Task.WhenAll(stdout, stderr);
    Environment.ExitCode = child.ExitCode;
}
catch (Exception ex) { await errors.WriteLineAsync(ex.ToString()); Environment.ExitCode = 1; }
finally { GC.KeepAlive(handler); Native.FreeConsole(); }

static async Task Pump(StreamReader source, StreamWriter target)
{
    while (await source.ReadLineAsync() is { } line) await target.WriteLineAsync(line);
}
internal static class Native
{
    internal delegate bool Handler(uint signal);
    [DllImport("kernel32.dll")] internal static extern bool AllocConsole();
    [DllImport("kernel32.dll")] internal static extern bool FreeConsole();
    [DllImport("kernel32.dll")] internal static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] internal static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    [DllImport("kernel32.dll")] internal static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr handle, int command);
}
