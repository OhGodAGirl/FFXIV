using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Diagnostics;

namespace Exportaramus.Tests.Snapshots;

public sealed class ControllerTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "isolated-test-recovery/test-controller", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");

    [Fact]
    public async Task ManualCapturePublishesAfterDurableWriteAndThrowingLocalSubscriberCannotRollback()
    {
        var port = new FakePort(); var storage = new AtomicSnapshotStorage(root);
        using var controller = new SnapshotController(port, storage, null);
        var notices = new List<string>();
        controller.Changed += _ => throw new InvalidOperationException("consumer fixture");
        controller.Changed += json => { Assert.NotEmpty(controller.Publication.GetSnapshot()); Assert.True(File.Exists(storage.CharacterPath(1))); notices.Add(json); };
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull();
        await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        var bundle = controller.Publication.Current!;
        Assert.Equal(bundle.CompactJsonV12, await storage.ReadAsync(1, false, default));
        await Pump(controller, 1, () => notices.Count > 0, 2);
        Assert.Equal(1, port.BeginCount); Assert.NotEmpty(await File.ReadAllTextAsync(storage.CanonicalPath));
        controller.Dispose(); await controller.ShutdownTask;
        Assert.Equal("", controller.Publication.GetSnapshot()); Assert.True(port.Disposed);
    }

    [Fact]
    public async Task LogoutAndCharacterSwitchRejectAnInProgressOldCapture()
    {
        var port = new FakePort(); using var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        var withdrawals = new List<string>(); controller.Withdrawn += withdrawals.Add;
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); controller.Update(1, "Test", false, false, 1, Now.AddSeconds(1));
        Assert.True(controller.Busy);
        controller.Update(2, "Second", false, false, 2, Now.AddSeconds(2));
        Assert.Equal(2ul, controller.ActiveContentId); Assert.Empty(controller.Publication.GetSnapshot()); Assert.True(port.ResetCount > 0);
        Assert.Contains("CharacterChanged", Assert.Single(withdrawals));
        controller.Update(0, "", true, false, 3, Now.AddSeconds(3));
        Assert.Equal(0ul, controller.ActiveContentId); Assert.Equal(2, withdrawals.Count);
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task CaptureFailurePreservesLastPublicationAndCopyState()
    {
        var port = new FakePort(); using var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        var previous = controller.Publication.Current;
        port.Reject = true; controller.RequestFull();
        await Pump(controller, 1, () => controller.Error != null, 2);
        Assert.Same(previous, controller.Publication.Current);
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task InvalidPersistedCompactDoesNotPreventFreshCapture()
    {
        var storage = new AtomicSnapshotStorage(root);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(1))!);
        await File.WriteAllTextAsync(storage.CharacterPath(1), "not JSON");
        var port = new FakePort(); using var controller = new SnapshotController(port, storage, null);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull();
        await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        Assert.Equal(1, port.BeginCount); Assert.NotEmpty(controller.Publication.GetSnapshot());
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task LogoutBeforeFirstCapturePublishesNoSnapshotAndDisposalIsIdempotent()
    {
        var port = new FakePort(); var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        controller.RequestFull(); controller.RequestPresence();
        controller.Update(0, "", true, false, 0, Now);
        Assert.Equal("", controller.Publication.GetSnapshot()); Assert.Equal(0, port.BeginCount);
        controller.Dispose(); controller.Dispose(); await controller.ShutdownTask;
        controller.Update(1, "No", false, false, 100, Now); Assert.Equal(0ul, controller.ActiveContentId);
    }

    [Fact]
    public async Task MalformedEvidenceRemainsUnavailableAfterSuccessfulFullCapture()
    {
        var storage = new AtomicSnapshotStorage(root); Directory.CreateDirectory(Path.GetDirectoryName(storage.EvidencePath(1))!);
        await File.WriteAllTextAsync(storage.EvidencePath(1), "invalid evidence");
        var port = new FakePort(); using var controller = new SnapshotController(port, storage, null);
        controller.Update(1, "Test", false, false, 0, Now); controller.RequestFull();
        await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        Assert.False(controller.Reports.Available);
        Assert.Equal("Unavailable", controller.Reports.TryEnqueue("1", 100, "Fixture", 2, Now));
        using var capabilities = JsonDocument.Parse(controller.Publication.GetCapabilities());
        Assert.False(capabilities.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
        Assert.Equal("invalid evidence", await File.ReadAllTextAsync(storage.EvidencePath(1)));
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task EvidenceWriteFailureDisablesIntakeAndPreservesPriorPublication()
    {
        var storage = new AtomicSnapshotStorage(root, new FailEvidenceWrites());
        using var controller = new SnapshotController(new FakePort(), storage, null);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        var previous = controller.Publication.Current;
        Assert.Equal("Accepted", controller.Reports.TryEnqueue("1", 100, "Fixture", 2, Now));
        await Pump(controller, 1, () => !controller.Reports.Available, 2);
        Assert.Same(previous, controller.Publication.Current);
        Assert.Equal(previous!.CompactJsonV12, await storage.ReadAsync(1, false, default));
        Assert.Equal("Unavailable", controller.Reports.TryEnqueue("1", 100, "Fixture", 3, Now));
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task DisposalObservesCaptureDependencyShutdownAndInitialCapabilityWork()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = new FakePort { ShutdownTask = completion.Task };
        var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        controller.Update(0, "", true, false, 0, Now);
        Assert.Empty(controller.Publication.GetCapabilities());
        Assert.True(controller.Busy);
        controller.Dispose(); Assert.False(controller.ShutdownTask.IsCompleted);
        completion.SetResult(); await controller.ShutdownTask;
        Assert.Empty(controller.Publication.GetSnapshot()); Assert.True(port.Disposed);
    }

    [Fact]
    public async Task PendingCatalogAndCaptureAreBoundedAndLightweightKeepsUnlocks()
    {
        var port = new FakePort { CatalogReady = false, AdvanceRemaining = 1 };
        using var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        controller.Update(1, "Test", false, false, 0, Now);
        Assert.False(controller.Busy); Assert.Equal(0, port.BeginCount);
        port.CatalogReady = true; controller.Update(1, "Test", false, false, 0, Now);
        Assert.True(controller.Busy);
        await Pump(controller, 1, () => controller.Reports.Available, 0); Assert.False(controller.Busy);
        controller.RequestFull(); controller.Update(1, "Test", false, false, 1, Now);
        Assert.True(controller.Busy); controller.Update(1, "Test", false, false, 1, Now);
        Assert.True(controller.Busy); Assert.Null(controller.Publication.Current);
        controller.Update(1, "Test", false, false, 1, Now); Assert.True(controller.Busy);
        await Pump(controller, 1, () => controller.Publication.Current != null, 1);
        var full = controller.Publication.Current!;
        controller.RequestPresence(); await Pump(controller, 1, () => controller.Publication.Current!.Revision > full.Revision, 2);
        var lightweight = controller.Publication.Current!;
        Assert.Equal(CaptureType.Lightweight, lightweight.Data.State.CaptureType);
        Assert.Same(full.Data.State.Unlocks, lightweight.Data.State.Unlocks);
        controller.Update(0, "", false, true, 3, Now);
        Assert.Equal(1ul, controller.ActiveContentId); Assert.Same(lightweight, controller.Publication.Current);
        controller.Dispose(); controller.RequestFull(); controller.RequestPresence(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task SynchronousCancellationEndsReadWithoutPublishingAnErrorOrPartialResult()
    {
        var port = new FakePort { BeginFailure = new OperationCanceledException() };
        using var controller = new SnapshotController(port, new AtomicSnapshotStorage(root), null);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); controller.Update(1, "Test", false, false, 1, Now);
        Assert.False(controller.Busy); Assert.Null(controller.Error); Assert.Null(controller.Publication.Current);
        port.BeginFailure = null; controller.RequestFull();
        await Pump(controller, 1, () => controller.Publication.Current != null, 2);
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public void PortWithoutBackgroundWorkProvidesAlreadyCompletedShutdown()
    {
        ISnapshotCapturePort port = new IdlePort();
        Assert.Same(Task.CompletedTask, port.ShutdownTask); Assert.Empty(port.TrustedSourceKeys);
    }

    [Fact]
    public async Task ReportIntakeRecoversFromSavedUnavailableHealthAfterDedicatedFileRemoval()
    {
        var port = new FakePort(); var storage = new AtomicSnapshotStorage(root);
        var saved = CollectionStateFactory.Empty(1, Guid.NewGuid(), Now);
        saved = saved with
        {
            ReportedEvidence = new([], new(DataType.ReportedOwnership, SourceStatus.Unavailable,
            Completeness.Unknown, ProvenanceType.None, CapabilityState.Supported, ReasonCode.CaptureFailed, "Previous failure", null, 0))
        };
        await storage.CommitAsync(1, CollectionDataSerializer.Compact(CollectionDataAssembler.Assemble(port.Catalog!, saved), 7), null, null, false, false, Now, default);
        using var controller = new SnapshotController(port, storage, null);
        await Pump(controller, 1, () => controller.Publication.Current is not null, 0);
        Assert.Equal(SourceStatus.Fresh, controller.Publication.Current!.Data.State.ReportedEvidence.Health.SourceStatus);
        var revision = controller.Publication.Current.Revision;
        Assert.Equal("Accepted", controller.Reports.TryEnqueue("1", 100, "Fixture", 1, Now.AddSeconds(1)));
        Assert.Equal("Accepted", controller.Reports.TryEnqueue("1", 100, "Another fixture", 1, Now.AddSeconds(1)));
        await Pump(controller, 1, () => controller.Publication.Current!.Revision > revision, 1);
        Assert.True(controller.Reports.Available); Assert.Equal(2, controller.Publication.Current!.Data.State.ReportedEvidence.Items.Length);
        Assert.Equal(2, CollectionStateReader.ReadReports(await storage.ReadAsync(1, true, default), 1).Length);
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task EmptyStartupPublishesTrueIntakeCapabilitiesWithoutInventingSnapshot()
    {
        using var controller = new SnapshotController(new FakePort(), new AtomicSnapshotStorage(root), null);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        Assert.Null(controller.Publication.Current);
        using var caps = JsonDocument.Parse(controller.Publication.GetCapabilities());
        Assert.True(caps.RootElement.GetProperty("reportedOwnershipEvidence").GetProperty("available").GetBoolean());
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task RestoredSupportedStateCannotBypassCurrentBuildValidationRegistry()
    {
        var fixture = SnapshotFixture.Create(); var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(123, CollectionDataSerializer.Compact(fixture, 8), null, null, false, false, Now, default);
        using var controller = new SnapshotController(new FakePort { Catalog = fixture.Catalog with { GameDataVersion = "new build" } }, storage, null);
        await Pump(controller, 123, () => controller.Publication.Current is not null, 0);
        var restored = controller.Publication.Current!.Data.State;
        Assert.All(restored.Unlocks.Values, source => { Assert.Empty(source.UnlockedIds); Assert.Equal(CapabilityState.UnavailablePendingValidation, source.Health.CapabilityState); });
        Assert.All(restored.CurrentItemPresence.Values, source => Assert.Empty(source.Items));
        controller.Dispose(); await controller.ShutdownTask;
    }

    [Fact]
    public async Task NoticesNameOnlyObservedSourcesAndWithdrawalSeesAlreadyClearedPublication()
    {
        using var controller = new SnapshotController(new FakePort(), new AtomicSnapshotStorage(root), null);
        var notices = new List<string>(); controller.Changed += notices.Add;
        var withdrawn = new List<(string Snapshot, bool Intake)>();
        controller.Withdrawn += _ => withdrawn.Add((controller.Publication.GetSnapshot(), controller.Reports.Available));
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); await Pump(controller, 1, () => controller.Publication.Current is not null, 1);
        await Pump(controller, 1, () => notices.Count == 1, 1); notices.Clear();
        controller.RequestPresence(); await Pump(controller, 1, () => notices.Count == 1, 2);
        using (var notice = JsonDocument.Parse(notices[0]))
        {
            Assert.Equal(new[] { "SourceHealth" }, notice.RootElement.GetProperty("changedDataTypes").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(CollectionReadOperation.PhysicalSources.Order(StringComparer.Ordinal), notice.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
        }
        notices.Clear(); Assert.Equal("Accepted", controller.Reports.TryEnqueue("1", 100, "Fixture", 3, Now.AddSeconds(3)));
        await Pump(controller, 1, () => notices.Count == 1, 3);
        using (var notice = JsonDocument.Parse(notices[0])) Assert.Equal(new[] { "reportedOwnership" }, notice.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
        controller.Update(0, "", true, false, 4, Now);
        Assert.Equal(("", false), Assert.Single(withdrawn));
        await Pump(controller, 1, () => controller.Reports.Available, 5);
        controller.Dispose(); await controller.ShutdownTask;
        Assert.Equal(("", false), withdrawn[^1]); Assert.Equal(2, withdrawn.Count);
    }

    [Fact]
    public async Task WorkerNormalizesRawIdsAndDiagnosticViewsDescribeRealOperationStates()
    {
        var measurements = new DiagnosticMeasurements(); var fixture = SnapshotFixture.Create();
        RawCaptureBatch? raw = null;
        var port = new FakePort
        {
            Catalog = fixture.Catalog,
            CatalogReady = false,
            AdvanceRemaining = 1,
            ReadFactory = (generation, at) =>
        {
            var state = CollectionStateFactory.Empty(1, Guid.NewGuid(), at, generation);
            raw = new(state.Unlocks.SetItem("mounts", new([1, 1], CaptureReadPolicy.Health(DataType.PermanentUnlock, generation, at, ReasonCode.None))), state.CurrentItemPresence, []);
            return raw;
        }
        };
        using var controller = new SnapshotController(port, new AtomicSnapshotStorage(root, measurements: measurements), null, measurements);
        controller.Update(1, "Test", false, false, 0, Now);
        Assert.Equal("Idle", controller.DiagnosticOperation(0).ControllerState); Assert.Null(controller.DiagnosticOperation(0).CaptureType);
        Assert.False(controller.CancellationRequested); Assert.Equal(4, controller.DiagnosticTasks.Length);
        port.CatalogReady = true; controller.Update(1, "Test", false, false, 0, Now);
        Assert.Contains(controller.DiagnosticOperation(0).ControllerState, new[] { "PreparingCapabilities", "Working" });
        Assert.Equal(4, controller.DiagnosticTasks.Length);
        await Pump(controller, 1, () => controller.Reports.Available, 0);
        controller.RequestFull(); controller.Update(1, "Test", false, false, 1, Now);
        Assert.Equal("Capturing", controller.DiagnosticOperation(2).ControllerState); Assert.Equal("Full", controller.DiagnosticOperation(2).CaptureType);
        Assert.Equal(1000, controller.DiagnosticOperation(2).ElapsedMilliseconds);
        controller.Update(1, "Test", false, false, 2, Now); controller.Update(1, "Test", false, false, 2, Now);
        Assert.Equal("Working", controller.DiagnosticOperation(2).ControllerState); Assert.Equal(4, controller.DiagnosticTasks.Length);
        await Pump(controller, 1, () => controller.Publication.Current is not null, 2);
        Assert.Equal(new uint[] { 1, 1 }, raw!.Unlocks["mounts"].UnlockedIds); Assert.Equal(new uint[] { 1 }, controller.Publication.Current!.Data.State.Unlocks["mounts"].UnlockedIds);
        Assert.Equal("Succeeded", controller.StorageDiagnostic.LastWrite.Status); Assert.Equal("Idle", controller.DiagnosticOperation(3).ControllerState);
        controller.RequestPresence(); controller.Update(1, "Test", false, false, 3, Now);
        Assert.Equal("Lightweight", controller.DiagnosticOperation(3).CaptureType);
        await Pump(controller, 1, () => controller.Publication.Current!.Data.State.CaptureType == CaptureType.Lightweight, 3);
        Assert.Equal("Lightweight", controller.DiagnosticOperation(3).CaptureType);
        Assert.NotEmpty(measurements.Snapshot()); controller.Dispose(); await controller.ShutdownTask;
        Assert.Equal("Disposed", controller.DiagnosticOperation(4).ControllerState);
    }

    private sealed class IdlePort : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog => null;
        public bool PrepareCatalog() => false;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now) { }
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult() => null;
        public void ResetSession() { }
        public void Dispose() { }
    }

    private static async Task Pump(SnapshotController controller, ulong id, Func<bool> completed, double monotonic)
    {
        for (int i = 0; i < 500; i++)
        {
            ControllerPumpStep.Advance(controller, () => controller.Update(id, "Test", false, false, monotonic, Now.AddSeconds(monotonic)));
            if (completed()) return;
            // These fixtures expect success, or complete explicitly on their injected failure.
            // Reject an unexpected completed failure before repeating the same fixed-time update.
            if (!controller.Busy) Assert.Null(controller.Error);
            Assert.True(controller.Busy, "A pending fixture expectation requires an owned capture or background operation.");
            await Task.Delay(2);
        }
        Assert.Fail("Controller did not reach expected state: " + controller.Error);
    }

    private sealed class FakePort : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; init; } = new("English", "2026.08.11.0000.0000",
            CollectionKeys.Unlocks.Concat(CollectionKeys.RichOnly).ToImmutableDictionary(key => key,
                key => new CollectionDefinition(key, key, key is "outfits" or "relics" ? DataType.Derived : key is "armoires" or "glamour_dresser" ? DataType.CurrentItemPresence : DataType.PermanentUnlock, true, [])), [100]);
        public int BeginCount; public int ResetCount; public bool Disposed; public bool Reject;
        public bool CatalogReady = true; public int AdvanceRemaining; public Exception? BeginFailure;
        public ImmutableHashSet<string> TrustedSourceKeys { get; init; } = [];
        public Func<long, DateTimeOffset, RawCaptureBatch>? ReadFactory;
        public Task ShutdownTask { get; init; } = Task.CompletedTask;
        private RawCaptureBatch? result;
        public bool PrepareCatalog() => CatalogReady;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            BeginCount++; if (BeginFailure is not null) throw BeginFailure; var state = CollectionStateFactory.Empty(contentId, Guid.NewGuid(), now, generation);
            result = ReadFactory?.Invoke(generation, now) ?? new(full ? state.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, state.CurrentItemPresence, []);
        }
        public bool Advance() => AdvanceRemaining-- <= 0;
        public RawCaptureBatch? TakeResult() => Reject ? null : result;
        public void ResetSession() { ResetCount++; result = null; }
        public void Dispose() { Disposed = true; ResetSession(); }
    }

    private sealed class FailEvidenceWrites : IAtomicFileOperations
    {
        private readonly AtomicFileOperations actual = new();
        public Stream OpenRead(string path) => actual.OpenRead(path);
        public void Replace(string source, string destination, string? backup) { Fail(destination); actual.Replace(source, destination, backup); }
        public void Move(string source, string destination) { Fail(destination); actual.Move(source, destination); }
        public void Delete(string path) => actual.Delete(path);
        private static void Fail(string path) { if (path.EndsWith("reported-evidence.json", StringComparison.Ordinal)) throw new IOException("Fixture persistence failure"); }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

