using System.Text;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class StorageMetadataDecisionTests
{
    [Fact]
    public async Task MetadataTracksActualUtf8BytesCanonicalRetentionAndBothHistoryKinds()
    {
        var root = Root(); var storage = new AtomicSnapshotStorage(root); var now = SnapshotFixture.Now;
        const string compact = "{\"name\":\"é\"}";
        const string firstRich = "{\"name\":\"first é\"}";
        const string secondRich = "{\"name\":\"second longer é\"}";
        const string reports = "{\"items\":[]}";
        try
        {
            Assert.False(storage.Diagnostic.CanonicalAvailable); Assert.Equal("Never", storage.Diagnostic.LastRead.Status);
            Assert.Null(await storage.ReadAsync(1, false, default));
            Assert.Equal("NotFound", storage.Diagnostic.LastRead.Status); Assert.Equal(0, storage.Diagnostic.LastRead.ByteCount);
            var compactOnly = await storage.CommitAsync(1, compact, null, null, false, false, now, default);
            Assert.Equal(0, compactOnly.HistoryCount); Assert.Equal(0, compactOnly.HistoryBytes); Assert.Null(compactOnly.LatestHistoryUtc);
            Assert.False(storage.Diagnostic.CanonicalAvailable); Assert.Equal(0, storage.Diagnostic.CanonicalBytes);
            Assert.Equal(Bytes(compact), storage.Diagnostic.LastWrite.ByteCount);
            Assert.Equal(compact, await storage.ReadAsync(1, false, default));
            Assert.Equal("Succeeded", storage.Diagnostic.LastRead.Status); Assert.Equal(Bytes(compact), storage.Diagnostic.LastRead.ByteCount);
            Assert.Null(storage.Diagnostic.LastRead.FailureCode);

            var first = await storage.CommitAsync(1, compact, firstRich, reports, true, true, now, default);
            var manual = Path.Combine(root, "snapshots", "manual-snapshot-20260907-000000-000.json");
            Assert.Equal(firstRich, await File.ReadAllTextAsync(manual));
            Assert.Equal(now, first.LatestHistoryUtc); Assert.Equal(Bytes(firstRich), first.HistoryBytes);
            Assert.True(storage.Diagnostic.CanonicalAvailable); Assert.Equal(Bytes(firstRich), storage.Diagnostic.CanonicalBytes);
            Assert.Equal(Bytes(compact) + Bytes(firstRich) + Bytes(reports), storage.Diagnostic.LastWrite.ByteCount);
            Assert.Equal("Succeeded", storage.Diagnostic.LastWrite.Status); Assert.Null(storage.Diagnostic.LastWrite.FailureCode);
            File.SetLastWriteTimeUtc(manual, now.AddSeconds(10).UtcDateTime);

            var retained = await storage.CommitAsync(1, compact, null, reports, true, false, now.AddSeconds(1), default);
            Assert.Equal(now.AddSeconds(10), retained.LatestHistoryUtc);
            Assert.Equal(1, retained.HistoryCount); Assert.Equal(Bytes(firstRich), retained.HistoryBytes);
            Assert.True(storage.Diagnostic.CanonicalAvailable); Assert.Equal(Bytes(firstRich), storage.Diagnostic.CanonicalBytes);
            Assert.Equal(Bytes(compact) + Bytes(reports), storage.Diagnostic.LastWrite.ByteCount);

            var second = await storage.CommitAsync(1, compact, secondRich, reports, true, false, now.AddSeconds(2), default);
            var automatic = Path.Combine(root, "snapshots", "automatic-snapshot-20260907-000002-000.json");
            Assert.Equal(secondRich, await File.ReadAllTextAsync(automatic));
            Assert.Equal(now.AddSeconds(2), second.LatestHistoryUtc);
            Assert.Equal(2, second.HistoryCount); Assert.Equal(Bytes(firstRich) + Bytes(secondRich), second.HistoryBytes);
            Assert.Equal(2, storage.Diagnostic.HistoryFileCount); Assert.Equal(second.HistoryBytes, storage.Diagnostic.HistoryBytes);
            File.SetLastWriteTimeUtc(automatic, now.AddSeconds(20).UtcDateTime);

            var unchanged = await storage.CommitAsync(1, compact, "{}", null, false, true, now.AddSeconds(3), default);
            Assert.Equal(now.AddSeconds(20), unchanged.LatestHistoryUtc);
            Assert.Equal(2, unchanged.HistoryCount); Assert.Equal(second.HistoryBytes, unchanged.HistoryBytes);
            Assert.Equal(2, storage.Diagnostic.CanonicalBytes);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "snapshots"), "*-snapshot-*.json").Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ReadAndWriteFailuresPublishDistinctDiagnosticsWhileRetainingAcceptedStorageMetadata()
    {
        var root = Root(); var measurements = new DiagnosticMeasurements(); var storage = new AtomicSnapshotStorage(root, measurements: measurements);
        try
        {
            await storage.CommitAsync(1, "{}", "{\"saved\":true}", null, true, false, SnapshotFixture.Now, default);
            var accepted = storage.Diagnostic;
            await Assert.ThrowsAnyAsync<Exception>(() => storage.CommitAsync(1, "bad JSON", "{}", null, true, false, SnapshotFixture.Now.AddSeconds(1), default));
            Assert.Equal("Failed", storage.Diagnostic.LastWrite.Status); Assert.Equal("WriteFailed", storage.Diagnostic.LastWrite.FailureCode);
            Assert.Equal(0, storage.Diagnostic.LastWrite.ByteCount);
            Assert.Equal(accepted.CanonicalBytes, storage.Diagnostic.CanonicalBytes); Assert.True(storage.Diagnostic.CanonicalAvailable);
            Assert.Equal(accepted.HistoryFileCount, storage.Diagnostic.HistoryFileCount); Assert.Equal(accepted.HistoryBytes, storage.Diagnostic.HistoryBytes);
            var writeFailure = Assert.Single(measurements.Failures);
            Assert.Equal("Storage", writeFailure.Component); Assert.Equal("Write", writeFailure.Operation); Assert.Equal("WriteFailed", writeFailure.Code);

            await File.WriteAllBytesAsync(storage.CharacterPath(1), [255]);
            await Assert.ThrowsAsync<DecoderFallbackException>(() => storage.ReadAsync(1, false, default));
            Assert.Equal("Failed", storage.Diagnostic.LastRead.Status); Assert.Equal("ReadFailed", storage.Diagnostic.LastRead.FailureCode);
            Assert.Equal(0, storage.Diagnostic.LastRead.ByteCount);
            Assert.Equal(2, measurements.Failures.Length);
            var readFailure = measurements.Failures[1];
            Assert.Equal("Storage", readFailure.Component); Assert.Equal("Read", readFailure.Operation); Assert.Equal("ReadFailed", readFailure.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    private static int Bytes(string value) => Encoding.UTF8.GetByteCount(value);
    private static string Root() => Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/storage-metadata-decisions", Guid.NewGuid().ToString("N"));
}
