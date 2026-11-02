using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record MarketPrice(
    uint ItemId,
    uint PricePerUnit,
    uint Quantity,
    string WorldName,
    bool IsHighQuality);

