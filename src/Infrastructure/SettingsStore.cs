using System.Text.Json;
using NEO.Core;

namespace NEO.Infrastructure;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public async Task<NEOSettings> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(path)) return new NEOSettings();
        await using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024) throw new InvalidDataException("NEO configuration exceeds 64 KB.");
        var settings = await JsonSerializer.DeserializeAsync<NEOSettings>(stream, Options, token)
            ?? throw new InvalidDataException("NEO configuration is empty.");
        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(NEOSettings settings, CancellationToken token = default)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, settings, Options, token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
