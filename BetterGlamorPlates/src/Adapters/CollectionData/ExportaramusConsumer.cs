using System.Globalization;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;
using Kit = Exportaramus.ConsumerKit.V1_2;

namespace BetterGlamorPlates.Adapters.CollectionData;

/// <summary>Owns the pinned client, bounded discovery and immediate invalidation of projected data.</summary>
internal sealed class ExportaramusConsumer : IDisposable
{
    private readonly IExportaramusClientPort client;
    private readonly Action<Exception> reportError;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly object gate = new();
    private CollectionDataSnapshot? latest;
    private long generation;
    private int pending = 1;
    private DateTimeOffset nextPoll;
    private bool disposed;
    private Task identityWork = Task.CompletedTask;
    internal Task Disposal { get; private set; } = Task.CompletedTask;

    internal ExportaramusConsumer(Func<Action<string>, Action<string>, Kit.ICollectionDataTransport> bind,
        Action<Exception> reportError, Func<DateTimeOffset>? utcNow = null,
        Func<Action, CancellationToken, Task>? startWorker = null)
    {
        this.reportError = reportError;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        client = new PinnedExportaramusClient(new Kit.CollectionDataIpcClient((changed, withdrawn) => bind(
            payload => { Invalidate(); changed(payload); },
            payload => { Invalidate(); withdrawn(payload); }), startWorker));
    }

    internal ExportaramusConsumer(IExportaramusClientPort client, Action<Exception> reportError, Func<DateTimeOffset> utcNow)
    {
        this.client = client;
        this.reportError = reportError;
        this.utcNow = utcNow;
    }

    internal CollectionDataSnapshot? Latest { get { lock (gate) return latest; } }

    internal bool TryTakeRefreshRequest()
    {
        lock (gate)
        {
            if (disposed || (pending == 0 && utcNow() < nextPoll))
                return false;
            pending = 0;
            nextPoll = utcNow().AddSeconds(5);
            return true;
        }
    }

    internal void RequestRefresh()
    {
        lock (gate)
        {
            if (disposed) return;
            pending = 1;
        }
        client.Changed("{}");
    }

    internal async Task<CollectionDataParseResult> RefreshAsync(ulong contentId, DateTimeOffset now)
    {
        long scope;
        lock (gate)
        {
            if (disposed) return CollectionDataParseResult.Reject(CollectionDataFailure.Disposed);
            scope = generation;
        }
        // The caller owns a background task; the kit itself coalesces and observes its worker.
        await client.TickAsync(contentId.ToString(CultureInfo.InvariantCulture), now).ConfigureAwait(false);
        var result = ExportaramusProjection.Project(client.View, contentId);
        lock (gate)
        {
            if (disposed || scope != generation)
                return CollectionDataParseResult.Reject(CollectionDataFailure.StaleCompletion);
            latest = result.Snapshot;
        }
        return result;
    }

    internal void ClearCharacter()
    {
        lock (gate) { if (disposed) return; }
        Invalidate();
        // Identity invalidation in TickAsync happens synchronously before its worker is returned.
        identityWork = Observe(Task.WhenAll(identityWork, client.TickAsync(null, utcNow())));
    }

    private void Invalidate()
    {
        lock (gate)
        {
            if (disposed) return;
            generation++;
            latest = null;
            pending = 1;
        }
    }

    private async Task Observe(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception) { reportError(exception); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            latest = null;
            generation++;
        }
        Disposal = DisposeCoreAsync();
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await identityWork.ConfigureAwait(false);
        }
        catch (Exception exception) { reportError(exception); }
    }
}




/// <summary>Owned client boundary; the production implementation delegates to the pinned kit.</summary>
internal interface IExportaramusClientPort : IAsyncDisposable
{
    Kit.CollectionDataClientView View { get; }
    void Changed(string payload);
    Task TickAsync(string? contentId, DateTimeOffset now);
}

internal sealed class PinnedExportaramusClient(Kit.CollectionDataIpcClient client) : IExportaramusClientPort
{
    public Kit.CollectionDataClientView View => client.View;
    public void Changed(string payload) => client.Changed(payload);
    public Task TickAsync(string? contentId, DateTimeOffset now) => client.TickAsync(contentId, now);
    public ValueTask DisposeAsync() => client.DisposeAsync();
}
