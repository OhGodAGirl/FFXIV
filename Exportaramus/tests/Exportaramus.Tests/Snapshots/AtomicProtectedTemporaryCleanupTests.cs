using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicProtectedTemporaryCleanupTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/protected-temporary", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RecoveryCleansAnOwnedTemporaryProtectedWhileUnloadedWithoutChangingAcceptedOrUnrelatedFiles()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"accepted\":true}", "{\"acceptedRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var interrupted = new AtomicSnapshotStorage(root, new InterruptedPreparation());
        await Assert.ThrowsAsync<IOException>(() => interrupted.CommitAsync(1, "{\"candidate\":true}", "{\"candidateRich\":true}", null, false, false, SnapshotFixture.Now.AddSeconds(1), default));
        var temporary = Path.GetFullPath(Assert.Single(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories)));
        Assert.StartsWith(original.CharacterPath(1) + ".", temporary, StringComparison.Ordinal);
        var journal = Path.Combine(root, "state", "write-recovery.json");
        var journalBytes = await File.ReadAllBytesAsync(journal);
        // Simulate an attribute change while storage is unloaded; preserve the real interrupted journal.
        File.SetAttributes(temporary, File.GetAttributes(temporary) | FileAttributes.ReadOnly);
        var unrelated = Path.Combine(Path.GetDirectoryName(temporary)!, "unrelated.tmp");
        await File.WriteAllTextAsync(unrelated, "leave untouched");
        File.SetAttributes(unrelated, FileAttributes.ReadOnly | FileAttributes.Archive);
        var unrelatedAttributes = File.GetAttributes(unrelated);
        Assert.Equal(journalBytes, await File.ReadAllBytesAsync(journal));

        var recovered = new AtomicSnapshotStorage(root);
        Assert.Equal("{\"accepted\":true}", await recovered.ReadAsync(1, false, default));

        Assert.Equal("{\"acceptedRich\":true}", await File.ReadAllTextAsync(recovered.CanonicalPath));
        Assert.Equal("Recovered", recovered.Diagnostic.RecoveryState);
        Assert.False(File.Exists(temporary));
        Assert.False(File.Exists(journal));
        Assert.Equal("leave untouched", await File.ReadAllTextAsync(unrelated));
        Assert.Equal(unrelatedAttributes, File.GetAttributes(unrelated));
    }

    private sealed class InterruptedPreparation : IAtomicFileOperations
    {
        private int payloads;
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        {
            if (mode == FileMode.CreateNew && ++payloads == 2) throw new IOException("Second payload preparation unavailable.");
            return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        }
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path)
        {
            if (path.EndsWith(".tmp", StringComparison.Ordinal)) throw new IOException("Temporary cleanup unavailable during initial failure.");
            File.Delete(path);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
