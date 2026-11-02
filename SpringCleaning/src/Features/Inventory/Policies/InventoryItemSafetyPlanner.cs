namespace SpringCleaning;

internal static class InventoryItemSafetyPlanner
{
    // Integration assumption: the game's 100% spiritbond value is 10,000.
    // Collectability shares the field and must never be interpreted as spiritbond.
    internal const ushort FullySpiritbound = 10_000;

    internal static string? StorageReason(bool symbolic, uint quantity, bool? gearset) =>
        symbolic ? "Blocked: the inventory entry is symbolic."
        : quantity != 1 ? "Blocked: stacked items are not stored automatically."
        : gearset != false ? "Blocked: gear-set membership could not be excluded."
        : null;

    internal static InventoryItemSafety Discard(bool symbolic, uint quantity, bool? gearset,
        bool protectMateriaAndSpiritbond, bool? hasMateria, ushort? spiritbond, bool collectable)
        => Discard(symbolic, quantity, gearset, protectMateriaAndSpiritbond, protectMateriaAndSpiritbond,
            hasMateria, spiritbond, collectable);

    internal static InventoryItemSafety Discard(bool symbolic, uint quantity, bool? gearset,
        bool protectMateria, bool protectSpiritbond, bool? hasMateria, ushort? spiritbond, bool collectable)
    {
        var reason = StorageReason(symbolic, quantity, gearset);
        if (reason is null && collectable)
            reason = "Blocked: collectables require manual review.";
        if (reason is null)
        {
            if ((protectMateria && hasMateria is null) || (protectSpiritbond && spiritbond is null))
                reason = "Waiting for local materia and spiritbond checks.";
            else if (protectMateria && hasMateria == true)
                reason = "Blocked: materia is attached.";
            else if (protectSpiritbond && spiritbond.GetValueOrDefault() == FullySpiritbound)
                reason = "Blocked: the item is fully spiritbound.";
        }
        return new(true, reason is null, reason ?? "Ready");
    }
}
