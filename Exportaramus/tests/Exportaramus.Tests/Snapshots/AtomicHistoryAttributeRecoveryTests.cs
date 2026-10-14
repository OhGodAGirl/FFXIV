using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicHistoryAttributeRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/history-attributes", Guid.NewGuid().ToString("N"));
    private const FileAttributes Protected = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive | FileAttributes.NotContentIndexed;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PruningRollbackPreservesHistoryAttributesAndCleansOnlyPrivateCopies(bool readOnly)
    {
        var oldest = await PrepareHistory(readOnly ? Protected : Protected & ~FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(oldest);
        using var cancellation = new CancellationTokenSource();
        var operations = new Operations { AfterMove = (source, _) => { if (source == oldest) cancellation.Cancel(); } };
        var storage = new AtomicSnapshotStorage(root, operations);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Candidate(storage, cancellation.Token));

        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(oldest));
        Assert.Equal(attributes, File.GetAttributes(oldest));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal(25, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json").Length);
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task RetryOverwritesAnOwnedReadOnlyHiddenTemporaryLeftByFailedRestoration()
    {
        var oldest = await PrepareHistory(Protected);
        var attributes = File.GetAttributes(oldest);
        using var cancellation = new CancellationTokenSource();
        var fail = true;
        var operations = new Operations
        {
            AfterMove = (source, _) => { if (source == oldest) cancellation.Cancel(); },
            BeforeMove = (_, destination) => { if (fail && Path.GetFullPath(destination) == oldest) throw new IOException("history restore temporarily unavailable"); },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => Candidate(storage, cancellation.Token));
        Assert.False(File.Exists(oldest));
        var temporary = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(oldest)!, Path.GetFileName(oldest) + ".*.tmp"));
        Assert.Equal(attributes, File.GetAttributes(temporary));
        Assert.Equal(attributes, File.GetAttributes(temporary + ".bak"));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));

        fail = false;
        Assert.Equal("{}", await storage.ReadAsync(1, false, default));

        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(oldest));
        Assert.Equal(attributes, File.GetAttributes(oldest));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task StaleProgressReplayDoesNotRewriteAnAlreadyRestoredReadOnlyHistory()
    {
        var oldest = await PrepareHistory(Protected);
        var attributes = File.GetAttributes(oldest);
        using var cancellation = new CancellationTokenSource();
        var restored = false;
        var phase = 0;
        var operations = new Operations
        {
            AfterMove = (source, destination) =>
            {
                if (source == oldest) cancellation.Cancel();
                if (Path.GetFullPath(destination) == oldest) restored = true;
            },
            Opening = path => { if (restored && phase != 1 && Path.GetFullPath(path) == Path.GetFullPath(Journal + ".tmp")) throw new IOException("terminal progress temporarily unavailable"); },
            Deleting = path => { if (phase == 1 && Path.GetFullPath(path) == Path.GetFullPath(Journal)) throw new IOException("journal cleanup unavailable after backups were consumed"); },
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => Candidate(storage, cancellation.Token));
        Assert.True(restored);
        Assert.Equal(attributes, File.GetAttributes(oldest));
        Assert.NotEmpty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.True(File.Exists(Journal));

        phase = 1;
        await Assert.ThrowsAsync<IOException>(() => storage.ReadAsync(1, false, default));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.True(File.Exists(Journal));
        Assert.Equal(attributes, File.GetAttributes(oldest));
        phase = 2;
        Assert.Equal("{}", await storage.ReadAsync(1, false, default));

        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(oldest));
        Assert.Equal(attributes, File.GetAttributes(oldest));
        Assert.Equal("Recovered", storage.Diagnostic.RecoveryState);
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public async Task SuccessfulPruningCanDeleteItsReadOnlyPrivateBackupWithoutChangingUnrelatedFiles()
    {
        var oldest = await PrepareHistory(Protected);
        var unrelated = Path.Combine(root, "retained-user-file.json");
        await File.WriteAllTextAsync(unrelated, "keep");
        File.SetAttributes(unrelated, Protected);
        var unrelatedAttributes = File.GetAttributes(unrelated);
        var storage = new AtomicSnapshotStorage(root);

        var result = await Candidate(storage, default);

        Assert.Equal(25, result.HistoryCount);
        Assert.False(File.Exists(oldest));
        Assert.Equal("{\"new\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("None", storage.Diagnostic.RecoveryState);
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.Equal(unrelatedAttributes, File.GetAttributes(unrelated));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    private async Task<string> PrepareHistory(FileAttributes attributes)
    {
        var storage = new AtomicSnapshotStorage(root);
        for (var index = 0; index < 25; index++)
            await storage.CommitAsync(1, "{}", "{\"old\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(index), default);
        var histories = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json").Order(StringComparer.Ordinal).ToArray();
        for (var index = 0; index < histories.Length; index++) File.SetLastWriteTimeUtc(histories[index], SnapshotFixture.Now.AddSeconds(index).UtcDateTime);
        File.SetAttributes(histories[0], attributes);
        return Path.GetFullPath(histories[0]);
    }
    private static Task<StorageCommitResult> Candidate(AtomicSnapshotStorage storage, CancellationToken token) => storage.CommitAsync(1, "{\"new\":true}", "{\"new\":true}", null, true, false, SnapshotFixture.Now.AddMinutes(1), token);
    private string Journal => Path.Combine(root, "state", "write-recovery.json");
    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string, string>? BeforeMove { get; init; }
        public Action<string, string>? AfterMove { get; init; }
        public Action<string>? Opening { get; init; }
        public Action<string>? Deleting { get; init; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        { Opening?.Invoke(path); return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options); }
        public void Move(string source, string destination) { BeforeMove?.Invoke(source, destination); File.Move(source, destination); AfterMove?.Invoke(source, destination); }
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) { Deleting?.Invoke(path); File.Delete(path); }
    }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
