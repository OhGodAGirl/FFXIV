using Exportaramus.Features.Diagnostics;
using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Features.CollectionData.Capture;

public sealed class SnapshotCapturePort : ISnapshotCapturePort
{
    private readonly Func<Exportaramus.Features.Inventory.InventorySnapshot?>? inventorySnapshot;
    private readonly IDataManager data;
    private readonly DiagnosticMeasurements measurements;
    private readonly CollectionReader reader;
    private readonly StorageReadinessObserver readiness;
    private readonly CatalogPreparation preparation;
    private CollectionReadOperation? operation;
    private bool disposed;

    public SnapshotCapturePort(IDataManager data, IUnlockState unlocks, IPlayerState player,
        IFramework framework, IGameGui gameGui, DerivedCatalogInputs inputs, DiagnosticMeasurements? measurements = null, Func<Exportaramus.Features.Inventory.InventorySnapshot?>? inventorySnapshot = null)
    {
        this.inventorySnapshot = inventorySnapshot;
        this.data = data; this.measurements = measurements ?? new();
        preparation = new(() => new CollectionCatalogBuilder(data, inputs, this.measurements), (projection, token) => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var result = this.measurements.Measure("CatalogBuild", () => CollectionCatalogBuilderPolicy.Build(projection));
            token.ThrowIfCancellationRequested();
            return result;
        }, token));
        reader = new CollectionReader(data, unlocks, player, framework, gameGui);
        readiness = new StorageReadinessObserver(reader, data, gameGui, unlocks);
    }

    public CollectionCatalog? Catalog => preparation.Catalog;
    public bool SupportsTargetedCapture => true;
    public Task ShutdownTask => preparation.ShutdownTask;
    public ImmutableHashSet<string> TrustedSourceKeys => Catalog is { } catalog ? reader.TrustedSources(catalog.GameDataVersion) : [];
    public DiagnosticTask CatalogTask => new("Catalog", DiagnosticTaskPolicy.State(preparation.WorkerStatus));
    public bool PrepareCatalog() => preparation.Prepare(data.Language.ToString(),
        data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version);

    public void Begin(bool full, ulong contentId, long generation, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        operation?.Dispose();
        reader.BeginCapture();
        if (inventorySnapshot is not null) reader.SetRetainerInventory(inventorySnapshot());
        operation = new(reader, Catalog ?? throw new InvalidOperationException("Catalog is not ready."), full, contentId, generation, now, measurements);
    }

    public bool Advance() => operation?.Advance() ?? true;
    public void BeginTargeted(ImmutableArray<string> sources, ulong contentId, long generation, DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        operation?.Dispose();
        reader.BeginCapture();
        if (inventorySnapshot is not null) reader.SetRetainerInventory(inventorySnapshot());
        operation = new(reader, Catalog ?? throw new InvalidOperationException("Catalog is not ready."), false, contentId, generation, now, measurements, sources);
    }
    public RawCaptureBatch? TakeResult()
    {
        var result = operation?.Result; operation?.Dispose(); operation = null; return result;
    }
    public bool ObserveReadySources(ulong contentId, double monotonicNow, bool guidedOwnsEdges) =>
        !disposed && readiness.Poll(contentId, monotonicNow, guidedOwnsEdges);
    public bool ObserveReadyUnlocks(ulong contentId, double now) => !disposed && readiness.PollUnlocks(contentId, now);
    public bool ObserveReadyBeasts(ulong contentId, double now) => !disposed && readiness.PollBeasts(contentId, now);
    public void ResetSession() { operation?.Dispose(); operation = null; reader.ResetSession(); readiness.Reset(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        ResetSession(); preparation.Dispose();
    }
}
