using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class RestorationClockBoundaryTests
{
    [Fact]
    public async Task UnchangedTargetedCompletionClearsRestorationFailure()
    {
        var fixture = SnapshotFixture.Create();
        var path = Path.Combine(AppContext.BaseDirectory, "restore-targeted", Guid.NewGuid().ToString("N"));
        var store = new AtomicSnapshotStorage(path);
        await store.CommitAsync(123, CollectionDataSerializer.CompactV12(fixture, 7), null, null, false, false, SnapshotFixture.Now, default);
        var port = new Port();
        using var controller = new SnapshotController(port, store, null);
        var now = SnapshotFixture.Now.AddHours(-1);
        try
        {
            await Pump(controller, now, 0, () => controller.Reports.Available && !controller.Busy);
            Assert.Contains("could not be restored", Assert.IsType<string>(controller.Error));
            var previous = new CollectionDataPublisher(store, null).Prepare(fixture, "Fixture", 8, null, false, false, default).Bundle;
            controller.Publication.Accept(previous);
            controller.HintSource("orchestrions", 123, 0);
            controller.Update(123, "Fixture", false, false, double.NaN, now);
            await Pump(controller, now, 0, () => !controller.Busy);
            Assert.Equal(1, port.TargetedBeginCount);
            Assert.Same(previous, controller.Publication.Current);
            Assert.Null(controller.Error);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Theory]
    [InlineData("1.0", false)]
    [InlineData("1.1", false)]
    [InlineData("1.2", false)]
    [InlineData("1.0", true)]
    [InlineData("1.1", true)]
    [InlineData("1.2", true)]
    public async Task RestoredPublicationsMustValidateWithoutChangingTheirObservationTimes(string version, bool clockMovedBack)
    {
        // Arrange: save a snapshot valid at its capture time.
        var fixture = SnapshotFixture.Create();
        var saved = version switch
        {
            "1.0" => CollectionDataSerializer.Compact(fixture, 7),
            "1.1" => CollectionDataSerializer.CompactV11(fixture, 7),
            _ => CollectionDataSerializer.CompactV12(fixture, 7),
        };
        var directory = Path.Combine(AppContext.BaseDirectory, "restore-clock-boundary", Guid.NewGuid().ToString("N"));
        var storage = new AtomicSnapshotStorage(directory);
        await storage.CommitAsync(123, saved, null, null, false, false, SnapshotFixture.Now, default);
        var port = new Port();
        var measurements = new DiagnosticMeasurements();
        using var controller = new SnapshotController(port, storage, null, measurements);
        var now = SnapshotFixture.Now.AddHours(clockMovedBack ? -1 : 1);
        try
        {
            // Act: restore with a clock before or after the saved observation.
            await Pump(controller, now, 0, () => controller.Reports.Available && !controller.Busy);

            // Assert: reject invalid timestamps and preserve the observation time.
            Assert.Equal(saved, await storage.ReadAsync(123, false, default));
            if (clockMovedBack)
            {
                Assert.True(controller.Publication.Current is null, "A future-dated saved snapshot must not become an accepted publication.");
                Assert.Empty(controller.Publication.GetSnapshot());
                Assert.Empty(controller.Publication.GetSnapshotV11());
                Assert.Empty(controller.Publication.GetSnapshotV12());
                Assert.Contains("Sync Now", Assert.IsType<string>(controller.Error), StringComparison.Ordinal);
                Assert.Contains(measurements.Failures, failure => failure.Component == "Storage" && failure.Operation == "Restore" && failure.Code == "StoredSnapshotInvalid" && failure.ExceptionType == nameof(InvalidDataException));
                controller.RequestFull();
                await Pump(controller, now.AddSeconds(1), 1, () => controller.Publication.Current is not null && !controller.Busy);
                Assert.Equal(1, port.BeginCount);
                Assert.Equal(CaptureType.Full, controller.Publication.Current!.Data.State.CaptureType);
                Assert.Null(controller.Error);
            }
            else
            {
                Assert.Equal(CaptureType.Restored, controller.Publication.Current!.Data.State.CaptureType);
                Assert.Equal(SnapshotFixture.Now, controller.Publication.Current.Data.State.Unlocks["mounts"].Health.ObservedAtUtc);
                Assert.Equal(SourceStatus.Stale, controller.Publication.Current.Data.State.Unlocks["mounts"].Health.SourceStatus);
                Assert.Equal(0, port.BeginCount);
            }
            Assert.True(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateSnapshot(controller.Publication.GetSnapshot(), "123").Success);
            Assert.True(Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateSnapshot(controller.Publication.GetSnapshotV11(), "123").Success);
            Assert.True(Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateSnapshot(controller.Publication.GetSnapshotV12(), "123").Success);
        }
        finally
        {
            controller.Dispose();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task Pump(SnapshotController controller, DateTimeOffset now, double monotonic, Func<bool> complete)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        do
        {
            timeout.Token.ThrowIfCancellationRequested();
            controller.Update(123, "Fixture", false, false, monotonic, now);
            await Task.Delay(1, timeout.Token);
        } while (!complete());
    }

    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog Catalog { get; } = SnapshotFixture.Create().Catalog;
        public ImmutableHashSet<string> TrustedSourceKeys { get; } = CollectionKeys.Unlocks.Concat(CollectionKeys.Presence).ToImmutableHashSet();
        public int BeginCount
        {
            get; private set;
        }
        private RawCaptureBatch? result;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            BeginCount++;
            var state = SnapshotFixture.Create().State;
            result = new(state.Unlocks.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
            {
                Health = CaptureReadPolicy.Health(DataType.PermanentUnlock, generation, now, ReasonCode.None)
            }),
                state.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
                {
                    Health = CaptureReadPolicy.Health(DataType.CurrentItemPresence, generation, now, ReasonCode.None)
                }), state.Quests);
        }
        public bool SupportsTargetedCapture => true;
        public int TargetedBeginCount
        {
            get; private set;
        }
        public void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now)
        {
            TargetedBeginCount++;
            result = new(ImmutableDictionary<string, PermanentUnlockState>.Empty, ImmutableDictionary<string, CurrentItemPresenceState>.Empty, []);
        }
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose()
        {
        }
    }
}
