using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Features.GlamourPlates;

internal static class CollectionProviderPolicy
{
    internal static SharedOwnershipState ClassifyOwnership(
        SavedPlateItem? item,
        CollectionDataSnapshot? snapshot)
    {
        if (item is null)
        {
            return SharedOwnershipState.Empty;
        }

        if (snapshot is null)
        {
            return SharedOwnershipState.Unknown;
        }

        if (Contains(snapshot.GlamourDresser, CollectionSourceStatus.Fresh, item.ItemId) ||
            Contains(snapshot.Armoire, CollectionSourceStatus.Fresh, item.ItemId))
        {
            return SharedOwnershipState.FreshMatch;
        }

        if (Contains(snapshot.GlamourDresser, CollectionSourceStatus.Stale, item.ItemId) ||
            Contains(snapshot.Armoire, CollectionSourceStatus.Stale, item.ItemId))
        {
            return SharedOwnershipState.LastKnownMatch;
        }

        return snapshot.GlamourDresser.Status == CollectionSourceStatus.Fresh &&
               snapshot.Armoire.Status == CollectionSourceStatus.Fresh
            ? SharedOwnershipState.NotObserved
            : SharedOwnershipState.Unknown;
    }

    internal static SharedPlateCompleteness CreatePlateCompleteness(
        SavedPlate? plate,
        CollectionDataSnapshot? snapshot)
    {
        if (plate is null)
        {
            return SharedPlateCompleteness.Empty;
        }

        var states = plate.Items
            .Where(item => item is not null)
            .Select(item => ClassifyOwnership(item, snapshot))
            .ToArray();
        if (states.Length == 0)
        {
            return new SharedPlateCompleteness(
                0,
                0,
                0,
                0,
                0,
                "This saved plate is empty.");
        }

        var fresh = states.Count(state => state == SharedOwnershipState.FreshMatch);
        var lastKnown = states.Count(state => state == SharedOwnershipState.LastKnownMatch);
        var notObserved = states.Count(state => state == SharedOwnershipState.NotObserved);
        var unknown = states.Count(state => state == SharedOwnershipState.Unknown);
        var collected = fresh + lastKnown;
        var summary = snapshot is null
            ? $"{states.Length:N0} item(s); ownership cannot be checked right now."
            : lastKnown > 0
                ? $"{collected:N0} of {states.Length:N0} collected ({lastKnown:N0} last known); {notObserved:N0} not collected; {unknown:N0} unknown."
                : $"{collected:N0} of {states.Length:N0} collected; {notObserved:N0} not collected; {unknown:N0} unknown.";

        return new SharedPlateCompleteness(
            states.Length,
            fresh,
            lastKnown,
            notObserved,
            unknown,
            summary);
    }

    internal static string? DescribeOwnership(SharedOwnershipState state) => state switch
    {
        SharedOwnershipState.Empty => null,
        SharedOwnershipState.FreshMatch => "Shared: Current match",
        SharedOwnershipState.LastKnownMatch => "Shared: Last-known match",
        SharedOwnershipState.NotObserved => "Shared: Not collected",
        SharedOwnershipState.Unknown => "Shared: Unknown",
        _ => "Shared: Unknown",
    };

    internal static string DescribeSlotOwnership(SharedOwnershipState state) => state switch
    {
        SharedOwnershipState.Empty => "Empty",
        SharedOwnershipState.FreshMatch => "Collected",
        SharedOwnershipState.LastKnownMatch => "Stale",
        SharedOwnershipState.NotObserved => "Not collected",
        SharedOwnershipState.Unknown => "Unknown",
        _ => "Unknown",
    };

    internal static string DescribeSummary(
        bool checkedProvider,
        CollectionDataFailure failure,
        CollectionDataSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            var bothObserved = IsObserved(snapshot.GlamourDresser.Status) &&
                               IsObserved(snapshot.Armoire.Status);
            var summary = bothObserved
                ? snapshot.GlamourDresser.Status == CollectionSourceStatus.Stale ||
                  snapshot.Armoire.Status == CollectionSourceStatus.Stale
                    ? "Collection ready from last-known data."
                    : "Collection ready."
                : "Collection partially available; some ownership is unknown.";
            return checkedProvider && failure != CollectionDataFailure.None
                ? $"{summary} Refresh failed; keeping saved ownership."
                : summary;
        }

