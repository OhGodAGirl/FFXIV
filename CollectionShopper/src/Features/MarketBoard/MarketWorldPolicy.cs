namespace CollectionShopper;

internal static class MarketWorldPolicy
{
    internal static bool Matches(string? requested, string? current)
        => Select(requested) is { } market && string.Equals(market, Select(current), StringComparison.Ordinal);

    public static string? Select(string? currentWorld)
        => string.IsNullOrWhiteSpace(currentWorld) ? null : currentWorld;

}
