using Exportaramus.Features.Inventory;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Inventory;

public sealed class InventoryCachePathBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "cache-path-boundaries", Guid.NewGuid().ToString("N"));

    private string? cleanupAnchor;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicCacheReplacementHonorsTheMeasuredFullPathBoundary(bool temporaryFits)
    {
        var deep = root;
        while (deep.Length < 32499)
        {
            deep = Path.Combine(deep, new string('d', Math.Min(200, 32500 - deep.Length - 1)));
            if (deep.Length < 32000) cleanupAnchor = deep;
        }
        Directory.CreateDirectory(deep);
        bool CanWrite(int length)
        {
            var cache = new InventoryFileCache(Path.Combine(deep, new string('p', length)));
            var path = cache.CharacterPath(1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "probe");
                File.Delete(path);
                return true;
            }
            catch (IOException error) when (error is PathTooLongException or DirectoryNotFoundException || (error.HResult & 0xffff) == 123)
            {
                return false;
            }
        }
        var low = 1;
        var high = 255;
        Assert.True(CanWrite(low));
        Assert.False(CanWrite(high));
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (CanWrite(middle)) low = middle; else high = middle;
        }
        Assert.True(low > 41);
        var shortCache = new InventoryFileCache(Path.Combine(root, "baseline"));
        var saved = InventoryServiceTests.Saved(1);
        await shortCache.SaveAsync(saved, default);
        var initial = await File.ReadAllBytesAsync(shortCache.CharacterPath(1));
        var committed = false;
        var cacheUnderTest = new InventoryFileCache(Path.Combine(deep, new string('p', low - (temporaryFits ? 37 : 36))), commitFile: (_, _) => committed = true);
        var destination = cacheUnderTest.CharacterPath(1);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, initial);
        output.WriteLine($"Measured maximum complete file path: {deep.Length + 1 + low + destination.Length - (deep.Length + 1 + low - (temporaryFits ? 37 : 36))}; filesystem: {new DriveInfo(Path.GetPathRoot(root)!).DriveFormat}.");
        var changed = saved with { Revision = saved.Revision + 1 };
        if (temporaryFits)
        {
            await cacheUnderTest.SaveAsync(changed, default);
            Assert.True(committed);
        }
        else
        {
            await Assert.ThrowsAnyAsync<IOException>(() => cacheUnderTest.SaveAsync(changed, default));
            Assert.False(committed);
        }
        Assert.Equal(initial, await File.ReadAllBytesAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(Path.GetDirectoryName(destination)!));
    }

    public void Dispose()
    {
        if (cleanupAnchor is not null && Directory.Exists(cleanupAnchor))
            Directory.Move(cleanupAnchor, Path.Combine(root, "short-cleanup"));
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
