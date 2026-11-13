using NEO.Infrastructure;

namespace NEO.Tests;

public sealed class PcmPlaybackBufferTests
{
    [Fact]
    public async Task BurstLargerThanCapacityPreservesEverySampleInOrder()
    {
        var buffer = new PcmPlaybackBuffer(24000);
        var expected = new byte[24000 * 2 * 7];
        new Random(42).NextBytes(expected);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var producer = buffer.EnqueueAsync(expected, timeout.Token);
        Assert.False(producer.IsCompleted); // Backpressure: a seven-second burst cannot fit.
        using var received = new MemoryStream();
        var read = new byte[4800];
        while (!producer.IsCompleted || buffer.BufferedBytes > 0)
        {
            var count = Math.Min(read.Length, buffer.BufferedBytes);
            if (count > 0)
            {
                buffer.Read(read, 0, count);
                received.Write(read, 0, count);
            }
            else await Task.Delay(1, timeout.Token);
        }
        await producer;
        Assert.Equal(expected, received.ToArray());
        Assert.Equal(24000, buffer.WaveFormat.SampleRate);
    }

    [Fact]
    public async Task FullBufferCanBeCancelledWithoutWaitingForSpeaker()
    {
        var buffer = new PcmPlaybackBuffer(24000);
        using var cancellation = new CancellationTokenSource();
        var producer = buffer.EnqueueAsync(new byte[24000 * 2 * 7], cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => producer);
    }
}
