using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationAdmissionBoundaryTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "publication-admission", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AlreadyCancelledPublicationDoesNotBeginSerializationOrDiskWork()
    {
        var measurements = new DiagnosticMeasurements();
        var publisher = new CollectionDataPublisher(new AtomicSnapshotStorage(root, measurements: measurements), null, measurements);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var operation = publisher.PrepareAndPersistAsync(SnapshotFixture.Create(), "Fixture", 1, null, false, false, cancellation.Token);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.True(operation.IsCanceled);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(measurements.Snapshot());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task InvalidCompactRevisionCannotReachStorageOrReplaceAcceptedFiles()
    {
        var data = SnapshotFixture.Create();
        var storage = new AtomicSnapshotStorage(root);
        var publisher = new CollectionDataPublisher(storage, null);
        var accepted = await publisher.PrepareAndPersistAsync(data, "Fixture", 1, null, false, false, default);
        var prior = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

        var operation = publisher.PrepareAndPersistAsync(data, "Fixture", 0, accepted, false, false, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => operation);

        Assert.True(operation.IsFaulted);
        Assert.Equal(prior.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        foreach (var file in prior) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
        Assert.Equal(accepted.CompactJsonV12, await storage.ReadAsync(123, false, default));
    }

    [Fact]
    public async Task CancellationAtThePreparedPublicationBoundaryPreventsRecoveryAndEveryFileOperation()
    {
        var measurements = new DiagnosticMeasurements();
        var storage = new AtomicSnapshotStorage(root, measurements: measurements);
        var publisher = new CollectionDataPublisher(storage, null, measurements);
        var prepared = publisher.Prepare(SnapshotFixture.Create(), "Fixture", 1, null, false, false, default);
        Assert.False(Directory.Exists(root));
        Assert.True(prepared.Bundle.CompactBytes > 0);
        Assert.DoesNotContain("FileWrite", measurements.Snapshot().Keys);

        // A prior interrupted preparation has a valid journal and an uninstalled temporary payload.
        var recovery = Path.Combine(root, "state", "write-recovery.json");
        var relative = "snapshots/characters/123/collection-data.json";
        var temporary = relative + "." + new string('a', 32) + ".tmp";
        var payload = Path.GetFullPath(Path.Combine(root, temporary));
        Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        await File.WriteAllTextAsync(payload, prepared.Bundle.CompactJson);
        await File.WriteAllTextAsync(recovery, System.Text.Json.JsonSerializer.Serialize(new
        {
            format = "exportaramus-write-recovery",
            version = 1,
            committed = false,
            rollbackFailed = false,
            files = new[] { new { destination = relative, temporary, backup = temporary + ".bak", existed = false, delete = false, applied = false } }
        }));
        var before = await File.ReadAllBytesAsync(recovery);
        var payloadBefore = await File.ReadAllBytesAsync(payload);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PersistAsync(prepared, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(before, await File.ReadAllBytesAsync(recovery));
        Assert.Equal(payloadBefore, await File.ReadAllBytesAsync(payload));
        Assert.Equal(new[] { recovery, payload }.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        Assert.DoesNotContain("FileWrite", measurements.Snapshot().Keys);
        Assert.Equal("Never", storage.Diagnostic.LastWrite.Status);
        Assert.Equal("None", storage.RecoveryState);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
