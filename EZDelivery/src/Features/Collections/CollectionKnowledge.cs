using Exportaramus.ConsumerKit.V1_1;

namespace EZDelivery.Features.Collections;

/// <summary>Whether collection evidence establishes item ownership.</summary>
public enum OwnershipState
{
    /// <summary>Evidence establishes that the item is already owned.</summary>
    Present,
    /// <summary>Complete evidence establishes absence.</summary>
    Missing,
    /// <summary>Available evidence cannot establish presence or absence.</summary>
    Unknown,
}
/// <summary>Evidence category used to determine ownership.</summary>
public enum CollectionKind
{
    /// <summary>Equipment warning or equipment ownership evidence.</summary>
    Equipment,
    /// <summary>Missing chocobo barding unlocks.</summary>
    Barding,
    /// <summary>Furnishings absent from tracked storage; placed furnishings are not counted.</summary>
    Furniture,
}
/// <summary>Collection ownership decision and explanation derived from available evidence.</summary>
public sealed record Ownership(OwnershipState State, string Reason);

/// <summary>Only validated provider snapshots enter this projection. Absence needs complete evidence.</summary>
public sealed class CollectionKnowledge
{
    private static readonly string[] PresenceSources =
        ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "glamourDresser", "armoire"];
    private static readonly string[] ItemStorageSources =
        ["inventory", "chocoboSaddlebag", "premiumSaddlebag", "retainers"];
    private readonly HashSet<uint> confirmed = [];
    private readonly HashSet<uint> present = [];
    private readonly Dictionary<uint, DateTimeOffset> delivered = [];
    private CollectionDataSnapshot? snapshot;
    private string? character;
    /// <summary>Whether a validated snapshot for the active character is available.</summary>
    public bool Available => snapshot is not null;
    /// <summary>Player-readable provider availability or snapshot-age explanation.</summary>
    public string Status { get; private set; } = "Exportaramus data unavailable";
    /// <summary>Changes when the active collection projection or local acquisition evidence changes.</summary>
    public long Generation { get; private set; }

    /// <summary>Accepts only validated provider state matching the active character.</summary>
    public void Update(CollectionDataClientView view, string? activeCharacter)
    {
        if (character != activeCharacter)
        {
            character = activeCharacter;
            confirmed.Clear();
            delivered.Clear();
            snapshot = null;
            Generation++;
        }
        var accepted = view.Availability is CollectionDataAvailability.Ready or CollectionDataAvailability.Refreshing
            && activeCharacter is not null && view.Snapshot?.ContentId == activeCharacter
            ? view.Snapshot : null;
        // The kit rejects equal-revision byte conflicts. Routine polls may return a new
        // managed object for the same accepted version; that is not a context change.
        if (snapshot is not null && accepted is not null && snapshot.ContentId == accepted.ContentId
            && snapshot.ProviderSessionId == accepted.ProviderSessionId && snapshot.Revision == accepted.Revision)
            accepted = snapshot;
        if (!ReferenceEquals(snapshot, accepted))
        {
            Generation++;
            present.Clear();
            if (accepted is not null)
            {
                foreach (var source in accepted.CurrentItemPresence.Values)
                {
                    foreach (var item in source.Items) present.Add(item.ItemId);
                    if (source.DresserEntries is not { } entries) continue;
                    foreach (var entry in entries)
                    {
                        present.Add(entry.BaseItemId);
                        foreach (var component in entry.Components) present.Add(component.ItemId);
                    }
                }
            }
        }
        snapshot = accepted;
        Status = accepted is null ? $"Exportaramus data unavailable ({view.Failure})"
            : accepted.SourceHealth.Values.Any(health => health.SourceStatus == "Stale" || health.ProvenanceType == "Restored")
                ? "Using last Exportaramus snapshot; storage may have changed since capture"
                : "Collection data from Exportaramus";
    }

    /// <summary>Records a confirmed local purchase until newer collection evidence arrives.</summary>
    public void ConfirmAcquisition(uint itemId)
    {
        if (character is not null && itemId != 0 && confirmed.Add(itemId)) Generation++;
    }

    // Called only after exact inventory removal AND the full seal reward agree.
    /// <summary>Removes local purchase suppression after verified inventory removal and seal credit.</summary>
    public void ConfirmDelivery(string activeCharacter, uint itemId, DateTimeOffset at)
    {
        if (character != activeCharacter || itemId == 0) return;
        confirmed.Remove(itemId);
        if (!delivered.TryGetValue(itemId, out var prior) || at > prior) delivered[itemId] = at;
        Generation++;
    }

    /// <summary>Clears local purchase reports while retaining storage and unlock checks.</summary>
    public void ResetPurchaseTracking(DateTimeOffset at)
    {
        foreach (var id in confirmed) delivered[id] = at;
        confirmed.Clear();
        if (snapshot is { } data)
            foreach (var evidence in data.ReportedOwnership.Items.Where(x => x.Reporter == "EZDelivery"))
                delivered[evidence.ItemId] = at;
        Generation++;
    }

    /// <summary>Determines ownership using category-specific evidence and completeness requirements.</summary>
    public Ownership Evaluate(uint itemId, CollectionKind kind, uint bardingUnlockId = 0)
    {
        if (itemId == 0) return new(OwnershipState.Unknown, "Item mapping unavailable");
        if (confirmed.Contains(itemId)) return new(OwnershipState.Present, "Purchased this session");
        var data = snapshot;
        if (data is null) return new(OwnershipState.Unknown, Status);
        if (present.Contains(itemId)) return new(OwnershipState.Present, "Present in Exportaramus storage or acquisition data");
        foreach (var evidence in data.ReportedOwnership.Items.Where(x => x.ItemId == itemId))
        {
            if (evidence.Reporter == "EZDelivery" && DateTimeOffset.TryParse(evidence.ReportedAtUtc, out var reported))
            {
                if (kind == CollectionKind.Equipment && delivered.TryGetValue(itemId, out var removed) && removed > reported) continue;
                var sources = kind == CollectionKind.Equipment ? PresenceSources : ItemStorageSources;
                if (sources.All(key => CompleteSnapshot(data, key) && data.CurrentItemPresence.ContainsKey(key)
                    && ObservedAfter(data, key, reported))
                    && (kind != CollectionKind.Barding || CompleteSnapshot(data, "bardings") && data.Unlocks.ContainsKey("bardings")
                        && ObservedAfter(data, "bardings", reported))) continue;
            }
            return new(OwnershipState.Present, "Reported acquisition in Exportaramus");
        }
        if (kind == CollectionKind.Barding)
        {
            if (bardingUnlockId == 0) return new(OwnershipState.Unknown, "Barding unlock mapping unavailable");
            if (data.Unlocks.TryGetValue("bardings", out var unlocked) && unlocked.UnlockedIds.Contains(bardingUnlockId))
                return new(OwnershipState.Present, "Barding unlocked");
            if (!CompleteSnapshot(data, "bardings") || !data.Unlocks.ContainsKey("bardings"))
                return new(OwnershipState.Unknown, "Barding unlock data incomplete");
        }
        // Validated furniture/barding catalog categories cannot enter equipment-only storage.
        // Furniture absence here is explicitly storage-only, never a statement about placed housing.
        foreach (var key in kind == CollectionKind.Equipment ? PresenceSources : ItemStorageSources)
        {
            if (!CompleteSnapshot(data, key) || !data.CurrentItemPresence.ContainsKey(key))
                return new(OwnershipState.Unknown, $"Exportaramus snapshot is incomplete for {key}; capture this source in Exportaramus");
        }
        return new(OwnershipState.Missing, kind == CollectionKind.Furniture
            ? "Not observed in supported storage; placed furnishings are not included"
            : "Not present in the last complete Exportaramus snapshot");
    }

    // The user explicitly chose the provider's last complete snapshot, including
    // retained and disk-restored observations. This is snapshot absence, not live absence.
    // Missing, partial, failed and unsupported sources still cannot establish absence.
    private static bool CompleteSnapshot(CollectionDataSnapshot data, string key) =>
        data.SourceCapabilities.TryGetValue(key, out var capability) && capability.CapabilityState == "Supported"
        && data.SourceHealth.TryGetValue(key, out var health) && health.CapabilityState == "Supported"
        && health.SourceStatus is "Fresh" or "Stale" && health.Completeness == "Complete"
        && health.ProvenanceType is "Observed" or "Restored";

    private static bool ObservedAfter(CollectionDataSnapshot data, string key, DateTimeOffset at) =>
        DateTimeOffset.TryParse(data.SourceHealth[key].ObservedAtUtc, out var observed) && observed > at;
}
