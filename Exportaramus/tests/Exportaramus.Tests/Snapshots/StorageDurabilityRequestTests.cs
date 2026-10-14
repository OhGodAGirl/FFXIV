using Microsoft.Win32.SafeHandles;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class StorageDurabilityRequestTests
{
    [Fact]
    public async Task RealTemporaryStreamsRequestWriteThroughAndFlushToDiskBeforeAtomicInstallation()
    {
        var root = Root(); var operations = new Operations(); var storage = new AtomicSnapshotStorage(root, operations);
        try
        {
            await storage.CommitAsync(1, "{\"revision\":1}", "{\"rich\":1}", "{\"items\":[]}", true, true, SnapshotFixture.Now, default);

            var payloads = operations.Writers.Where(writer => writer.Mode == FileMode.CreateNew).ToArray();
            Assert.Equal(4, payloads.Length);
            Assert.All(payloads, writer => Assert.Equal(FileOptions.Asynchronous | FileOptions.WriteThrough, writer.Options));
            Assert.All(operations.Writers, writer =>
            {
                Assert.True(writer.WroteBytes);
                Assert.True(writer.FlushedToDisk);
                Assert.True(writer.CapturedHandle.IsClosed);
                Assert.True(writer.Installed);
                Assert.True(writer.Options.HasFlag(FileOptions.WriteThrough));
            });
            Assert.Equal("{\"revision\":1}", await storage.ReadAsync(1, false, default));
            Assert.Equal("{\"rich\":1}", await File.ReadAllTextAsync(storage.CanonicalPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AFailedDurabilityRequestPreventsEveryPayloadInstallationAndPreservesPriorFiles()
    {
        var root = Root(); var original = new AtomicSnapshotStorage(root);
        try
        {
            await original.CommitAsync(1, "{\"revision\":1}", "{\"rich\":1}", "{\"items\":[]}", true, true, SnapshotFixture.Now, default);
            var priorHistory = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json");
            var operations = new Operations { FailPayloadFlush = true };
            var storage = new AtomicSnapshotStorage(root, operations);

            await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"revision\":2}", "{\"rich\":2}", "{\"items\":[2]}", true, false, SnapshotFixture.Now.AddSeconds(1), default));

            Assert.Equal(0, operations.InstalledPayloads);
            var failed = Assert.Single(operations.Writers, writer => writer.Mode == FileMode.CreateNew);
            Assert.True(failed.WroteBytes); Assert.True(failed.CapturedHandle.IsClosed); Assert.False(failed.FlushedToDisk);
            Assert.Equal("{\"revision\":1}", await original.ReadAsync(1, false, default));
            Assert.Equal("{\"items\":[]}", await original.ReadAsync(1, true, default));
            Assert.Equal("{\"rich\":1}", await File.ReadAllTextAsync(original.CanonicalPath));
            Assert.Equal(priorHistory, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Root() => Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/durability-request-decisions", Guid.NewGuid().ToString("N"));

    private sealed class Operations : IAtomicFileOperations
    {
        public List<Writer> Writers { get; } = [];
        public bool FailPayloadFlush { get; init; }
        public int InstalledPayloads { get; private set; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        {
            var writer = new Writer(path, mode, bufferSize, options, FailPayloadFlush && mode == FileMode.CreateNew);
            Writers.Add(writer); return writer;
        }
        public void Replace(string source, string destination, string? backup) { VerifyInstall(source); File.Replace(source, destination, backup); }
        public void Move(string source, string destination) { VerifyInstall(source); File.Move(source, destination); }
        public void Delete(string path) => File.Delete(path);
        private void VerifyInstall(string source)
        {
            var writer = Writers.LastOrDefault(writer => writer.Path == source);
            if (writer is null) return;
            Assert.True(writer.WroteBytes); Assert.True(writer.FlushedToDisk); Assert.True(writer.CapturedHandle.IsClosed);
            if (writer.Mode == FileMode.CreateNew)
            {
                Assert.All(Writers.Where(value => value.Mode == FileMode.CreateNew), value =>
                { Assert.True(value.FlushedToDisk); Assert.True(value.CapturedHandle.IsClosed); });
                InstalledPayloads++;
            }
            writer.Installed = true;
        }
    }

    private sealed class Writer : FileStream
    {
        private readonly bool failFlush;
        public string Path { get; }
        public FileMode Mode { get; }
        public FileOptions Options { get; }
        public SafeFileHandle CapturedHandle { get; }
        public bool WroteBytes { get; private set; }
        public bool FlushedToDisk { get; private set; }
        public bool Installed { get; set; }
        public Writer(string path, FileMode mode, int bufferSize, FileOptions options, bool failFlush)
            : base(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
        { Path = path; Mode = mode; Options = options; this.failFlush = failFlush; CapturedHandle = SafeFileHandle; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); WroteBytes = buffer.Length != 0; }
        public override void Write(ReadOnlySpan<byte> buffer) { base.Write(buffer); WroteBytes = buffer.Length != 0; }
        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk)
            {
                Assert.True(WroteBytes);
                if (failFlush) throw new IOException("Durability request fixture failure.");
            }
            base.Flush(flushToDisk);
            if (flushToDisk) FlushedToDisk = true;
        }
    }
}
