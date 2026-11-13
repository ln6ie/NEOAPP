using System.Diagnostics;
using NEO.Core;
using NEO.Infrastructure;

internal static class ExternalServerFixture
{
    public static async Task RunAsync(string hostPath)
    {
        var settings = new Settings();
        var start = new ProcessStartInfo(Path.GetFullPath(hostPath)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(settings.ServerExecutablePath);
        foreach (var arg in ServerManager.ServerArguments(settings, 8081)) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--verbose");
        using var host = Process.Start(start) ?? throw new InvalidOperationException("Fixture host did not start.");
        var stdout = Pump(host.StandardOutput);
        var stderr = Pump(host.StandardError);
        Console.WriteLine($"EXTERNAL_FIXTURE_HOST_PID {host.Id}; send Enter to stop this fixture only.");
        await Console.In.ReadLineAsync();
        await host.StandardInput.WriteLineAsync("stop");
        await host.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Console.WriteLine($"EXTERNAL_FIXTURE_EXIT {host.ExitCode}");
    }
    private static async Task Pump(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            if (line.Contains("CUDA", StringComparison.OrdinalIgnoreCase) || line.Contains("offload", StringComparison.OrdinalIgnoreCase) || line.StartsWith("NEO_SERVER_", StringComparison.Ordinal) || line.Contains("listening", StringComparison.OrdinalIgnoreCase)) Console.WriteLine(line);
    }
}
