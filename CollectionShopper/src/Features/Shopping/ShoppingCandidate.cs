namespace CollectionShopper;

internal sealed record ShoppingCandidate(
    uint ItemId,
    string ItemName,
    ushort IconId,
    uint CollectionId,
    string CollectionName,
    string? CollectionIconPath,
    string Patch,
    string Source,
    ShoppingCategory Category,
    int Priority,
    int RemainingPieces,
    bool MappingValid = true);
