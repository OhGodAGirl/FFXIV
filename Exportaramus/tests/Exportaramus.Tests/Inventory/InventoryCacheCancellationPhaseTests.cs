using System.Reflection;
using Exportaramus.Features.Inventory;
namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCacheCancellationPhaseTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationStopsTheNextCacheIoPhase(bool afterWrite)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-phase-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var ordinary = new InventoryFileCache(directory);
            var snapshot = InventoryServiceTests.Saved(1);
            await ordinary.SaveAsync(snapshot, default);
            var path = ordinary.CharacterPath(1);
            var prior = await File.ReadAllBytesAsync(path);
            RecordingStream? stream = null;
            var readBackCalls = 0;
            Func<string, FileStream> write = temporary => stream = new RecordingStream(temporary, cancellation, afterWrite);
            Func<string, CancellationToken, Task<byte[]>> read = (temporary, token) =>
            {
                readBackCalls++;
                return File.ReadAllBytesAsync(temporary, token);
            };
            var cache = (InventoryFileCache)Activator.CreateInstance(typeof(InventoryFileCache), BindingFlags.Instance | BindingFlags.NonPublic, null, [directory, null, null, write, read], null)!;
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SaveAsync(snapshot, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.NotNull(stream);
            if (afterWrite) Assert.Equal(0, stream.DurableFlushCalls);
            else Assert.True(stream.DurableFlushCalls >= 1);
            Assert.Equal(0, readBackCalls);
            Assert.Equal(prior, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class RecordingStream(string path, CancellationTokenSource cancellation, bool afterWrite)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)
    {
        public int DurableFlushCalls
        {
            get; private set;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            await base.WriteAsync(buffer, token).ConfigureAwait(false);
            if (afterWrite) cancellation.Cancel();
        }
        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk) DurableFlushCalls++;
            base.Flush(flushToDisk);
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            if (!afterWrite) cancellation.Cancel();
        }
    }
}
