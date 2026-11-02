#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal sealed record CollectionSourceSnapshot(
    CollectionSourceStatus Status,
    DateTimeOffset? ObservedAtUtc,
    ImmutableArray<uint> ItemIds,
    string? Reason);
