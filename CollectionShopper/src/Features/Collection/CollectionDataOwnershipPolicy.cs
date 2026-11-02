using System.Collections.Immutable;
using System.Globalization;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper;

internal static class CollectionDataOwnershipPolicy
{
    private static readonly ImmutableArray<string> ConsumableSources =
        ["inventory", "chocoboSaddlebag", "premiumSaddlebag", "retainers"];
    internal static ImmutableArray<string> PhysicalSources { get; } =
        ["inventory", "armoryChest", "chocoboSaddlebag", "premiumSaddlebag", "retainers", "armoire", "glamourDresser"];

    internal static CollectionDataOwnershipState NotChecked { get; } = new(
        false, CollectionDataValidationCode.NoPayload, null, [], [],
        "Exportaramus ownership has not been received for the current character.");

    internal static CollectionDataOwnershipState Build(CollectionDataClientView view, ulong contentId)
    {
        ArgumentNullException.ThrowIfNull(view);
        var snapshot = view.Availability == CollectionDataAvailability.Ready ? view.Snapshot : null;
        var failure = view.Failure;
        if (contentId == 0 || snapshot?.ContentId != contentId.ToString(CultureInfo.InvariantCulture))
        {
            if (snapshot is not null)
                failure = CollectionDataValidationCode.WrongCharacter;
            snapshot = null;
        }
        if (snapshot is null)
        {
            return new(true, failure, null, [], [], contentId == 0
                ? "Exportaramus ownership is unavailable until a character is loaded."
                : $"Exportaramus ownership is {view.Availability.ToString().ToLowerInvariant()} ({failure}). No missing items are inferred.");
        }

        var sources = PhysicalSources.Select(key => DescribeSource(snapshot, key)).ToImmutableArray();
        var owned = PhysicalSources
            .Where(key => IsObserved(snapshot, key) && snapshot.CurrentItemPresence.ContainsKey(key))
            .SelectMany(key => snapshot.CurrentItemPresence[key].Items.Select(item => item.ItemId))
            .Distinct().Order().ToImmutableArray();
        var gaps = sources.Where(source => !source.AssertsOwnership).Select(source => source.Reason).ToArray();
        var detail = gaps.Length == 0
            ? "Exportaramus supplied complete current or last-known physical observations. Unlock readiness is checked separately for each category."
            : "Exportaramus physical observations are incomplete: " + string.Join("; ", gaps) + ".";
        return new(true, CollectionDataValidationCode.None, snapshot, owned, sources, detail);
    }

    internal static ItemOwnership Classify(CollectionDataOwnershipState state, ShoppingCategory category,
        uint itemId, uint unlockId, bool mappingValid = true)
    {
        var snapshot = state.Snapshot;
        if (snapshot is null)
            return new(false, false, false, state.StatusDetail);
        if (state.TrustedOwnedItemIds.Contains(itemId))
            return new(true, true, false, "The item was observed in Exportaramus storage; this does not prove a permanent unlock.");
        if (snapshot.ReportedOwnership.Items.Any(item => item.ItemId == itemId
            // Migration retires this plugin's legacy purchase reports with its legacy history.
            // Current canonical coverage still must establish absence below.
            && !(item.Reporter == PluginIdentity.InternalName && state.LegacyPurchaseArchive.Contains(itemId))
            && (!state.AcknowledgedAcquisitions.TryGetValue(itemId, out var acknowledged)
                || !DateTimeOffset.TryParse(item.ReportedAtUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var reported) || reported > acknowledged)))
            return new(true, true, false, "A reported acquisition suppresses duplicate purchases; it does not prove an unlock or current possession.");
        if (!mappingValid)
            return new(false, false, false, "The public catalog entry has no verified native collection mapping.");

        var key = UnlockKey(category);
        if (key is not null)
        {
            if (!snapshot.Unlocks.TryGetValue(key, out var unlocks) || !IsObserved(snapshot, key))
                return new(false, false, false, DescribeSource(snapshot, key).Reason!);
            if (unlocks.UnlockedIds.Contains(unlockId))
                return new(true, true, true, $"Exportaramus observed the {key} unlock.");
            if (!IsFresh(snapshot, key))
                return new(false, false, false, $"Exportaramus {key} is last known; refresh this collection to verify unlock absence.");
        }
        var gap = state.Sources.FirstOrDefault(source => RequiredPhysicalSources(category).Contains(source.Source) && !source.AssertsOwnership);
        return gap is null
            ? new(true, false, false, "Complete observations contain neither this item nor its unlock.")
            : new(false, false, false, gap.Reason!);
    }

