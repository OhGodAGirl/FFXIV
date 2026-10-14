using System.Collections.Immutable;

namespace Exportaramus.Features.Inventory;

/// <summary>Accepts complete location observations; readers receive immutable prepared values.</summary>
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
    private Task? disposal;
    private string? lastCacheFailure;
    private DateTimeOffset retryAt;

    public string? LastCacheFailure { get { lock (sync) return lastCacheFailure; } }
    public InventorySnapshot? GetSnapshot() => Volatile.Read(ref snapshot);

    public void SetCharacter(ulong contentId)
    {
        lock (sync)
        {
            if (disposed || (snapshot?.ContentId ?? 0) == contentId) return;
            generation++;
            Volatile.Write(ref snapshot, contentId == 0 ? null : InventoryPolicy.Empty(contentId, ++revision));
            restorePending = contentId != 0 && cache is not null;
        }
    }

    public bool ObserveRoster(ulong contentId, IEnumerable<InventoryRetainer> roster, DateTimeOffset? observedAt = null)
    {
        lock (sync)
        {
            if (!IsCurrent(contentId)) return false;
            var rosterObservedAt = observedAt ?? clock.GetUtcNow();
            if (rosterObservedAt == default || snapshot!.RetainerRosterObservedAtUtc > rosterObservedAt) return false;
            var retainers = new Dictionary<ulong, InventoryRetainer>();
            foreach (var retainer in roster)
            {
                if (retainer is null || retainer.RetainerId == 0 || !InventoryPolicy.ValidName(retainer.Name)
                    || retainers.Count == InventoryPolicy.MaximumRetainers || !retainers.TryAdd(retainer.RetainerId, retainer)) return false;
            }
            var locations = snapshot!.Locations.Where(location => location.Key.Kind != InventoryLocationKind.Retainer).ToList();
            foreach (var retainer in retainers.Values)
            {
                var key = new InventoryLocationKey(InventoryLocationKind.Retainer, retainer.RetainerId);
                var old = snapshot.Locations.FirstOrDefault(location => location.Key == key);
                locations.Add(old is null ? InventoryPolicy.Unknown(key, retainer.Name) : old with { Name = retainer.Name });
            }
            var ordered = InventoryPolicy.Order(locations);
            if (snapshot.RetainerRosterKnown && snapshot.Locations.SequenceEqual(ordered)) return false;
            Publish(snapshot with { RetainerRosterKnown = true, RetainerRosterObservedAtUtc = rosterObservedAt.ToUniversalTime(), Locations = ordered });
            return true;
        }
    }

    public bool AcceptLocation(ulong contentId, InventoryLocationKey key, string name, IEnumerable<InventoryItemQuantity> items, DateTimeOffset observedAt)
    {
        lock (sync)
        {
            if (!CanAccept(contentId, key, name) || observedAt == default) return false;
            // CanAccept guarantees a standard location or a member of the current Retainer roster.
            var old = snapshot!.Locations.First(location => location.Key == key);
            if (old.ObservedAtUtc > observedAt) return false;
            var quantified = key.Kind is not (InventoryLocationKind.Armoire or InventoryLocationKind.GlamourDresser);
            if (!InventoryPolicy.TryNormalize(items, out var normalized, quantified))
                return MarkUnavailable(contentId, key, name, observedAt, true);
            if (old is { State: InventoryObservationState.Observed } && old.Name == name
                && old.Items.SequenceEqual(normalized) && observedAt - old.ObservedAtUtc!.Value < TimeSpan.FromMinutes(1)) return false;
            Replace(new(key, name, InventoryObservationState.Observed, observedAt.ToUniversalTime(), observedAt.ToUniversalTime(), normalized));
            return true;
        }
    }

    public bool MarkUnavailable(ulong contentId, InventoryLocationKey key, string name, DateTimeOffset attemptedAt, bool readFailed)
    {
        lock (sync)
        {
            if (!CanAccept(contentId, key, name) || attemptedAt == default) return false;
            // CanAccept guarantees this location exists in the current snapshot.
            var old = snapshot!.Locations.First(location => location.Key == key);
            if (old.AttemptedAtUtc > attemptedAt) return false;
            var state = readFailed ? InventoryObservationState.ReadFailed : old.HasObservation ? InventoryObservationState.Retained : InventoryObservationState.Unknown;
            if (old.State == state && old.Name == name) return false;
            Replace(old with { Name = name, State = state, AttemptedAtUtc = attemptedAt.ToUniversalTime() });
            return true;
        }
    }

    /// <summary>Collect completed work and launch at most one owned I/O operation, without waiting.</summary>
    public void Pump()
    {
        lock (sync)
        {
            if (disposed || cache is null || worker is { IsCompleted: false }) return;
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
                        && ReferenceEquals(pending, workerWrite)) pendingWrites.Remove(workerWrite.ContentId);
                    lastCacheFailure = null;
                    retryAt = default;
                }
                catch (Exception)
                {
                    lastCacheFailure = "InventoryCacheUnavailable";
                    retryAt = clock.GetUtcNow().AddSeconds(30);
                }
                worker = null;
                workerWrite = null;
            }
            workerGeneration = generation;
            workerCharacter = snapshot?.ContentId ?? 0;
            if (restorePending)
            {
                restorePending = false;
                var identity = workerCharacter;
                worker = Task.Run(() => cache.LoadAsync(identity, lifetime.Token));
            }
            else if (pendingWrites.Count > 0 && clock.GetUtcNow() >= retryAt)
            {
                var next = pendingWrites.First();
                workerWrite = next.Value;
                worker = Task.Run(async () => { await cache.SaveAsync(next.Value, lifetime.Token).ConfigureAwait(false); return (InventorySnapshot?)null; });
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
        value = value with { Revision = ++revision };
        Volatile.Write(ref snapshot, value);
        if (cache is null) return;
        // Bounded per-character coalescing retains final writes across ordinary character switches.
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
            // An in-flight write remains in pendingWrites until Pump collects it under this same lock.
            var finalWrites = pendingWrites.Values.ToImmutableArray();
            pendingWrites.Clear();
            disposal = Task.Run(async () =>
            {
                try { if (owned is not null) await owned.ConfigureAwait(false); } catch (Exception) { }
                // Final writes own only immutable values and cache handles, never host services.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (cache is not null)
                    foreach (var value in finalWrites)
                        try { await cache.SaveAsync(value, deadline.Token).ConfigureAwait(false); } catch (Exception) { }
                lifetime.Dispose();
            });
            return new(disposal);
        }
    }
}

