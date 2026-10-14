using System.Text;
using System.Text.Json;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class AtomicStorageTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/test-storage", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");

    [Fact]
    public async Task WritesCanonicalAndExactCharacterStateWithoutBom()
    {
        var storage = new AtomicSnapshotStorage(root);
        Assert.Null(await storage.ReadAsync(1, false, default));
        var first = await storage.CommitAsync(1, "{\"revision\":1}", "{\"rich\":1}", "{\"items\":[]}", true, true, now, default);
        Assert.Equal(1, first.HistoryCount); Assert.NotNull(first.LatestHistoryUtc);
        Assert.Equal("{\"revision\":1}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"items\":[]}", await storage.ReadAsync(1, true, default));
        Assert.Equal("{\"rich\":1}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal((byte)'{', (await File.ReadAllBytesAsync(storage.CanonicalPath))[0]);
        Assert.Null(await storage.ReadAsync(2, false, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => storage.CharacterPath(0));
    }

    [Fact]
    public async Task UnchangedFullCaptureReplacesCanonicalWithoutHistoryOrTouchingOtherFiles()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{}", "{\"at\":1}", null, true, false, now, default);
        var unrelated = Path.Combine(root, "snapshots", "manual-snapshot-unrelated.json");
        await File.WriteAllTextAsync(unrelated, "keep");
        var second = await storage.CommitAsync(1, "{\"at\":2}", "{\"at\":2}", null, false, true, now.AddSeconds(1), default);
        Assert.Equal(1, second.HistoryCount); Assert.Equal("{\"at\":2}", await File.ReadAllTextAsync(storage.CanonicalPath));
        Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task ValidationAndCancellationPreserveAllPreviousAcceptedFiles()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{\"old\":true}", "{\"old\":true}", null, false, false, now, default);
        await Assert.ThrowsAnyAsync<Exception>(() => storage.CommitAsync(1, "{}", "invalid", null, true, false, now, default));
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Equal("{\"old\":true}", await File.ReadAllTextAsync(storage.CanonicalPath));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.CommitAsync(1, "{}", "{}", null, true, false, now, cancellation.Token));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RequiredLaterWriteFailureRollsBackAlreadyReplacedCharacterState()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        Directory.CreateDirectory(storage.CanonicalPath);
        await Assert.ThrowsAnyAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{}", null, false, false, now, default));
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task HistoryRetainsNewestTwentyFiveAndTimestampCollisionDoesNotReplaceState()
    {
        var storage = new AtomicSnapshotStorage(root);
        for (int i = 0; i < 27; i++)
            await storage.CommitAsync(1, $"{{\"revision\":{i}}}", $"{{\"revision\":{i}}}", null, true, i % 2 == 0, now.AddSeconds(i), default);
        var history = Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json");
        Assert.Equal(25, history.Length);
        Assert.False(File.Exists(Path.Combine(root, "snapshots", "manual-snapshot-20260906-000000-000.json")));
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{}", "{}", null, true, true, now.AddSeconds(26), default));
        Assert.Equal("{\"revision\":26}", await storage.ReadAsync(1, false, default));
    }

    [Fact]
    public async Task OversizedReadAndWriteAreRejectedWithoutTruncation()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{}", null, null, false, false, now, default);
        var oversized = "{\"padding\":\"" + new string('x', 4194304) + "\"}";
        await Assert.ThrowsAsync<OutputSizeLimitException>(() => storage.CommitAsync(1, oversized, null, null, false, false, now, default));
        Assert.Equal("{}", await storage.ReadAsync(1, false, default));
        await File.WriteAllTextAsync(storage.CharacterPath(1), oversized);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, false, default));
        await File.WriteAllBytesAsync(storage.CharacterPath(1), [0xc3, 0x28]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => storage.ReadAsync(1, false, default));
    }

    [Fact]
    public async Task StartupRollsBackInterruptedReplacementFromExactRecoveryRecord()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        var path = original.CharacterPath(1);
        var temporary = path + "." + new string('a', 32) + ".tmp";
        await File.WriteAllTextAsync(temporary, "{\"new\":true}");
        File.Replace(temporary, path, temporary + ".bak");
        WriteRecovery(false, (path, temporary, true, false));
        var recovered = new AtomicSnapshotStorage(root);
        Assert.Equal("{\"old\":true}", await recovered.ReadAsync(1, false, default));
        Assert.Equal("Recovered", recovered.RecoveryState);
        Assert.False(File.Exists(temporary + ".bak")); Assert.False(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    [Fact]
    public async Task CommittedRecoveryRetainsReplacementButRemovedEvidenceIsNeverReconstructed()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", null, "{\"old\":true}", false, false, now, default);
        var path = original.CharacterPath(1); var temporary = path + "." + new string('b', 32) + ".tmp";
        await File.WriteAllTextAsync(temporary, "{\"new\":true}"); File.Replace(temporary, path, temporary + ".bak");
        WriteRecovery(true, (path, temporary, true, false));
        Assert.Equal("{\"new\":true}", await new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
        var evidence = original.EvidencePath(1); var evidenceTemporary = evidence + "." + new string('c', 32) + ".tmp";
        File.Move(evidence, evidenceTemporary + ".bak");
        WriteRecovery(false, (evidence, evidenceTemporary, true, false));
        Assert.Null(await new AtomicSnapshotStorage(root).ReadAsync(1, true, default));
        Assert.False(File.Exists(evidence)); Assert.False(File.Exists(evidenceTemporary + ".bak"));
    }

    [Fact]
    public async Task RecoveryRemovesNewInterruptedFileAndRestoresRetainedHistory()
    {
        var storage = new AtomicSnapshotStorage(root); var path = storage.CharacterPath(1);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"new\":true}");
        var temporary = path + "." + new string('d', 32) + ".tmp";
        var history = Path.Combine(root, "snapshots", "manual-snapshot-20260906-000000-000.json");
        var historyTemporary = history + "." + new string('e', 32) + ".tmp";
        await File.WriteAllTextAsync(historyTemporary + ".bak", "{\"history\":true}");
        WriteRecovery(false, (path, temporary, false, false), (history, historyTemporary, true, true));
        Assert.Null(await new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
        Assert.Equal("{\"history\":true}", await File.ReadAllTextAsync(history));
    }

    [Fact]
    public async Task MalformedRecoveryCannotDeleteUnrelatedPaths()
    {
        var path = Path.Combine(root, "unrelated.json"); Directory.CreateDirectory(root); await File.WriteAllTextAsync(path, "keep");
        WriteRecovery(false, (path, path + "." + new string('f', 32) + ".tmp", false, false));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
    }

    [Fact]
    public async Task FailedRollbackPreservesBackupAndRecoveryUntilStorageCanRecover()
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        var fault = new FaultFiles
        {
            BeforeMove = (_, destination) => { if (destination.EndsWith("current-snapshot.json", StringComparison.Ordinal)) throw new IOException("canonical unavailable"); },
            BeforeReplace = (source, _, _) => { if (File.Exists(source + ".bak")) throw new IOException("rollback temporarily unavailable"); },
        };
        var storage = new AtomicSnapshotStorage(root, fault);
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{}", null, false, false, now, default));
        Assert.Equal("Failed", storage.RecoveryState);
        Assert.NotEmpty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
        Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        Assert.Equal("{\"old\":true}", await new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostCommitCleanupFailureDoesNotRejectDurableNewState(bool unauthorized)
    {
        var original = new AtomicSnapshotStorage(root);
        await original.CommitAsync(1, "{}", null, null, false, false, now, default);
        var storage = new AtomicSnapshotStorage(root, new FaultFiles
        {
            BeforeDelete = path => { if (path.EndsWith(".bak", StringComparison.Ordinal)) { if (unauthorized) throw new UnauthorizedAccessException(); throw new IOException(); } },
        });
        var result = await storage.CommitAsync(1, "{\"accepted\":true}", null, null, false, false, now, default);
        Assert.Equal(0, result.HistoryCount); Assert.Equal("Pending", storage.RecoveryState);
        var recovered = new AtomicSnapshotStorage(root);
        Assert.Equal("{\"accepted\":true}", await recovered.ReadAsync(1, false, default));
        Assert.Equal("Recovered", recovered.RecoveryState);
    }

    [Fact]
    public async Task GrowingReadCannotBypassLimitAfterInitialLengthCheck()
    {
        var original = new AtomicSnapshotStorage(root); await original.CommitAsync(1, "{}", null, null, false, false, now, default);
        var storage = new AtomicSnapshotStorage(root, new FaultFiles { Read = _ => new GrowingStream(new byte[4194305]) });
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, false, default));
    }

    [Fact]
    public async Task PreparedButUncommittedRecoveryCleansOnlyNamedTemporaries()
    {
        var storage = new AtomicSnapshotStorage(root); await storage.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        var path = storage.CharacterPath(1); var temporary = path + "." + new string('1', 32) + ".tmp";
        await File.WriteAllTextAsync(temporary, "{\"candidate\":true}");
        var newPath = storage.CharacterPath(2); Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        var newTemporary = newPath + "." + new string('2', 32) + ".tmp"; await File.WriteAllTextAsync(newTemporary, "{}");
        var absentPath = storage.CharacterPath(3); var absentTemporary = absentPath + "." + new string('3', 32) + ".tmp";
        var unrelated = Path.Combine(root, "unrelated.tmp"); await File.WriteAllTextAsync(unrelated, "keep");
        WriteRecovery(false, (path, temporary, true, false), (newPath, newTemporary, false, false), (absentPath, absentTemporary, false, false));
        Assert.Equal("{\"old\":true}", await new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
        Assert.False(File.Exists(temporary)); Assert.False(File.Exists(newTemporary)); Assert.Equal("keep", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task MissingBackupDuringRollbackIsReportedAndJournalIsPreserved()
    {
        var original = new AtomicSnapshotStorage(root); await original.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        var storage = new AtomicSnapshotStorage(root, new FaultFiles
        {
            BeforeMove = (_, destination) =>
            {
                if (!destination.EndsWith("current-snapshot.json", StringComparison.Ordinal)) return;
                foreach (var backup in Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories)) File.Delete(backup);
                throw new IOException("Failure with an unavailable prior backup");
            },
        });
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{\"new\":true}", "{}", null, false, false, now, default));
        Assert.Equal("Failed", storage.RecoveryState); Assert.True(File.Exists(Path.Combine(root, "state", "write-recovery.json")));
        await Assert.ThrowsAsync<IOException>(() => new AtomicSnapshotStorage(root).ReadAsync(1, false, default));
    }

    [Fact]
    public async Task RollbackToleratesAlreadyRemovedNewDestination()
    {
        var storage = new AtomicSnapshotStorage(root, new FaultFiles
        {
            BeforeMove = (_, destination) =>
            {
                if (!destination.EndsWith("current-snapshot.json", StringComparison.Ordinal)) return;
                File.Delete(Path.Combine(root, "snapshots", "characters", "1", "collection-data.json"));
                throw new IOException();
            },
        });
        await Assert.ThrowsAsync<IOException>(() => storage.CommitAsync(1, "{}", "{}", null, false, false, now, default));
        Assert.Null(await storage.ReadAsync(1, false, default));
    }

    [Fact]
    public async Task TooManyRecoveryEntriesRejectBeforeReplacingAnyCurrentFile()
    {
        var storage = new AtomicSnapshotStorage(root); await storage.CommitAsync(1, "{\"old\":true}", null, null, false, false, now, default);
        var directory = Path.Combine(root, "snapshots");
        for (int i = 0; i < 90; i++) await File.WriteAllTextAsync(Path.Combine(directory, $"manual-snapshot-20260905-000000-{i:000}.json"), "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.CommitAsync(1, "{}", "{}", null, true, false, now, default));
        Assert.Equal("{\"old\":true}", await storage.ReadAsync(1, false, default));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("version")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("duplicate")]
    [InlineData("temporary")]
    [InlineData("temporaryPrefix")]
    [InlineData("historyPrefix")]
    [InlineData("historySuffix")]
    [InlineData("backup")]
    [InlineData("delete")]
    [InlineData("deleteMissing")]
    [InlineData("deleteCanonical")]
    [InlineData("size")]
    public async Task RecoveryRejectsMalformedRecordsWithoutMutatingFiles(string defect)
    {
        var storage = new AtomicSnapshotStorage(root); var path = storage.CharacterPath(1); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "keep");
        var temporary = path + "." + new string('f', 32) + ".tmp";
        WriteRecovery(false, (path, temporary, true, false));
        var recovery = Path.Combine(root, "state", "write-recovery.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(recovery))!;
        var entries = json["files"]!.AsArray();
        switch (defect)
        {
            case "format": json["format"] = "other"; break;
            case "version": json["version"] = 2; break;
            case "empty": entries.Clear(); break;
            case "count": while (entries.Count < 65) entries.Add(entries[0]!.DeepClone()); break;
            case "duplicate": entries.Add(entries[0]!.DeepClone()); break;
            case "temporary": entries[0]!["temporary"] = "../elsewhere.tmp"; break;
            case "temporaryPrefix":
                entries[0]!["temporary"] = "unrelated/" + entries[0]!["temporary"]!.GetValue<string>();
                entries[0]!["backup"] = entries[0]!["temporary"]!.GetValue<string>() + ".bak";
                break;
            case "historyPrefix":
            case "historySuffix":
                var destination = defect == "historyPrefix" ? "elsewhere/manual-snapshot-20260905-000000-000.json" : "snapshots/../unrelated.json";
                entries[0]!["destination"] = destination;
                entries[0]!["temporary"] = destination + "." + new string('f', 32) + ".tmp";
                entries[0]!["backup"] = entries[0]!["temporary"]!.GetValue<string>() + ".bak";
                break;
            case "backup": entries[0]!["backup"] = "../elsewhere.bak"; break;
            case "delete": entries[0]!["delete"] = true; break;
            case "deleteMissing": entries[0]!["delete"] = true; entries[0]!["existed"] = false; break;
            case "deleteCanonical":
                entries[0]!["destination"] = "current-snapshot.json"; entries[0]!["temporary"] = "current-snapshot.json." + new string('f', 32) + ".tmp";
                entries[0]!["backup"] = entries[0]!["temporary"]!.GetValue<string>() + ".bak"; entries[0]!["delete"] = true; break;
            case "size": json["padding"] = new string('x', 65536); break;
        }
        await File.WriteAllTextAsync(recovery, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, false, default));
        Assert.Equal("Failed", storage.RecoveryState); Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CleanupToleratesRecoveryRecordAlreadyRemovedAfterCommittedBackups()
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{}", null, null, false, false, now, default);
        var files = new FaultFiles
        {
            BeforeDelete = path =>
        {
            if (path.EndsWith(".bak", StringComparison.Ordinal)) File.Delete(Path.Combine(root, "state", "write-recovery.json"));
        }
        };
        var next = new AtomicSnapshotStorage(root, files);
        await next.CommitAsync(1, "{\"new\":true}", null, null, false, false, now, default);
        Assert.Equal("None", next.RecoveryState); Assert.Equal("{\"new\":true}", await next.ReadAsync(1, false, default));
    }

    private sealed class FaultFiles : IAtomicFileOperations
    {
        public Action<string, string>? BeforeMove;
        public Action<string, string, string?>? BeforeReplace;
        public Action<string>? BeforeDelete;
        public Func<string, Stream>? Read;
        public Stream OpenRead(string path) => Read?.Invoke(path) ?? new AtomicFileOperations().OpenRead(path);
        public void Replace(string source, string destination, string? backup) { BeforeReplace?.Invoke(source, destination, backup); File.Replace(source, destination, backup); }
        public void Move(string source, string destination) { BeforeMove?.Invoke(source, destination); File.Move(source, destination); }
        public void Delete(string path) { BeforeDelete?.Invoke(path); File.Delete(path); }
    }

    private sealed class GrowingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override long Length => 1;
    }

    private void WriteRecovery(bool committed, params (string Destination, string Temporary, bool Existed, bool Delete)[] files)
    {
        Directory.CreateDirectory(Path.Combine(root, "state"));
        File.WriteAllText(Path.Combine(root, "state", "write-recovery.json"), JsonSerializer.Serialize(new
        {
            format = "exportaramus-write-recovery",
            version = 1,
            committed,
            files = files.Select(f => new
            {
                destination = Path.GetRelativePath(root, f.Destination).Replace('\\', '/'),
                temporary = Path.GetRelativePath(root, f.Temporary).Replace('\\', '/'),
                backup = Path.GetRelativePath(root, f.Temporary).Replace('\\', '/') + ".bak",
                existed = f.Existed,
                delete = f.Delete
            }).ToArray(),
        }));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
