#nullable enable

using System;
using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal sealed record CollectionDresserEntry(int EntryIndex, uint RawItemId, uint BaseItemId,
    uint? OutfitId, ImmutableArray<CollectionDresserComponent> Components);
