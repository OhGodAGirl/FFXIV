using Exportaramus.Bootstrap;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Snapshots;

public sealed class ConfigurationFilenameBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "configuration-filename-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TemporaryNameOverflowPreservesTheExistingConfiguration()
    {
        var limit = ComponentLimit();
        var path = Path.Combine(directory, new string('c', limit - 36));
        File.WriteAllText(path, "{\"value\":1}");
        await Assert.ThrowsAnyAsync<IOException>(() => ConfigurationFile.WriteAsync(path, "{\"value\":2}"));
        Assert.Equal("{\"value\":1}", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task TemporaryNameAtTheComponentLimitCanReplaceTheConfiguration()
    {
        var limit = ComponentLimit();
        var path = Path.Combine(directory, new string('c', limit - 37));
        File.WriteAllText(path, "{\"value\":1}");
        await ConfigurationFile.WriteAsync(path, "{\"value\":2}");
        Assert.Equal("{\"value\":2}", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }

    private int ComponentLimit()
    {
        Directory.CreateDirectory(directory);
        bool CanCreate(int length)
        {
            var path = Path.Combine(directory, new string('p', length));
            try { File.WriteAllText(path, "probe"); File.Delete(path); return true; }
            catch (IOException error) when (error is PathTooLongException || (error.HResult & 0xffff) == 123) { return false; }
        }
        var low = 1;
        var high = 1024;
        Assert.True(CanCreate(low));
        Assert.False(CanCreate(high));
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (CanCreate(middle)) low = middle; else high = middle;
        }
        Assert.True(low > 37);
        output.WriteLine($"Measured filename component limit: {low}; filesystem: {new DriveInfo(Path.GetPathRoot(directory)!).DriveFormat}.");
        return low;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