        if (!checkedProvider)
        {
            return "Collection not available yet.";
        }

        return DescribePlayerFailure(failure);
    }

    internal static string DescribePrimaryStatus(string collectionStatus, int knownOwnedItems)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionStatus);
        return collectionStatus.StartsWith("Collection ready", StringComparison.Ordinal)
            || collectionStatus.StartsWith("Collection partially", StringComparison.Ordinal)
                ? $"{collectionStatus.TrimEnd('.')} · {knownOwnedItems:N0} owned"
                : collectionStatus;
    }

    internal static SharedCollectionDiagnostics CreateDiagnostics(
        bool checkedProvider,
        CollectionDataFailure failure,
        CollectionDataSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            if (!checkedProvider)
            {
                return SharedCollectionDiagnostics.Empty;
            }

            var unavailableDetail = DescribeFailure(failure);
            return new SharedCollectionDiagnostics(
                "Exportaramus ownership is unavailable. The collection is unknown, not empty; local saved plates remain available.",
                new SharedCollectionSourceStatus(
                    "Glamour Dresser",
                    "Not Available",
                    unavailableDetail),
                new SharedCollectionSourceStatus(
                    "Armoire",
                    "Not Available",
                    unavailableDetail));
        }

        var guidance = snapshot.GlamourDresser.Status == CollectionSourceStatus.Stale ||
                       snapshot.Armoire.Status == CollectionSourceStatus.Stale
            ? "Last-known ownership remains available. Missing items remain unknown until both sources are current; loading still revalidates native sources and dyes."
            : "Exportaramus is the collection ownership authority. Plate loading separately revalidates exact native sources and dyes.";
        if (checkedProvider && failure != CollectionDataFailure.None)
        {
            guidance = $"{guidance} Last refresh failed: {DescribeFailure(failure)}";
        }

        return new SharedCollectionDiagnostics(
            guidance,
            CreateSourceStatus("Glamour Dresser", snapshot.GlamourDresser),
            CreateSourceStatus("Armoire", snapshot.Armoire));
    }

    internal static string DescribeCollectionView(
        bool checkedProvider,
        CollectionDataFailure failure,
        CollectionDataSnapshot? snapshot,
        int knownOwnedItems)
    {
        if (snapshot is null)
        {
            return checkedProvider
                ? DescribePlayerFailure(failure)
                : "Collection not available yet.";
        }

        return $"Showing {knownOwnedItems:N0} collected item(s).";
    }

    internal static string? DescribeActionValidationHint(CollectionDataSnapshot? snapshot, SavedPlate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        if (!HasFreshPlanningData(snapshot))
        {
            return null;
        }

        var matched = CountDistinctFreshMatches(snapshot!, plate);
        if (matched == 0)
        {
            return null;
        }

        return $"Exportaramus still sees {matched:N0} matching item ID(s) in Fresh storage. The load was stopped because action-time native validation could not confirm the exact source and dyes.";
    }

    internal static bool CanPublishBackgroundRefresh(
        ulong requestedContentId,
        ulong currentContentId,
        CollectionDataFailure failure,
        bool cancellationRequested)
    {
        if (cancellationRequested ||
            requestedContentId == 0 ||
            currentContentId == 0 ||
            requestedContentId != currentContentId)
        {
            return false;
        }

        return failure is not CollectionDataFailure.Busy
            and not CollectionDataFailure.Disposed
            and not CollectionDataFailure.StaleCompletion;
    }

    private static SharedCollectionSourceStatus CreateSourceStatus(
        string name,
        CollectionSourceSnapshot source)
    {
        var detail = source.Status switch
        {
            CollectionSourceStatus.Fresh => "Used for collection views and plate planning. Loading revalidates exact native source IDs and dyes.",
            CollectionSourceStatus.Stale => "Saved ownership remains usable for collection views and plate planning. It updates automatically when the game exposes this source again.",
            CollectionSourceStatus.Unavailable => string.IsNullOrWhiteSpace(source.Reason)
                ? "The provider marked this source unavailable."
                : source.Reason,
            CollectionSourceStatus.NeverObserved => "The provider has not observed this source yet.",
            _ => "The provider state is unavailable.",
        };

        if (source.ObservedAtUtc is { } observed)
        {
            detail = $"{detail} Last observed {observed:yyyy-MM-dd HH:mm:ss 'UTC'}.";
        }

        return new SharedCollectionSourceStatus(
            name,
            DescribeSourceLabel(source.Status),
            detail);
    }

    internal static string DescribeFreshness(CollectionFreshness freshness) => freshness switch
    {
        CollectionFreshness.Fresh => "Current",
        CollectionFreshness.LastKnown => "Last known",
        _ => "Unknown",
    };

    private static int CountDistinctFreshMatches(CollectionDataSnapshot snapshot, SavedPlate plate)
    {
        var freshItemIds = new HashSet<uint>();
        if (snapshot.GlamourDresser.Status == CollectionSourceStatus.Fresh)
        {
            freshItemIds.UnionWith(snapshot.GlamourDresser.ItemIds);
        }

        if (snapshot.Armoire.Status == CollectionSourceStatus.Fresh)
        {
            freshItemIds.UnionWith(snapshot.Armoire.ItemIds);
        }

        var matched = new HashSet<uint>();
        foreach (var item in plate.Items)
        {
            if (item is not null && freshItemIds.Contains(item.ItemId))
            {
                matched.Add(item.ItemId);
            }
        }

        return matched.Count;
    }

    private static bool HasFreshPlanningData(CollectionDataSnapshot? snapshot)
        => snapshot is not null &&
           (snapshot.GlamourDresser.Status == CollectionSourceStatus.Fresh ||
            snapshot.Armoire.Status == CollectionSourceStatus.Fresh);

    private static bool IsObserved(CollectionSourceStatus status) =>
        status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale;

    private static string DescribeSourceLabel(CollectionSourceStatus status) => status switch
    {
        CollectionSourceStatus.Fresh => "Current",
        CollectionSourceStatus.Stale => "Last known",
        CollectionSourceStatus.Unavailable => "Unavailable",
        CollectionSourceStatus.NeverObserved => "Never observed",
        _ => "Unknown",
    };

    private static bool Contains(
        CollectionSourceSnapshot source,
        CollectionSourceStatus requiredStatus,
        uint itemId)
        => source.Status == requiredStatus && source.ItemIds.AsSpan().BinarySearch(itemId) >= 0;

    private static string DescribeFailure(CollectionDataFailure failure) => failure switch
    {
        CollectionDataFailure.NoCharacter
            => "Shared collection data is waiting for the current character.",
        CollectionDataFailure.ProviderUnavailable
            => "Shared collection data provider unavailable.",
        CollectionDataFailure.EmptyPayload
            => "Shared collection data provider returned no snapshot.",
        CollectionDataFailure.PayloadTooLarge
            => "Shared collection data snapshot exceeded bounds and was rejected.",
        CollectionDataFailure.MalformedJson
            => "Shared collection data snapshot was malformed and was rejected.",
        CollectionDataFailure.UnsupportedProtocol
            => "Shared collection data protocol version is unsupported.",
        CollectionDataFailure.InvalidProvider
            => "Shared collection data provider identity was invalid.",
        CollectionDataFailure.WrongCharacter
            => "Shared collection data was rejected for another character.",
        CollectionDataFailure.InvalidRevision
            => "Shared collection data revision was invalid.",
        CollectionDataFailure.OutOfOrder
            => "Shared collection data snapshot arrived out of order.",
        CollectionDataFailure.InvalidTimestamp
            => "Shared collection data timestamp was invalid.",
        CollectionDataFailure.InvalidSources
            => "Shared collection data source bounds were invalid.",
        CollectionDataFailure.Busy
            => "Shared collection data refresh is already in progress.",
        CollectionDataFailure.Disposed
            => "Shared collection data client is unavailable.",
        CollectionDataFailure.StaleCompletion
            => "Shared collection data refresh finished after character state changed.",
        _ => "Shared collection data is unavailable.",
    };

    private static string DescribePlayerFailure(CollectionDataFailure failure) => failure switch
    {
        CollectionDataFailure.NoCharacter
            => "Collection will be available after logging in.",
        CollectionDataFailure.Busy
            => "Collection is refreshing.",
        _ => "Exportaramus is unavailable; ownership is unknown.",
    };
}

