#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal static class CollectionDataWireContract
{
    internal const int MaximumNoticeBytes = 4_096;
    internal static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);
}
