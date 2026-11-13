using NEO.Core;
using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("http://example.com:8081/")]
    [InlineData("file:///health")]
    [InlineData("http://user:secret@127.0.0.1/")]
    public void RejectsRemoteOrUnsafeEndpoints(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new Settings { HttpEndpoint = endpoint }.Validate());

    [Fact]
    public void RejectsNonFiniteGeometry() =>
        Assert.Throws<ArgumentException>(() => new Settings { Width = double.NaN }.Validate());

    [Fact]
    public async Task RoundTripsSettingsAndRejectsMalformedJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NEO-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "NEO.settings.json");
            var store = new SettingsStore(path);
            Assert.Equal(new Settings(), await store.LoadAsync());
            var expected = new Settings { Width = 300, Monitor = 1 };
            await store.SaveAsync(expected);
            Assert.Equal(expected, await store.LoadAsync());
            await File.WriteAllTextAsync(path, "{broken");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
