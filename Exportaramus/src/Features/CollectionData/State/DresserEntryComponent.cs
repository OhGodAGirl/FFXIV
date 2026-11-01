namespace Exportaramus.Features.CollectionData.State;

// Plain entries use slot 0. Outfits use their index in the 11-slot map.
public sealed record DresserEntryComponent(int SlotIndex, uint ItemId);
