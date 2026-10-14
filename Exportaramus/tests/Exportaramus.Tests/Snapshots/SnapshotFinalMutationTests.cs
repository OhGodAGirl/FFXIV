using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class SnapshotFinalMutationTests
{
    [Fact]
    public async Task CanonicalChangeNoticeUsesV11AndIncludesObservedFacewear()
    {
        var directory = NewDirectory();
        var storage = new AtomicSnapshotStorage(directory);
        var controller = NewController(storage);
        var canonical = new List<string>();
        var legacy = new List<string>();
        controller.ChangedV11 += _ => throw new InvalidOperationException("Subscriber fixture failure");
        controller.ChangedV11 += canonical.Add;
        controller.Changed += legacy.Add;
        try
        {
            await Pump(controller, () => controller.Reports.Available, 0);
            controller.RequestFull();
            await Pump(controller, () => controller.Publication.Current is not null, 1);
            await Pump(controller, () => canonical.Count != 0, 1);

            var bundle = Assert.IsType<Exportaramus.Features.CollectionData.Publication.AcceptedSnapshotBundle>(controller.Publication.Current);
            using var notice = JsonDocument.Parse(Assert.Single(canonical));
            Assert.Equal("1.1", notice.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(bundle.Revision, notice.RootElement.GetProperty("revision").GetInt64());
            Assert.Contains("facewear", notice.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(bundle.CompactJsonV12, await storage.ReadAsync(123, false, default));

            using var legacyNotice = JsonDocument.Parse(Assert.Single(legacy));
            Assert.Equal("1.0", legacyNotice.RootElement.GetProperty("schemaVersion").GetString());
            Assert.DoesNotContain("facewear", legacyNotice.RootElement.GetProperty("changedSources").EnumerateArray().Select(value => value.GetString()));
        }
        finally { await Shutdown(controller, directory); }
    }

    [Theory]
    [InlineData("logout", "LoggedOut")]
    [InlineData("character", "CharacterChanged")]
    [InlineData("dispose", "ProviderStopping")]
    public async Task CanonicalWithdrawalRetainsSchemaAndDepartingIdentity(string transition, string reason)
    {
        var directory = NewDirectory();
        var controller = NewController(new(directory));
        var canonical = new List<string>();
        var legacy = new List<string>();
        controller.WithdrawnV11 += _ => throw new InvalidOperationException("Subscriber fixture failure");
        controller.WithdrawnV11 += canonical.Add;
        controller.Withdrawn += legacy.Add;
        try
        {
            await Pump(controller, () => controller.Reports.Available, 0);
            switch (transition)
            {
                case "logout": controller.Update(0, "", true, false, 1, SnapshotFixture.Now.AddSeconds(1)); break;
                case "character": controller.Update(456, "Another fixture", false, false, 1, SnapshotFixture.Now.AddSeconds(1)); break;
                case "dispose": controller.Dispose(); break;
                default: throw new InvalidOperationException("Unknown transition fixture");
            }

            using var notice = JsonDocument.Parse(Assert.Single(canonical));
            Assert.Equal("1.1", notice.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("SnapshotWithdrawn", notice.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("123", notice.RootElement.GetProperty("contentId").GetString());
            Assert.Equal(controller.Session, notice.RootElement.GetProperty("providerSessionId").GetGuid());
            Assert.Equal(reason, notice.RootElement.GetProperty("reasonCode").GetString());
            using var legacyNotice = JsonDocument.Parse(Assert.Single(legacy));
            Assert.Equal("1.0", legacyNotice.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(reason, legacyNotice.RootElement.GetProperty("reasonCode").GetString());
            Assert.Empty(controller.Publication.GetSnapshot());
        }
        finally { await Shutdown(controller, directory); }
    }

    [Fact]
    public async Task FirstCapabilityPreparationAlwaysYieldsBeforeRestoration()
    {
        var directory = NewDirectory();
        var controller = NewController(new(directory));
        try
        {
            controller.Update(123, "Fixture", false, false, 0, SnapshotFixture.Now);
            Assert.Equal("PreparingCapabilities", controller.DiagnosticOperation(0).ControllerState);
            Assert.Empty(controller.Publication.GetCapabilities());
            Assert.False(controller.Reports.Available);
            Assert.True(controller.Busy);
        }
        finally { await Shutdown(controller, directory); }
    }

    [Fact]
    public async Task FinalOwnedCancellationSourceClosesAfterPendingShutdown()
    {
        var directory = NewDirectory();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new SnapshotController(new Port { ShutdownTask = completion.Task }, new(directory), null);
        try
        {
            controller.Dispose();
            Assert.False(controller.ShutdownTask.IsCompleted);
            // Invalidate replaces the departing source. ObserveAsync owns this final source
            // until dependency shutdown settles, including any lazily allocated handle.
            var source = Assert.IsType<CancellationTokenSource>(typeof(SnapshotController)
                .GetField("scope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller));
            var handle = source.Token.WaitHandle.SafeWaitHandle;
            Assert.False(handle.IsClosed);
            completion.SetResult();
            await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(handle.IsClosed);
            Assert.Throws<ObjectDisposedException>(() => { _ = source.Token; });
        }
        finally
        {
            completion.TrySetResult();
            await Shutdown(controller, directory);
        }
    }

    private static string NewDirectory() => Path.Combine(AppContext.BaseDirectory, "snapshot-final", Guid.NewGuid().ToString("N"));

    private static SnapshotController NewController(AtomicSnapshotStorage storage) =>
        new(new Port(), storage, null, prepareCapabilities: work => Task.FromResult(work()));

    private static async Task Pump(SnapshotController controller, Func<bool> completed, double monotonic)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            controller.Update(123, "Fixture", false, false, monotonic, SnapshotFixture.Now.AddSeconds(monotonic));
            if (completed()) return;
            await Task.Delay(2);
        }
        Assert.Fail("Controller did not reach the expected state: " + controller.Error);
    }

    private static async Task Shutdown(SnapshotController controller, string directory)
    {
        controller.Dispose();
        await controller.ShutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class Port : ISnapshotCapturePort
    {
        private RawCaptureBatch? result;
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public Task ShutdownTask { get; init; } = Task.CompletedTask;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
        {
            var state = CollectionStateFactory.Empty(contentId, Guid.NewGuid(), now, generation);
            result = new(full ? state.Unlocks : ImmutableDictionary<string, PermanentUnlockState>.Empty, state.CurrentItemPresence, []);
        }
        public bool Advance() => true;
        public RawCaptureBatch? TakeResult() => result;
        public void ResetSession() => result = null;
        public void Dispose() => ResetSession();
    }
}
