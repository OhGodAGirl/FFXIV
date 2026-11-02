using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

/// <summary>Accepts complete location reads and returns immutable snapshots.</summary>
public sealed class InventoryService(IInventoryCache? cache = null, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<ulong, InventorySnapshot> pendingWrites = [];
    private InventorySnapshot? snapshot;
    private Task<InventorySnapshot?>? worker;
    private InventorySnapshot? workerWrite;
    private ulong workerCharacter;
    private long workerGeneration;
    private long generation;
    private long revision;
    private bool restorePending;
    private bool disposed;
    private bool pumping;
    private Task? disposal;
    private string? lastCacheFailure;
    private DateTimeOffset retryAt;
    private DateTimeOffset restoreRetryAt;
    private bool restoreFailed;
    private bool writeFailed;

    public string? LastCacheFailure
    {
        get
        {
            lock (sync)
                return restoreFailed || writeFailed ? "InventoryCacheUnavailable" : lastCacheFailure;
        }
    }
    public InventorySnapshot? GetSnapshot() => Volatile.Read(ref snapshot);

    public void SetCharacter(ulong contentId)
    {
        lock (sync)
        {
            if (disposed || (snapshot?.ContentId ?? 0) == contentId) return;
            generation++;
            Volatile.Write(ref snapshot, contentId == 0 ? null : InventoryPolicy.Empty(contentId, ++revision));
            restorePending = contentId != 0 && cache is not null;
            restoreFailed = false;
            restoreRetryAt = default;
        }
    }

    public bool ObserveRoster(ulong contentId, IEnumerable<InventoryRetainer> roster, DateTimeOffset? observedAt = null)
    {
        lock (sync)
        {
            if (!IsCurrent(contentId)) return false;
            var observedGeneration = generation;
            var rosterObservedAt = observedAt ?? clock.GetUtcNow();
            if (!IsCurrent(contentId) || generation != observedGeneration) return false;
            if (rosterObservedAt == default || snapshot!.RetainerRosterObservedAtUtc > rosterObservedAt) return false;
            var retainers = new Dictionary<ulong, InventoryRetainer>();
            foreach (var retainer in roster)
            {
                if (retainer is null || retainer.RetainerId == 0 || !InventoryPolicy.ValidName(retainer.Name)
                    || retainers.Count == InventoryPolicy.MaximumRetainers || !retainers.TryAdd(retainer.RetainerId, retainer))
                    return false;
            }
            var locations = snapshot!.Locations.Where(location => location.Key.Kind != InventoryLocationKind.Retainer).ToList();
            foreach (var retainer in retainers.Values)
            {
                var key = new InventoryLocationKey(InventoryLocationKind.Retainer, retainer.RetainerId);
                var old = snapshot.Locations.FirstOrDefault(location => location.Key == key);
                locations.Add(old is null ? InventoryPolicy.Unknown(key, retainer.Name) : old with
                {
                    Name = retainer.Name
                });
            }
            var ordered = InventoryPolicy.Order(locations);
            if (snapshot.RetainerRosterKnown && snapshot.Locations.SequenceEqual(ordered)) return false;
            Publish(snapshot with
            {
                RetainerRosterKnown = true,
                RetainerRosterObservedAtUtc = rosterObservedAt.ToUniversalTime(),
                Locations = ordered
            });
            return true;
        }
    }

    public bool AcceptLocation(ulong contentId, InventoryLocationKey key, string name, IEnumerable<InventoryItemQuantity> items, DateTimeOffset observedAt)
    {
        lock (sync)
        {
            if (!CanAccept(contentId, key, name) || observedAt == default) return false;
            // CanAccept guarantees that this location belongs to the current character.
            var old = snapshot!.Locations.First(location => location.Key == key);
            if (old.ObservedAtUtc > observedAt || old.AttemptedAtUtc > observedAt) return false;
            var quantified = key.Kind is not (InventoryLocationKind.Armoire or InventoryLocationKind.GlamourDresser);
            if (!InventoryPolicy.TryNormalize(items, out var normalized, quantified))
                return MarkUnavailable(contentId, key, name, observedAt, true);
            if (old is { State: InventoryObservationState.Observed } && old.Name == name
                && old.Items.SequenceEqual(normalized) && observedAt - old.ObservedAtUtc!.Value < TimeSpan.FromMinutes(1))
                return false;
            Replace(new(key, name, InventoryObservationState.Observed, observedAt.ToUniversalTime(), observedAt.ToUniversalTime(), normalized));
            return true;
        }
    }

    public bool MarkUnavailable(ulong contentId, InventoryLocationKey key, string name, DateTimeOffset attemptedAt, bool readFailed)
    {
        lock (sync)
        {
            if (!CanAccept(contentId, key, name) || attemptedAt == default) return false;
            // CanAccept guarantees that the location exists.
            var old = snapshot!.Locations.First(location => location.Key == key);
            if (old.AttemptedAtUtc > attemptedAt) return false;
            var state = readFailed ? InventoryObservationState.ReadFailed : old.HasObservation ? InventoryObservationState.Retained : InventoryObservationState.Unknown;
            if (old.State == state && old.Name == name) return false;
            Replace(old with
            {
                Name = name,
                State = state,
                AttemptedAtUtc = attemptedAt.ToUniversalTime()
            });
            return true;
        }
    }

    /// <summary>Collect finished work and start at most one cache operation. Do not wait for I/O.</summary>
    public void Pump()
    {
        lock (sync)
        {
            if (pumping) return;
            pumping = true;
            try
            {
                if (disposed || cache is null || worker is { IsCompleted: false })
                    return;
                if (worker is not null)
                {
                    try
                    {
                        var saved = worker.GetAwaiter().GetResult();
                        if (saved is not null && workerGeneration == generation && IsCurrent(workerCharacter))
                        {
                            var restored = InventoryPolicy.Restore(snapshot!, saved);
                            Publish(restored);
                        }
                        if (workerWrite is not null && pendingWrites.TryGetValue(workerWrite.ContentId, out var pending)
                            && ReferenceEquals(pending, workerWrite))
                            pendingWrites.Remove(workerWrite.ContentId);
                        if (workerWrite is not null)
                        {
                            writeFailed = false;
                            retryAt = default;
                        }
                        else if (workerGeneration == generation)
                        {
                            restoreFailed = false;
                            restoreRetryAt = default;
                        }
                        lastCacheFailure = null;
                    }
                    catch (Exception)
                    {
                        if (workerWrite is not null)
                        {
                            var retry = clock.GetUtcNow().AddSeconds(30);
                            if (!disposed)
                            {
                                writeFailed = true;
                                retryAt = retry;
                            }
                        }
                        else if (workerGeneration == generation && IsCurrent(workerCharacter))
                        {
                            var retry = clock.GetUtcNow().AddSeconds(30);
                            if (workerGeneration == generation && IsCurrent(workerCharacter))
                            {
                                restoreFailed = true;
                                restorePending = true;
                                restoreRetryAt = retry;
                            }
                        }
                    }
                    worker = null;
                    workerWrite = null;
                }
                if (disposed) return;
                var dispatchGeneration = generation;
                workerGeneration = generation;
                workerCharacter = snapshot?.ContentId ?? 0;
                if (restorePending)
                {
                    var now = clock.GetUtcNow();
                    if (disposed || generation != dispatchGeneration) return;
                    if (restorePending && now >= restoreRetryAt)
                    {
                        restorePending = false;
                        var identity = workerCharacter;
                        worker = Task.Run(() => cache.LoadAsync(identity, lifetime.Token));
                        return;
                    }
                }
                if (pendingWrites.Count > 0)
                {
                    var now = clock.GetUtcNow();
                    if (disposed || generation != dispatchGeneration) return;
                    if (pendingWrites.Count == 0 || now < retryAt) return;
                    var next = pendingWrites.First();
                    workerWrite = next.Value;
                    worker = Task.Run(async () => { await cache.SaveAsync(next.Value, lifetime.Token).ConfigureAwait(false); return (InventorySnapshot?)null; });
                }
            }
            finally
            {
                pumping = false;
            }
        }
    }

    private bool IsCurrent(ulong contentId) => !disposed && contentId != 0 && snapshot?.ContentId == contentId;
    private bool CanAccept(ulong contentId, InventoryLocationKey key, string name) => IsCurrent(contentId) && key.IsValid
        && InventoryPolicy.ValidName(name) && (key.Kind != InventoryLocationKind.Retainer
            || (snapshot!.RetainerRosterKnown && snapshot.Locations.Any(location => location.Key == key)));

    private void Replace(InventoryLocationSnapshot location) => Publish(snapshot! with
    {
        Locations = InventoryPolicy.Order(snapshot!.Locations.Where(old => old.Key != location.Key).Append(location)),
    });

    private void Publish(InventorySnapshot value)
    {
        value = value with
        {
            Revision = ++revision
        };
        Volatile.Write(ref snapshot, value);
        if (cache is null) return;
        // Keep the latest pending save for each character, including after a character switch.
        if (pendingWrites.ContainsKey(value.ContentId) || pendingWrites.Count < 16) pendingWrites[value.ContentId] = value;
        else lastCacheFailure = "InventoryCacheQueueFull";
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            generation++;
            Volatile.Write(ref snapshot, null);
            lifetime.Cancel();
            var owned = worker;
            // Keep active writes in pendingWrites until Pump collects them under this lock.
            var finalWrites = pendingWrites.Values.ToImmutableArray();
            pendingWrites.Clear();
            disposal = Task.Run(async () =>
            {
                try
                {
                    if (owned is not null)
                        await owned.ConfigureAwait(false);
                }
                catch (Exception) { }
                // Shutdown writes must use only immutable data and cache handles, not host services.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (cache is not null)
                    foreach (var value in finalWrites)
                        try
                        {
                            await cache.SaveAsync(value, deadline.Token).ConfigureAwait(false);
                        }
                        catch (Exception) { }
                lifetime.Dispose();
            });
            return new(disposal);
        }
    }
}

