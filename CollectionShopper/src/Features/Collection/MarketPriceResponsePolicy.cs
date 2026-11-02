using System.Text.Json;

namespace CollectionShopper;

/// <summary>
/// Converts one Universalis item payload into the cheapest safe NQ and HQ quote.
/// </summary>
internal static class MarketPriceResponsePolicy
{
    internal static void AddAggregatedMinimums(
        IDictionary<uint, MarketPriceQuote> prices,
        string market,
        JsonElement item)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        if (!TryGetUInt32(item, "itemId", out var itemId) || itemId == 0)
            return;

        var normalQuality = TryGetAggregatedPrice(item, "nq", itemId, market, false);
        var highQuality = TryGetAggregatedPrice(item, "hq", itemId, market, true);
        if (normalQuality is not null || highQuality is not null)
            prices[itemId] = new MarketPriceQuote(normalQuality, highQuality);
    }

    internal static void AddCheapestListing(
        IDictionary<uint, MarketPriceQuote> prices,
        uint itemId,
        JsonElement item)
    {
        ArgumentNullException.ThrowIfNull(prices);
        JsonElement listings = default;
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("listings", out listings)
            || listings.ValueKind != JsonValueKind.Array)
            return;

        MarketPrice? cheapestNq = null;
        MarketPrice? cheapestHq = null;
        foreach (var listing in listings.EnumerateArray())
        {
            uint quantity = default;
            if (!TryGetUInt32(listing, "pricePerUnit", out var pricePerUnit)
                || pricePerUnit == 0
                || !TryGetUInt32(listing, "quantity", out quantity))
            {
                continue;
            }

            var isHighQuality = false;
            if (listing.TryGetProperty("hq", out var hq))
            {
                if (hq.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    continue;
                isHighQuality = hq.GetBoolean();
            }

            var price = new MarketPrice(
                itemId,
                pricePerUnit,
                quantity,
                listing.TryGetProperty("worldName", out var world)
                    && world.ValueKind == JsonValueKind.String
                        ? world.GetString()!
                        : string.Empty,
                isHighQuality);
            if (price.Quantity != 1)
                continue;

            if (price.IsHighQuality)
            {
                if (cheapestHq is null || price.PricePerUnit < cheapestHq.PricePerUnit)
                    cheapestHq = price;
            }
            else if (cheapestNq is null || price.PricePerUnit < cheapestNq.PricePerUnit)
            {
                cheapestNq = price;
            }
        }

        if (cheapestNq is not null || cheapestHq is not null)
            prices[itemId] = new MarketPriceQuote(cheapestNq, cheapestHq);
    }

    private static MarketPrice? TryGetAggregatedPrice(
        JsonElement item,
        string qualityProperty,
        uint itemId,
        string market,
        bool isHighQuality)
    {
        if (!item.TryGetProperty(qualityProperty, out var quality)
            || quality.ValueKind != JsonValueKind.Object
            || !quality.TryGetProperty("minListing", out var minimumListing)
            || minimumListing.ValueKind != JsonValueKind.Object
            || !minimumListing.TryGetProperty("world", out var worldMinimum)
            || !TryGetUInt32(worldMinimum, "price", out var pricePerUnit)
            || pricePerUnit == 0)
        {
            return null;
        }

        return new MarketPrice(itemId, pricePerUnit, 1, market, isHighQuality);
    }

    private static bool TryGetUInt32(JsonElement element, string propertyName, out uint value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetUInt32(out value);
    }

}
