using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class StorageCancellationBoundaryTests
{
    [Fact]
    public async Task CancellationAfterPayloadWriteAvoidsAnUnnecessaryDurableFlushAndPreservesAcceptedFiles()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "cancel-before-durability", Guid.NewGuid().ToString("N"));
        var original = new AtomicSnapshotStorage(root);
        try
        {
            await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
            using var cancellation = new CancellationTokenSource();
            var files = new Operations(cancellation);
            var storage = new AtomicSnapshotStorage(root, files);

            var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(1,
                "{\"new\":true}", "{\"newRich\":true}", null, false, false, SnapshotFixture.Now, cancellation.Token));

            Assert.Equal(cancellation.Token, failure.CancellationToken);
            Assert.Equal(1, files.PayloadWrites);
            Assert.Equal(0, files.PayloadDurabilityRequests);
            Assert.Equal("{\"old\":true}", await original.ReadAsync(1, false, default));
            Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(original.CanonicalPath));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Operations(CancellationTokenSource cancellation) : IAtomicFileOperations
    {
        public int PayloadWrites { get; set; }
        public int PayloadDurabilityRequests { get; set; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            new Writer(path, mode, bufferSize, options, this, cancellation);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class Writer(string path, FileMode mode, int bufferSize, FileOptions options, Operations owner, CancellationTokenSource cancellation)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        private readonly bool isPayload = mode == FileMode.CreateNew;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (isPayload) { owner.PayloadWrites++; cancellation.Cancel(); }
        }
        public override void Flush(bool flushToDisk)
        {
            if (isPayload && flushToDisk) owner.PayloadDurabilityRequests++;
            base.Flush(flushToDisk);
        }
    }
}
