using System.Text.Json.Serialization;

namespace CollectionShopper;

internal sealed record MarketPriceQuote(
    MarketPrice? NormalQuality,
    MarketPrice? HighQuality);

