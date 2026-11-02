namespace SpringCleaning.Tests;

public sealed class InventoryItemSafetyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void SplitProtectionHonoursEachToggle(bool materia, bool spiritbond, bool safe)
    {
        Assert.Equal(safe, InventoryItemSafetyPlanner.Discard(false, 1, false,
            materia, spiritbond, true, 10000, false).IsSafe);
    }

    [Fact]
    public void DisabledProtectionDoesNotRequireItsField()
    {
        Assert.True(InventoryItemSafetyPlanner.Discard(false, 1, false, false, true, null, 9999, false).IsSafe);
        Assert.True(InventoryItemSafetyPlanner.Discard(false, 1, false, true, false, false, null, false).IsSafe);
        Assert.False(InventoryItemSafetyPlanner.Discard(false, 1, false, true, false, null, 0, false).IsSafe);
        Assert.False(InventoryItemSafetyPlanner.Discard(false, 1, false, false, true, false, null, false).IsSafe);
        Assert.False(InventoryItemSafetyPlanner.Discard(false, 1, true, false, false, null, null, false).IsSafe);
    }

    [Theory]
    [InlineData(false, 1, false, null)]
    [InlineData(true, 1, false, "symbolic")]
    [InlineData(false, 2, false, "stacked")]
    [InlineData(false, 0, false, "stacked")]
    [InlineData(false, 1, true, "gear-set")]
    [InlineData(false, 1, null, "gear-set")]
    public void StorageProtectsStructureAndGearsetsOnly(bool symbolic, uint quantity, bool? gearset, string? expected)
    {
        var result = InventoryItemSafetyPlanner.StorageReason(symbolic, quantity, gearset);
        if (expected is null) Assert.Null(result);
        else Assert.Contains(expected, result!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(true, false, 1, true)]
    [InlineData(true, false, 9999, true)]
    [InlineData(true, false, 10000, false)]
    [InlineData(true, false, 10001, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(false, true, 10000, true)]
    [InlineData(false, null, null, true)]
    [InlineData(true, null, 0, false)]
    [InlineData(true, false, null, false)]
    public void OptionalDiscardProtectionUsesExactFullSpiritbondAndDoesNotAffectStorage(bool protect, bool? materia, int? spiritbond, bool expected)
    {
        var result = InventoryItemSafetyPlanner.Discard(false, 1, false, protect, materia, (ushort?)spiritbond, false);
        Assert.Equal(expected, result.IsSafe);
        Assert.True(result.IsPresent);
        Assert.Null(InventoryItemSafetyPlanner.StorageReason(false, 1, false));
        Assert.Equal(expected ? "Ready" : materia == true ? "Blocked: materia is attached."
            : materia is null || spiritbond is null ? "Waiting for local materia and spiritbond checks."
            : "Blocked: the item is fully spiritbound.", result.Status);
    }

    [Fact]
    public void CollectableValueIsNeverTreatedAsSpiritbondAndGearsetsStayProtected()
    {
        Assert.Equal("Blocked: collectables require manual review.", InventoryItemSafetyPlanner.Discard(false, 1, false, false, null, null, true).Status);
        Assert.Equal("Blocked: the inventory entry is symbolic.", InventoryItemSafetyPlanner.Discard(true, 1, false, true, null, null, true).Status);
        Assert.False(InventoryItemSafetyPlanner.Discard(false, 1, true, false, null, null, false).IsSafe);
        Assert.Equal(10000, InventoryItemSafetyPlanner.FullySpiritbound);
        Assert.Equal(21800u, DresserPrismPolicy.ItemId);
    }
}
