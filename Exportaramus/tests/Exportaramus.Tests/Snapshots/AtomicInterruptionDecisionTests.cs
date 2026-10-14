using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicInterruptionDecisionTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/interruption-decisions", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedPreparationCleanupIsRetriedBeforeAnySameInstanceOperation(bool unauthorized, bool writeFirst)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var fail = true;
        var openings = 0;
        var operations = new Operations
        {
            Opening = (_, mode) =>
            {
                if (fail && mode == FileMode.CreateNew && ++openings == 2)
                    throw new IOException("Controlled second payload preparation failure.");
            },
            Deleting = path =>
            {
                if (!fail || !path.EndsWith(".tmp", StringComparison.Ordinal)) return;
                if (unauthorized) throw new UnauthorizedAccessException("Controlled temporary cleanup failure.");
                throw new IOException("Controlled temporary cleanup failure.");
            }
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        var rejected = storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, false, false, SnapshotFixture.Now, default);
        if (unauthorized) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rejected);
        else await Assert.ThrowsAsync<IOException>(() => rejected);
        Assert.Equal("Pending", storage.RecoveryState);
        Assert.NotEmpty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));

        fail = false;
        if (writeFirst)
            await storage.CommitAsync(1, "{\"retry\":true}", null, null, false, false, SnapshotFixture.Now, default);
        Assert.Equal(writeFirst ? "{\"retry\":true}" : "{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal(writeFirst ? "None" : "Recovered", storage.RecoveryState);
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(original.CanonicalPath));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    [Theory]
    [InlineData("BeforeFirstPayload")]
    [InlineData("BeforeSecondPayload")]
    [InlineData("AfterFirstInstall")]
    public async Task AnInterruptedImageRecoversOnlyThePreviouslyAcceptedOutputs(string phase)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var image = root + "-image";
        var captured = false;
        var openings = 0;
        void Capture()
        {
            if (captured) return;
            captured = true;
            foreach (var source in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(image, Path.GetRelativePath(root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
        }
        var operations = new Operations
        {
            Opening = (_, mode) =>
            {
                if (mode != FileMode.CreateNew) return;
                openings++;
                if ((phase == "BeforeFirstPayload" && openings == 1) || (phase == "BeforeSecondPayload" && openings == 2)) Capture();
            },
            Installed = destination => { if (phase == "AfterFirstInstall" && destination == original.CharacterPath(1)) Capture(); }
        };
        try
        {
            await new AtomicSnapshotStorage(root, operations).CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", null, true, false, SnapshotFixture.Now.AddSeconds(1), default);
            Assert.True(captured);
            var recovered = new AtomicSnapshotStorage(image);
            Assert.Equal("{\"old\":true}", await recovered.ReadAsync(1, false, default));
            Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(recovered.CanonicalPath));
            Assert.Empty(Directory.GetFiles(image, "*-snapshot-*.json", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(image, "*.tmp", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(image, "*.bak", SearchOption.AllDirectories));
            Assert.False(File.Exists(Path.Combine(image, "state", "write-recovery.json")));
        }
        finally { if (Directory.Exists(image)) Directory.Delete(image, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameInstanceRetriesCommittedCleanupAfterTheFilesystemBecomesAvailable(bool unauthorized)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, SnapshotFixture.Now, default);
        var fail = true;
        var operations = new Operations
        {
            Deleting = path =>
            {
                if (!fail || !path.EndsWith(".bak", StringComparison.Ordinal)) return;
                if (unauthorized) throw new UnauthorizedAccessException("Controlled backup cleanup failure.");
                throw new IOException("Controlled backup cleanup failure.");
            }
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await storage.CommitAsync(1, "{\"new\":true}", null, null, false, false, SnapshotFixture.Now, default);
        Assert.Equal("Pending", storage.RecoveryState);
        Assert.NotEmpty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));

        fail = false;
        Assert.Equal("{\"new\":true}", await storage.ReadAsync(1, false, default));

        Assert.Equal("Recovered", storage.RecoveryState);
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    [Fact]
    public async Task APartialRollbackRecordsOnlyUnrestoredFilesAndSameInstanceCompletesRecovery()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", "{\"oldRich\":true}", null, false, false, SnapshotFixture.Now, default);
        var fail = true;
        var operations = new Operations
        {
            Moving = (_, destination) =>
            {
                if (fail && Path.GetFileName(destination).StartsWith("automatic-snapshot-", StringComparison.Ordinal))
                    throw new IOException("Controlled final installation failure.");
            },
            Replacing = (source, destination) =>
            {
                if (fail && File.Exists(source + ".bak") && destination == original.CharacterPath(1))
                    throw new IOException("Controlled last restore failure.");
            }
        };
        var storage = new AtomicSnapshotStorage(root, operations);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{\"newRich\":true}", "{\"reports\":true}", true, false, SnapshotFixture.Now.AddSeconds(1), default));
        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(original.CanonicalPath));
        Assert.Equal("Failed", storage.RecoveryState);
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        Assert.NotEmpty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));

        fail = false;
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));

        Assert.Equal("{\"oldRich\":true}", await File.ReadAllTextAsync(original.CanonicalPath));
        Assert.Null(await storage.ReadAsync(1, true, default));
        Assert.Equal("Recovered", storage.RecoveryState);
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    private sealed class Operations : IAtomicFileOperations
    {
        public Action<string, FileMode>? Opening { get; init; }
        public Action<string>? Installed { get; init; }
        public Action<string>? Deleting { get; init; }
        public Action<string, string>? Moving { get; init; }
        public Action<string, string>? Replacing { get; init; }
        public Stream OpenRead(string path) => new AtomicFileOperations().OpenRead(path);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        { Opening?.Invoke(path, mode); return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options); }
        public void Move(string source, string destination) { Moving?.Invoke(source, destination); File.Move(source, destination); Installed?.Invoke(destination); }
        public void Replace(string source, string destination, string? backup) { Replacing?.Invoke(source, destination); File.Replace(source, destination, backup); Installed?.Invoke(destination); }
        public void Delete(string path) { Deleting?.Invoke(path); File.Delete(path); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
