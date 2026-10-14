using System.Collections.Immutable;
namespace Exportaramus.Features.CollectionData.State;

public sealed record DresserEntry(int EntryIndex, uint RawItemId, uint BaseItemId, uint? OutfitId,
    ImmutableArray<DresserEntryComponent> Components);
