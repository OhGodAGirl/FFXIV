namespace Exportaramus.Features.CollectionData.State;

// Plain entries use slot zero; outfit entries use their actual eleven-slot mapping index.
public sealed record DresserEntryComponent(int SlotIndex, uint ItemId);
