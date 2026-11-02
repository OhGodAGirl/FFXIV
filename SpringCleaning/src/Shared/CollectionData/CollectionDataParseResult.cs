#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

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
