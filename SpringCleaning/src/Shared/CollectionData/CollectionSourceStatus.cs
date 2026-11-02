#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal enum CollectionSourceStatus
{
    Fresh,
    Stale,
    Unavailable,
    NeverObserved,
}
