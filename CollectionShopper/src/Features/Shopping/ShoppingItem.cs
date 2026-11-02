using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record ShoppingItem(
    uint ItemId,
    string ItemName,
    uint CollectionId,
    string CollectionName,
    string? CollectionIconPath,
    string Patch,
    string Source,
    ushort IconId,
    bool OwnershipKnown,
    bool IsLocallyOwned,
    MarketPriceQuote? Prices,
    ShoppingCategory Category,
    int CollectionPriority,
    int RemainingPiecesInCollection,
    bool MappingValid = true);