    internal static bool CompleteFor(CollectionDataOwnershipState state, ShoppingCategory category)
    {
        if (state.Snapshot is not { } snapshot || state.Sources.Any(source =>
            RequiredPhysicalSources(category).Contains(source.Source) && !source.AssertsOwnership))
            return false;
        var key = UnlockKey(category);
        return key is null || snapshot.Unlocks.ContainsKey(key) && IsFresh(snapshot, key);
    }

    internal static DiagnosticCheck BuildDiagnosticCheck(CollectionDataOwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new("Exportaramus collection data",
            state.Snapshot is null ? DiagnosticResultKind.NotAvailable
                : state.CanInferMissingItemIds ? DiagnosticResultKind.Pass : DiagnosticResultKind.Warning,
            state.StatusDetail,
            state.CanInferMissingItemIds ? null
                : "Keep Exportaramus loaded. Refresh the named unavailable sources there; unknown ownership never enters the purchase list.");
    }

    internal static string BuildPlayerSummary(CollectionDataOwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Snapshot is null ? state.StatusDetail
            : $"{state.TrustedOwnedItemIds.Length:N0} owned item types";
    }

    private static string? UnlockKey(ShoppingCategory category) => category switch
    {
        ShoppingCategory.Minion => "minions",
        ShoppingCategory.Mount => "mounts",
        ShoppingCategory.Orchestrion => "orchestrions",
        ShoppingCategory.Hairstyle => "hairstyles",
        ShoppingCategory.Facewear => "facewear",
        ShoppingCategory.Fashion => "fashions",
        _ => null,
    };

    // Unlock consumables cannot be stored in equipment slots, the Armoire, or the Glamour Dresser.
    private static ImmutableArray<string> RequiredPhysicalSources(ShoppingCategory category)
        => category is ShoppingCategory.Outfit or ShoppingCategory.Weapon
            ? PhysicalSources
            : ConsumableSources;

    private static bool IsObserved(CollectionDataSnapshot snapshot, string key)
        => snapshot.SourceHealth.TryGetValue(key, out var health)
            && health.SourceStatus is "Fresh" or "Stale"
            && health.Completeness == "Complete" && health.CapabilityState == "Supported"
            && health.ObservedAtUtc is not null;

    private static bool IsFresh(CollectionDataSnapshot snapshot, string key)
        => IsObserved(snapshot, key) && snapshot.SourceHealth[key].SourceStatus == "Fresh";

    private static CollectionDataOwnershipSourceState DescribeSource(CollectionDataSnapshot snapshot, string key)
    {
        snapshot.SourceHealth.TryGetValue(key, out var health);
        var included = snapshot.CurrentItemPresence.TryGetValue(key, out var presence)
            || snapshot.Unlocks.ContainsKey(key);
        var usable = included && IsObserved(snapshot, key);
        return new(key, health?.SourceStatus, usable, presence?.Items.Length ?? 0,
            health?.ObservedAtUtc,
            usable ? $"{key}: {health!.SourceStatus} complete observation"
                : $"{key}: {health?.SourceStatus ?? "omitted"}, {health?.Completeness ?? "unknown"} ({health?.ReasonCode ?? "NoSourceHealth"})");
    }

}
