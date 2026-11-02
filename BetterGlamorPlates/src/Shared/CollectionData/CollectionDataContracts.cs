using System;
using System.Collections.Immutable;

namespace BetterGlamorPlates.Shared.CollectionData;

// Plugin-owned projection of validated Exportaramus facts; not a wire contract.
internal enum CollectionSourceStatus
{
    Fresh,
    Stale,
    Unavailable,
    NeverObserved,
}

internal enum CollectionSourceName
{
    Inventory,
    ArmoryChest,
    ChocoboSaddlebag,
    PremiumSaddlebag,
    Retainers,
    Armoire,
    GlamourDresser,
}

internal sealed record CollectionSourceSnapshot(
    CollectionSourceStatus Status,
    DateTimeOffset? ObservedAtUtc,
    ImmutableArray<uint> ItemIds,
    string? Reason);

internal sealed record CollectionDataSnapshot(
    string ProviderInternalName,
    string ProviderVersion,
    ulong ContentId,
    ulong Revision,
    DateTimeOffset CapturedAtUtc,
    CollectionSourceSnapshot Inventory,
    CollectionSourceSnapshot ArmoryChest,
    CollectionSourceSnapshot ChocoboSaddlebag,
    CollectionSourceSnapshot PremiumSaddlebag,
    CollectionSourceSnapshot Retainers,
    CollectionSourceSnapshot Armoire,
    CollectionSourceSnapshot GlamourDresser)
{
    internal CollectionSourceSnapshot GetSource(CollectionSourceName source) => source switch
    {
        CollectionSourceName.Inventory => Inventory,
        CollectionSourceName.ArmoryChest => ArmoryChest,
        CollectionSourceName.ChocoboSaddlebag => ChocoboSaddlebag,
        CollectionSourceName.PremiumSaddlebag => PremiumSaddlebag,
        CollectionSourceName.Retainers => Retainers,
        CollectionSourceName.Armoire => Armoire,
        CollectionSourceName.GlamourDresser => GlamourDresser,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}

internal enum CollectionDataFailure
{
    None,
    NoCharacter,
    ProviderUnavailable,
    Busy,
    Disposed,
    EmptyPayload,
    PayloadTooLarge,
    MalformedJson,
    UnsupportedProtocol,
    InvalidProvider,
    WrongCharacter,
    InvalidRevision,
    OutOfOrder,
    InvalidTimestamp,
    InvalidSources,
    StaleCompletion,
}

internal readonly record struct CollectionDataParseResult(
    CollectionDataSnapshot? Snapshot,
    CollectionDataFailure Failure)
{
    internal bool Accepted => Failure == CollectionDataFailure.None && Snapshot is not null;

    internal static CollectionDataParseResult Reject(CollectionDataFailure failure)
    {
        if (failure == CollectionDataFailure.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A rejected result requires a failure reason.");
        }

        return new CollectionDataParseResult(null, failure);
    }

    internal static CollectionDataParseResult Accept(CollectionDataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new CollectionDataParseResult(snapshot, CollectionDataFailure.None);
    }
}

