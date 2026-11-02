namespace BetterGlamorPlates.Features.GlamourPlates;

internal static class PlateLibrary
{
    internal static SavedPlate Create(string name, DateTime createdAtUtc, Guid plateId)
        => new()
        {
            Id = plateId,
            Name = NormalizeName(name),
            UpdatedAtUtc = createdAtUtc,
        };

    internal static SavedPlate Duplicate(
        SavedPlate source,
        DateTime createdAtUtc,
        Guid plateId,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceItems = source.Items ?? [];
        return new SavedPlate
        {
            Id = plateId,
            IconId = source.IconId,
            LibraryOrder = source.LibraryOrder,
            Name = NormalizeName(name ?? $"{source.Name} Copy"),
            Items = Enumerable.Range(0, PlateRules.SlotCount)
                .Select(index => index >= sourceItems.Length || sourceItems[index] is not { } item
                    ? null
                    : new SavedPlateItem
                    {
                        ItemId = item.ItemId,
                        Stain0Id = item.Stain0Id,
                        Stain1Id = item.Stain1Id,
                    })
                .ToArray(),
            UpdatedAtUtc = createdAtUtc,
        };
    }

    internal static void EnsureShape(SavedPlate plate)
    {
        var existingItems = plate.Items;
        if (existingItems?.Length == PlateRules.SlotCount)
            return;

        var items = new SavedPlateItem?[PlateRules.SlotCount];
        if (existingItems is not null)
            Array.Copy(existingItems, items, Math.Min(existingItems.Length, items.Length));
        plate.Items = items;
    }

    internal static string NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "New Plate" : normalized;
    }
}
