using System.Security.Cryptography;
using System.Text;

namespace Exportaramus.ConsumerKit.V1_2;

public interface ICollectionDataTransport : IDisposable
{
    string ReadCapabilities();
    string ReadSnapshot();
    string Report(string contentId, uint itemId, string reporter);
}

/// <summary>Call TickAsync outside draw/events on a bounded discovery cadence. Notices only enqueue bounded hints.</summary>
public sealed class CollectionDataIpcClient : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Func<Action<string>, Action<string>, ICollectionDataTransport> factory;
    private readonly Func<Action, CancellationToken, Task> startWorker;
    private readonly CancellationTokenSource lifetime = new();
    private ICollectionDataTransport? transport;
    private Task? work;
    private readonly Queue<string> withdrawals = new();
    private bool withdrawalOverflow;
    private bool pending = true;
    private bool disposed;
    private long generation;
    private string? expectedContentId;
    private string? acceptedHash;
    private string? acceptedSession;
    private string? acceptedContentId;
    private long acceptedRevision;
    private DateTimeOffset nextDiscovery;
    private CollectionDataClientView view = CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.ProviderUnavailable);
    public CollectionDataClientView View
    {
        get
        {
            lock (sync)
                return view;
        }
    }

    public CollectionDataIpcClient(Func<Action<string>, Action<string>, ICollectionDataTransport> factory, Func<Action, CancellationToken, Task>? startWorker = null)
    {
        this.factory = factory;
        this.startWorker = startWorker ?? ((action, token) => Task.Run(action, token));
        try
        {
            transport = factory(Changed, Withdrawn);
        }
        catch { view = CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.ProviderUnavailable); }
    }

    public void Changed(string payload)
    {
        if (!Bounded(payload))
            return;
        lock (sync)
        {
            if (!disposed)
                pending = true;
        }
    }
    public void Withdrawn(string payload)
    {
        if (!Bounded(payload))
            return;
        lock (sync)
        {
            if (disposed)
                return;
            var queuedWithdrawals = withdrawals.Count;
            if (queuedWithdrawals == 16)
                withdrawalOverflow = true;
            else
                withdrawals.Enqueue(payload);
        }
    }

    public Task TickAsync(string? activeContentId, DateTimeOffset now)
    {
        lock (sync)
        {
            if (disposed)
                return Task.CompletedTask;
            if (expectedContentId != activeContentId)
            {
                expectedContentId = activeContentId;
                generation++;
                pending = true;
                ClearOrdering();
                view = CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload);
            }
            if (work is { IsCompleted: false })
                return work;
            ProcessWithdrawal();
            if (!pending && now < nextDiscovery)
                return Task.CompletedTask;
            pending = false;
            nextDiscovery = now.AddSeconds(5);
            var scope = generation;
            var identity = expectedContentId;
            work = startWorker(() => Refresh(scope, identity), lifetime.Token);
            return work;
        }
    }

    public string ReportConfirmedAcquisition(string contentId, uint itemId, string reporter)
    {
        lock (sync)
        {
            if (disposed)
                return "ProviderStopping";
            if (transport is null || contentId != expectedContentId || view.Availability != CollectionDataAvailability.Ready)
                return "Unavailable";
            try
            {
                var result = transport.Report(contentId, itemId, reporter);
                return CollectionDataSemanticValidator.ReportCodes.Contains(result) ? result : "Unavailable";
            }
            catch { return "Unavailable"; }
        }
    }

    private void Refresh(long scope, string? identity)
    {
        try
        {
            ICollectionDataTransport binding;
            lock (sync)
            {
                if (disposed || scope != generation)
                    return;
                transport ??= factory(Changed, Withdrawn);
                binding = transport;
                view = view with
                {
                    Availability = CollectionDataAvailability.Refreshing
                };
            }
            var capabilities = CollectionDataSemanticValidator.ValidateCapabilities(binding.ReadCapabilities());
            if (!capabilities.Success)
            {
                Reject(scope, capabilities.Code);
                return;
            }
            var payload = binding.ReadSnapshot();
            if (string.IsNullOrEmpty(payload) || identity is null)
            {
                Reject(scope, CollectionDataValidationCode.NoPayload);
                return;
            }
            var snapshot = CollectionDataSemanticValidator.ValidateSnapshot(payload, identity);
            if (!snapshot.Success)
            {
                Reject(scope, snapshot.Code);
                return;
            }
            var value = snapshot.Value!;
            if (value.ProviderSessionId != capabilities.Value!.ProviderSessionId)
            {
                Reject(scope, CollectionDataValidationCode.WrongProviderSession);
                return;
            }
            var hash = Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(payload)));
            lock (sync)
            {
                if (disposed || scope != generation)
                    return;
                ProcessWithdrawal(value.ProviderSessionId, value.ContentId);
                if (scope != generation)
                    return;
                if (acceptedSession == value.ProviderSessionId && acceptedContentId == value.ContentId)
                {
                    if (value.Revision < acceptedRevision)
                    {
                        Reject(scope, CollectionDataValidationCode.LowerRevision);
                        return;
                    }
                    if (value.Revision == acceptedRevision && hash != acceptedHash)
                    {
                        Reject(scope, CollectionDataValidationCode.EqualRevisionConflict);
                        return;
                    }
                }
                acceptedHash = hash;
                acceptedSession = value.ProviderSessionId;
                acceptedContentId = value.ContentId;
                acceptedRevision = value.Revision;
                view = new(CollectionDataAvailability.Ready, value, value.ProviderSessionId, value.ContentId, value.Revision, CollectionDataValidationCode.None);
            }
        }
        catch { Reject(scope, CollectionDataValidationCode.ProviderUnavailable); }
    }

    private void ProcessWithdrawal(string? incomingSession = null, string? incomingContentId = null)
    {
        var invalidate = withdrawalOverflow;
        withdrawalOverflow = false;
        var count = withdrawals.Count;
        for (var i = 0; i < count; i++)
        {
            var payload = withdrawals.Dequeue();
            var result = CollectionDataSemanticValidator.ValidateWithdrawn(payload);
            if (result.Success)
            {
                var notice = result.Value!;
                if ((notice.ProviderSessionId == view.ProviderSessionId && notice.ContentId == view.ContentId) ||
                    (notice.ProviderSessionId == incomingSession && notice.ContentId == incomingContentId))
                    invalidate = true;
            }
        }
        if (invalidate)
        {
            generation++;
            ClearOrdering();
            pending = true;
            view = CollectionDataClientView.Empty(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.NoPayload);
        }
    }

    private void Reject(long scope, CollectionDataValidationCode code)
    {
        lock (sync)
        {
            if (disposed || scope != generation)
                return;
            view = CollectionDataClientView.Empty(code is CollectionDataValidationCode.NoPayload or CollectionDataValidationCode.ProviderUnavailable ? CollectionDataAvailability.Unavailable : CollectionDataAvailability.Rejected, code);
            if (code == CollectionDataValidationCode.ProviderUnavailable)
            {
                try
                {
                    transport?.Dispose();
                }
                catch { }
                transport = null;
            }
        }
    }

    private void ClearOrdering()
    {
        acceptedHash = null;
        acceptedSession = null;
        acceptedContentId = null;
        acceptedRevision = 0;
    }

    private static bool Bounded(string? payload)
    {
        try
        {
            return payload is not null && payload.Length <= 4096 && new UTF8Encoding(false, true).GetByteCount(payload) <= 4096;
        }
        catch (EncoderFallbackException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        Task? owned;
        ICollectionDataTransport? binding;
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            generation++;
            pending = false;
            withdrawals.Clear();
            withdrawalOverflow = false;
            owned = work;
            binding = transport;
            transport = null;
            view = CollectionDataClientView.Empty(CollectionDataAvailability.Disposed, CollectionDataValidationCode.Disposed);
        }
        try
        {
            try
            {
                lifetime.Cancel();
            }
            finally
            {
                if (owned is not null)
                {
                    try
                    {
                        await owned.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }
        finally
        {
            try
            {
                binding?.Dispose();
            }
            finally { lifetime.Dispose(); }
        }
    }
}
