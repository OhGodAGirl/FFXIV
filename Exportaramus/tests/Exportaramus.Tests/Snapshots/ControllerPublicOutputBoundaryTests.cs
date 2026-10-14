using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerPublicOutputBoundaryTests
{
    [Fact]
    public async Task EachAcceptedCaptureKindPublishesItsPreparedCapabilitiesAlongsideItsSnapshot()
    {
        await using var fixture = new Fixture();
        var controller = fixture.Controller;
        await Pump(controller, 123, false, false, 0, () => controller.Reports.Available);
        var restoredCapabilities = controller.Publication.GetCapabilities();

        controller.RequestFull();
        await Pump(controller, 123, false, false, 1, () => controller.Publication.Current is not null);
        var full = Accepted(controller, CaptureType.Full);
        Assert.NotEqual(restoredCapabilities, full.CapabilitiesJson);

        fixture.Port.InventoryUnavailable = true;
        controller.RequestPresence();
        await Pump(controller, 123, false, false, 2, () => controller.Publication.Current!.Revision > full.Revision);
        var presence = Accepted(controller, CaptureType.Lightweight);
        Assert.NotEqual(full.CapabilitiesJson, presence.CapabilitiesJson);

        Assert.Equal("Accepted", controller.Reports.TryEnqueue("123", 100, "Fixture", 3, SnapshotFixture.Now));
        await Pump(controller, 123, false, false, 3, () => controller.Publication.Current!.Revision > presence.Revision);
        var reported = Accepted(controller, CaptureType.Reported);
        Assert.Equal(100u, Assert.Single(reported.Data.State.ReportedEvidence.Items).ItemId);
        Assert.Equal(2, fixture.Port.BeginCount);
        Assert.True(fixture.Files.WriterCount > 0);
        Assert.Null(controller.Error);
    }

    [Fact]
    public async Task EvidencePersistenceFailurePublishesUnavailableReportCapabilitiesWithoutReplacingAcceptedBytes()
    {
        await using var fixture = new Fixture();
        var controller = fixture.Controller;
        await Pump(controller, 123, false, false, 0, () => controller.Reports.Available);
        controller.RequestFull();
        await Pump(controller, 123, false, false, 1, () => controller.Publication.Current is not null);
        var accepted = Accepted(controller, CaptureType.Full);
        var priorCompact = await File.ReadAllBytesAsync(fixture.Storage.CharacterPath(123));
        var priorRich = await File.ReadAllBytesAsync(fixture.Storage.CanonicalPath);
        Assert.NotEqual(accepted.CapabilitiesJson, accepted.UnavailableReportsCapabilitiesJson);
        fixture.Files.FailEvidence = true;

        Assert.Equal("Accepted", controller.Reports.TryEnqueue("123", 100, "Fixture", 2, SnapshotFixture.Now));
        await Pump(controller, 123, false, false, 2, () => !controller.Reports.Available);

        Assert.True(fixture.Files.EvidenceInstallAttempts > 0);
        Assert.NotNull(controller.Error);
        Assert.Same(accepted, controller.Publication.Current);
        Assert.Equal(accepted.CompactJson, controller.Publication.GetSnapshot());
        Assert.Equal(accepted.UnavailableReportsCapabilitiesJson, controller.Publication.GetCapabilities());
        using var capabilities = JsonDocument.Parse(controller.Publication.GetCapabilities());
        Assert.False(capabilities.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
        Assert.Equal(priorCompact, await File.ReadAllBytesAsync(fixture.Storage.CharacterPath(123)));
        Assert.Equal(priorRich, await File.ReadAllBytesAsync(fixture.Storage.CanonicalPath));
        Assert.Equal("Unavailable", controller.Reports.TryEnqueue("123", 100, "Fixture", 3, SnapshotFixture.Now));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task NoActiveIdentityPreparesDiscoveryOnlyWithoutRestorationReadsCaptureOrSynchronizationError(bool loggedOut, bool zoning)
    {
        await using var fixture = new Fixture();
        var controller = fixture.Controller;
        await Pump(controller, 0, loggedOut, zoning, 0, () => !controller.Busy && controller.Publication.GetCapabilities().Length != 0);
        for (var tick = 0; tick < 20; tick++)
        {
            controller.RequestFull(); controller.RequestPresence();
            controller.Update(0, "", loggedOut, zoning, 1, SnapshotFixture.Now.AddSeconds(1));
            await Task.Delay(1);
        }

        Assert.Equal(0ul, controller.ActiveContentId);
        Assert.Empty(controller.Publication.GetSnapshot());
        Assert.Null(controller.Error);
        Assert.False(controller.Busy);
        Assert.Equal(0, fixture.Port.BeginCount); Assert.Equal(0, fixture.Port.AdvanceCount); Assert.Equal(0, fixture.Port.TakeCount);
        Assert.Equal(0, fixture.Port.RestoreScopeReadCount);
        Assert.Equal(0, fixture.Files.ReadCount); Assert.Equal(0, fixture.Files.WriterCount);
        Assert.Equal("Never", controller.StorageDiagnostic.LastRead.Status);
        Assert.False(fixture.Measurements.Snapshot().ContainsKey("FileRead"));
        Assert.False(fixture.Measurements.Snapshot().ContainsKey("FileWrite"));
        Assert.Empty(fixture.Measurements.Failures);
    }

    [Fact]
    public async Task ARecognizedZoningIdentityGapDoesNotStartTheQueuedCaptureOrAnotherRestore()
    {
        await using var fixture = new Fixture();
        var controller = fixture.Controller;
        await Pump(controller, 123, false, false, 0, () => controller.Reports.Available);
        var restoreScopes = fixture.Port.RestoreScopeReadCount;
        Assert.Equal(1, restoreScopes);
        var openReads = fixture.Files.ReadCount;
        controller.RequestFull();

        for (var tick = 0; tick < 20; tick++)
        {
            controller.Update(0, "", false, true, 1, SnapshotFixture.Now.AddSeconds(1));
            await Task.Delay(1);
        }

        Assert.Equal(123ul, controller.ActiveContentId);
        Assert.Null(controller.Error); Assert.False(controller.Busy);
        Assert.Empty(controller.Publication.GetSnapshot());
        Assert.Equal(0, fixture.Port.BeginCount); Assert.Equal(0, fixture.Port.AdvanceCount); Assert.Equal(0, fixture.Port.TakeCount);
        Assert.Equal(openReads, fixture.Files.ReadCount); Assert.Equal(0, fixture.Files.WriterCount);
        Assert.Equal(restoreScopes, fixture.Port.RestoreScopeReadCount);
    }

    private static AcceptedSnapshotBundle Accepted(SnapshotController controller, CaptureType type)
    {
        var accepted = Assert.IsType<AcceptedSnapshotBundle>(controller.Publication.Current);
        Assert.Equal(type, accepted.Data.State.CaptureType);
        Assert.Equal(accepted.CompactJson, controller.Publication.GetSnapshot());
        Assert.Equal(accepted.CapabilitiesJson, controller.Publication.GetCapabilities());
        return accepted;
    }

    private static async Task Pump(SnapshotController controller, ulong id, bool loggedOut, bool zoning, double monotonic, Func<bool> complete)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            controller.Update(id, id == 0 ? "" : "Fixture", loggedOut, zoning, monotonic, SnapshotFixture.Now.AddSeconds(monotonic));
            if (complete()) return;
            await Task.Delay(2);
        }
        Assert.Fail("Controller did not reach the requested public state: " + controller.Error);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(AppContext.BaseDirectory, "controller-public-output", Guid.NewGuid().ToString("N"));
        public Port Port { get; } = new();
        public Files Files { get; } = new();
        public DiagnosticMeasurements Measurements { get; } = new();
        public AtomicSnapshotStorage Storage { get; }
        public SnapshotController Controller { get; }
        public Fixture()
        {
            Storage = new(root, Files, Measurements);
            Controller = new(Port, Storage, null, Measurements);
        }
        public async ValueTask DisposeAsync()
        {
            Controller.Dispose();
            await Controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(3));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Port : ISnapshotCapturePort
    {
        private readonly CollectionData data = SnapshotFixture.Create();
        private RawCaptureBatch? result;
        public CollectionCatalog Catalog => data.Catalog;
        public ImmutableHashSet<string> TrustedSourceKeys
        {
            get { RestoreScopeReadCount++; return ImmutableHashSet<string>.Empty; }
        }
        public int RestoreScopeReadCount { get; private set; }
        public bool InventoryUnavailable { get; set; }
        public int BeginCount { get; private set; }
        public int AdvanceCount { get; private set; }
        public int TakeCount { get; private set; }
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            BeginCount++;
            var unlocks = data.State.Unlocks.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Health = pair.Value.Health with { CaptureGeneration = generation, ObservedAtUtc = now } });
            var presence = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Health = pair.Value.Health with { CaptureGeneration = generation, ObservedAtUtc = now } });
            if (InventoryUnavailable)
                presence = presence.SetItem("inventory", presence["inventory"] with { Items = [], Health = CaptureReadPolicy.Health(DataType.CurrentItemPresence, generation, now, ReasonCode.NotLoadedThisSession) });
            result = new(full ? unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, presence, full ? data.State.Quests : []);
        }
        public bool Advance() { AdvanceCount++; return true; }
        public RawCaptureBatch? TakeResult() { TakeCount++; return result; }
        public void ResetSession() => result = null;
        public void Dispose() => ResetSession();
    }

    private sealed class Files : IAtomicFileOperations
    {
        private int reads;
        private int writers;
        private int evidenceInstalls;
        public int ReadCount => Volatile.Read(ref reads);
        public int WriterCount => Volatile.Read(ref writers);
        public int EvidenceInstallAttempts => Volatile.Read(ref evidenceInstalls);
        public bool FailEvidence { get; set; }
        public Stream OpenRead(string path) { Interlocked.Increment(ref reads); return new AtomicFileOperations().OpenRead(path); }
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options)
        { Interlocked.Increment(ref writers); return new(path, mode, FileAccess.Write, FileShare.None, bufferSize, options); }
        public void Move(string source, string destination) { Check(destination); File.Move(source, destination); }
        public void Replace(string source, string destination, string? backup) { Check(destination); File.Replace(source, destination, backup); }
        public void Delete(string path) => File.Delete(path);
        private void Check(string destination)
        {
            if (!destination.EndsWith("reported-evidence.json", StringComparison.Ordinal)) return;
            Interlocked.Increment(ref evidenceInstalls);
            if (FailEvidence) throw new IOException("Evidence installation fixture failure.");
        }
    }
}
