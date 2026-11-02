namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Produces the deterministic saved-plate rail shown by the main window.
/// </summary>
internal static class SavedPlateQuery
{
    internal static IReadOnlyList<SavedPlate> Apply(
        IEnumerable<SavedPlate> plates,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(plates);
        var normalizedSearch = search?.Trim() ?? string.Empty;

        return plates
            .Where(plate => string.IsNullOrEmpty(normalizedSearch)
                || plate.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
