using Exportaramus.Features.Snapshots;
using Exportaramus.Tests.Performance;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Snapshots;

[Collection(OfflinePerformanceCollection.Name)]
public sealed class HistoryPreparationAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task UnrelatedFilesDoNotScaleManagedHistoryPreparationAllocation()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "history-preparation", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(folder);
        try
        {
            for (var index = 0; index < 25; index++)
                await File.WriteAllTextAsync(Path.Combine(folder, $"manual-snapshot-20260905-000000-{index:000}.json"), "{}");
            var files = new MeasuringFiles();
            var storage = new AtomicSnapshotStorage(root, files);
            for (var warmup = 0; warmup < 3; warmup++) await Measure(storage, files);
            var baseline = long.MaxValue;
            for (var sample = 0; sample < 3; sample++) baseline = Math.Min(baseline, await Measure(storage, files));

            for (var index = 0; index < 2048; index++)
                await File.WriteAllTextAsync(Path.Combine(folder, $"unrelated-{index:0000}.dat"), "keep");
            var populated = long.MaxValue;
            for (var sample = 0; sample < 3; sample++) populated = Math.Min(populated, await Measure(storage, files));

            output.WriteLine($"History preparation allocated {baseline} bytes with 25 valid histories; {populated} bytes with the same 25 plus 2,048 unrelated files.");
            // Fixed I/O setup has generous headroom; materializing thousands of unrelated full paths does not.
            Assert.True(populated - baseline < 128 * 1024, $"Unrelated filenames added {populated - baseline} bytes to history preparation.");
            Assert.Equal(25, storage.Diagnostic.HistoryFileCount);
            Assert.Equal(25, Directory.GetFiles(folder, "*-snapshot-*.json").Length);
            Assert.Equal(2048, Directory.GetFiles(folder, "unrelated-*.dat").Length);
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(folder, "unrelated-0000.dat")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<long> Measure(AtomicSnapshotStorage storage, MeasuringFiles files)
    {
        files.BeginMeasurement();
        var pending = storage.CommitAsync(1, "{}", null, null, false, false, SnapshotFixture.Now, default);
        // History discovery and preparation happen synchronously before the first payload writer opens.
        Assert.True(files.Observed);
        Assert.Equal(files.StartThread, files.ObservedThread);
        await pending;
        return files.AllocatedBytes;
    }

    private sealed class MeasuringFiles : IAtomicFileOperations
    {
        private long started;
        public int StartThread { get; private set; }
        public int ObservedThread { get; private set; }
        public bool Observed { get; private set; }
        public long AllocatedBytes { get; private set; }
        public void BeginMeasurement() { Observed = false; StartThread = Environment.CurrentManagedThreadId; started = GC.GetAllocatedBytesForCurrentThread(); }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        {
            if (mode == FileMode.CreateNew && !Observed)
            {
                AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - started;
                ObservedThread = Environment.CurrentManagedThreadId;
                Observed = true;
            }
            return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        }
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }
}
