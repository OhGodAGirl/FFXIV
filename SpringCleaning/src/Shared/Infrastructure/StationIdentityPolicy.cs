namespace SpringCleaning;

internal static class StationIdentityPolicy
{
    public static bool Matches(
        IReadOnlySet<uint> stableIds,
        uint baseId,
        string displayedName,
        string englishFallbackName) =>
        stableIds.Contains(baseId)
        || displayedName.Contains(englishFallbackName, StringComparison.OrdinalIgnoreCase);
}
