using System.Globalization;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper;

/// <summary>Owns the pinned IPC client and publishes prepared ownership independently of catalog I/O.</summary>
internal sealed class ExportaramusConsumer : IDisposable
{
    private readonly CollectionDataIpcClient client;
    private readonly object gate = new();
    private CollectionDataOwnershipState state = CollectionDataOwnershipPolicy.NotChecked;
    private CollectionDataOwnershipState? delivered;
    private CollectionDataClientView? acceptedView;
    private Task completion = Task.CompletedTask;
    private long invalidation;
    private long publishedInvalidation;
    private bool disposed;

    internal Task Disposal { get; private set; } = Task.CompletedTask;

    internal ExportaramusConsumer(Func<Action<string>, Action<string>, ICollectionDataTransport> bind)
    {
        client = new CollectionDataIpcClient((changed, withdrawn) => bind(payload => OnChanged(payload, changed), payload =>
        {
            Interlocked.Increment(ref invalidation);
            withdrawn(payload);
        }));
    }
    internal Exception? DisposalFailure { get; private set; }
    internal Task Completion { get { lock (gate) return completion; } }
    internal CollectionDataOwnershipState State
    {
        get
        {
            lock (gate)
                return publishedInvalidation == Interlocked.Read(ref invalidation)
                    ? state
                    : CollectionDataOwnershipPolicy.NotChecked;
        }
    }

    internal void Tick(ulong contentId, DateTimeOffset now)
    {
        lock (gate)
        {
            if (disposed || !completion.IsCompleted)
                return;
            completion = PollAsync(contentId, now, Interlocked.Read(ref invalidation));
        }
    }

    internal bool TryTakeRefreshRequest()
    {
        lock (gate)
        {
            var current = State;
            if (ReferenceEquals(delivered, current))
                return false;
            delivered = current;
            return true;
        }
    }

    internal void ClearCharacter()
    {
        lock (gate)
        {
            if (disposed)
                return;
            publishedInvalidation = Interlocked.Increment(ref invalidation);
            state = CollectionDataOwnershipPolicy.NotChecked;
            acceptedView = null;
            // Tick changes the client's identity synchronously, even while its old worker completes.
            completion = Task.WhenAll(completion, client.TickAsync(null, DateTimeOffset.MinValue));
        }
    }

    internal string ReportConfirmedAcquisition(ulong contentId, uint itemId)
        => client.ReportConfirmedAcquisition(contentId.ToString(CultureInfo.InvariantCulture), itemId, PluginIdentity.InternalName);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            publishedInvalidation = Interlocked.Increment(ref invalidation);
            state = CollectionDataOwnershipPolicy.NotChecked;
            Disposal = DisposeCoreAsync();
        }
    }

    private async Task PollAsync(ulong contentId, DateTimeOffset now, long scope)
    {
        await client.TickAsync(contentId == 0 ? null : contentId.ToString(CultureInfo.InvariantCulture), now).ConfigureAwait(false);
        var view = client.View;
        lock (gate)
        {
            if (disposed || scope != Interlocked.Read(ref invalidation))
                return;
            if (publishedInvalidation == scope && acceptedView is not null
                && acceptedView.Availability == view.Availability
                && acceptedView.ProviderSessionId == view.ProviderSessionId
                && acceptedView.ContentId == view.ContentId
                && acceptedView.Revision == view.Revision
                && acceptedView.Failure == view.Failure)
                return;
            state = CollectionDataOwnershipPolicy.Build(view, contentId);
            acceptedView = view;
            publishedInvalidation = scope;
        }
    }

    private void OnChanged(string payload, Action<string> notifyClient)
    {
        var snapshot = Volatile.Read(ref state).Snapshot;
        if (snapshot is not null)
        {
            var notice = CollectionDataSemanticValidator.ValidateChanged(payload);
            var change = notice.Value!;
            if (notice.Success
                && change.ContentId == snapshot.ContentId
                && (change.ProviderSessionId != snapshot.ProviderSessionId || change.Revision > snapshot.Revision)
                && change.ChangedSources.Any(IsRelevantSource))
                Interlocked.Increment(ref invalidation);
        }
        notifyClient(payload);
    }

    private static bool IsRelevantSource(string source)
        => CollectionDataOwnershipPolicy.PhysicalSources.Contains(source)
            || source is "minions" or "mounts" or "orchestrions" or "hairstyles" or "facewear" or "fashions" or "reportedOwnership";

    private async Task DisposeCoreAsync()
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DisposalFailure = exception;
        }
    }

}
