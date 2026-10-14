using System.Collections.Immutable;
using System.Text;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class OutputSizeTests : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "output-size-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicStorageAcceptsExactRequiredDocumentBoundariesAndRejectsOneAdditionalByte(bool richBoundary)
    {
        var data = SnapshotFixture.Create(); var storage = new AtomicSnapshotStorage(root);
        var compact = CollectionDataSerializer.Compact(data, 1);
        var rich = CollectionDataSerializer.Rich(data, "Test", data.State.CapturedAtUtc);
        if (richBoundary) rich += new string(' ', 16777216 - Encoding.UTF8.GetByteCount(rich));
        else compact += new string(' ', 4194304 - Encoding.UTF8.GetByteCount(compact));
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(compact, "123").Success);
        Assert.True(CollectionDataSemanticValidator.ValidateRich(rich).Success);
        var result = await storage.CommitAsync(123, compact, rich, null, true, true, data.State.CapturedAtUtc, default);
        Assert.Equal(1, result.HistoryCount);
        Assert.Equal(richBoundary ? 16777216 : 4194304,
            new FileInfo(richBoundary ? storage.CanonicalPath : storage.CharacterPath(123)).Length);
        Assert.Equal(compact, await storage.ReadAsync(123, false, default));
        Assert.Equal(rich, await File.ReadAllTextAsync(storage.CanonicalPath));
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        await Assert.ThrowsAsync<OutputSizeLimitException>(() => storage.CommitAsync(123,
            richBoundary ? compact : compact + " ", richBoundary ? rich + " " : rich,
            null, true, true, data.State.CapturedAtUtc.AddSeconds(1), default));
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order());
        foreach (var file in files) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiArrayDocumentOverflowCannotReplaceAnyAcceptedFile(bool richOverflow)
    {
        var storage = new AtomicSnapshotStorage(root);
        var data = SnapshotFixture.Create();
        var contract = new FFXIVCollectContract(data.Catalog.GameDataVersion,
            data.Catalog.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet()));
        var publisher = new CollectionDataPublisher(storage, contract);
        var accepted = await publisher.PrepareAndPersistAsync(data, "Test", 1, null, true, false, default);
        Assert.True(accepted.Export.CanCopy);
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var candidate = LargeData(richOverflow);
        Assert.All(candidate.State.Unlocks.Values, value => Assert.InRange(value.UnlockedIds.Length, 0, 50000));
        Assert.All(candidate.State.CurrentItemPresence.Values, value => Assert.InRange(value.Items.Length, 0, 50000));
        Assert.All(candidate.Collections.Values, value => Assert.InRange(value.Items.Length, 0, 50000));
        var compact = CollectionDataSerializer.Compact(candidate, 2);
        if (richOverflow)
        {
            Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(compact, "123").Success);
            Assert.True(Encoding.UTF8.GetByteCount(CollectionDataSerializer.Rich(candidate, "Test", candidate.State.CapturedAtUtc)) > 16777216);
        }
        else Assert.True(Encoding.UTF8.GetByteCount(compact) > 4194304);
        var exception = await Assert.ThrowsAsync<OutputSizeLimitException>(() =>
            publisher.PrepareAndPersistAsync(candidate, "Test", 2, accepted, true, false, default));
        Assert.Equal("OutputSizeLimitExceeded", exception.Message);
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order());
        foreach (var file in files) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
        Assert.Equal(1, accepted.Revision); Assert.Equal(1, accepted.HistoryCount); Assert.True(accepted.Export.CanCopy);
    }

    [Fact]
    public async Task ControllerReportsStableSizeCodeAndRetainsRevisionNoticeHistoryAndCopy()
    {
        var fixture = SnapshotFixture.Create();
        var port = new Port(fixture);
        var measurements = new DiagnosticMeasurements();
        var storage = new AtomicSnapshotStorage(root);
        var contract = new FFXIVCollectContract(fixture.Catalog.GameDataVersion,
            fixture.Catalog.Collections.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Items.Select(item => item.Id).ToImmutableHashSet()));
        using var controller = new SnapshotController(port, storage, contract, measurements);
        var notices = new List<string>(); controller.Changed += notices.Add;
        await Pump(controller, () => controller.Reports.Available, 0);
        controller.RequestFull(); await Pump(controller, () => controller.Publication.Current is not null && notices.Count == 1, 1);
        var accepted = controller.Publication.Current!; Assert.True(accepted.Export.CanCopy);
        var files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        port.Data = LargeData(false);
        controller.RequestFull(); await Pump(controller, () => controller.Error is not null, 2);
        Assert.StartsWith("OutputSizeLimitExceeded:", controller.Error);
        Assert.Contains(measurements.Failures, value => value.Component == "Controller" && value.Code == "OutputSizeLimitExceeded");
        Assert.Same(accepted, controller.Publication.Current); Assert.Single(notices);
        Assert.Equal(accepted.CompactJson, controller.Publication.GetSnapshot());
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order());
        foreach (var file in files) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
        controller.Dispose(); await controller.ShutdownTask;
    }

    private static CollectionData LargeData(bool rich)
    {
        var data = SnapshotFixture.Create();
        var state = data.State with { CapturedAtUtc = data.State.CapturedAtUtc.AddSeconds(1) };
        if (!rich)
        {
            var items = Enumerable.Range(1, 50000).Select(id => new Exportaramus.Features.CollectionData.State.PresenceItem((uint)id, uint.MaxValue)).ToImmutableArray();
            foreach (var key in new[] { "inventory", "armoryChest", "chocoboSaddlebag" })
                state = state with { CurrentItemPresence = state.CurrentItemPresence.SetItem(key, state.CurrentItemPresence[key] with { Items = items }) };
            return CollectionDataAssembler.Assemble(data.Catalog, state);
        }
        var catalog = data.Catalog;
        var ids = Enumerable.Range(1, 20000).Select(id => (uint)id).ToImmutableArray();
        var entries = ids.Select(id => new CatalogItem(id, id, new string('x', 256), false)).ToImmutableArray();
        foreach (var key in new[] { "mounts", "minions" })
        {
            catalog = catalog with { Collections = catalog.Collections.SetItem(key, catalog.Collections[key] with { Items = entries }) };
            state = state with { Unlocks = state.Unlocks.SetItem(key, state.Unlocks[key] with { UnlockedIds = ids }) };
        }
        return CollectionDataAssembler.Assemble(catalog, state);
    }

    private static async Task Pump(SnapshotController controller, Func<bool> ready, double monotonic)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            controller.Update(123, "Test", false, false, monotonic, SnapshotFixture.Now.AddSeconds(monotonic));
            if (ready()) return;
            await Task.Delay(2);
        }
        Assert.Fail("Controller did not reach expected state: " + controller.Error);
    }

    private sealed class Port(CollectionData initial) : ISnapshotCapturePort
    {
        public CollectionData Data = initial;
        public CollectionCatalog Catalog => Data.Catalog;
        private long generation;
        private DateTimeOffset capturedAt;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong contentId, long value, DateTimeOffset now) { generation = value; capturedAt = now; }
        public bool Advance() => true;
        public RawCaptureBatch TakeResult() => new(
            Data.State.Unlocks.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Health = pair.Value.Health with { CaptureGeneration = generation, ObservedAtUtc = capturedAt } }),
            Data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Health = pair.Value.Health with { CaptureGeneration = generation, ObservedAtUtc = capturedAt } }), Data.State.Quests);
        public void ResetSession() { }
        public void Dispose() { }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
