using System.Reflection;
using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCacheReadbackCancellationTests
{
    [Fact]
    public async Task CancellationAfterReadbackPreservesThePreviousCache()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "cache-readback-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var ordinary = new InventoryFileCache(directory);
            var snapshot = InventoryServiceTests.Saved(1);
            await ordinary.SaveAsync(snapshot, default);
            var path = ordinary.CharacterPath(1);
            var prior = await File.ReadAllBytesAsync(path);
            var readBackCalls = 0;
            Func<string, CancellationToken, Task<byte[]>> read = async (temporary, token) =>
            {
                var bytes = await File.ReadAllBytesAsync(temporary, token);
                readBackCalls++;
                cancellation.Cancel();
                return bytes;
            };
            var cache = (InventoryFileCache)Activator.CreateInstance(typeof(InventoryFileCache), BindingFlags.Instance | BindingFlags.NonPublic, null, [directory, null, null, null, read], null)!;
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SaveAsync(snapshot with { Revision = snapshot.Revision + 1 }, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(1, readBackCalls);
            Assert.Equal(prior, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
