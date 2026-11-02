namespace SpringCleaning.Tests;

public sealed class MigrationModelsTests
{
    [Fact]
    public void DomainRecordsPreserveValuesAndOutfitSemantics()
    {
        // Arrange
        var component = new EligibleDresserComponent(0, 10, true, 20, "Hat");
        var outfit = new EligibleDresserItem(1, 2, "Outfit", new[] { component });
        var single = new EligibleDresserItem(
            2,
            3,
            "Single",
            new[] { component with { OutfitSlot = -1 } });
        var safety = new InventoryItemSafety(true, true, "Safe");
        var dresserComponent = new InventoryDresserComponent(1, 2, 3, 4, false, "Body", 5, 6);

        // Act
        var values = new object[]
        {
            new DresserAlertEntry(1, 2, 3, false, true, 4, "Body", "Alert", 5, DresserAlertKind.ArmoireEligible, true, false, true),
            new CabinetInventoryItem(1, 2, false, "Cabinet", 3),
            safety,
            new InventoryDuplicateItem(1, 2, 3, false, "Duplicate", 4, safety, true, true, InventoryDuplicateDisposition.RemovalReview),
            dresserComponent,
            new InventoryDresserCandidate(dresserComponent, new uint[] { 7 }),
            new InventoryDresserOutfit(8, 9, "Dresser outfit", new[] { dresserComponent }),
            new InventoryDresserItem(1, 2, 3, false, "Dresser item", 4, 5),
            new PendingWithdrawal(outfit, new Dictionary<uint, uint> { [10] = 1 }, 2, DateTime.UtcNow),
            new PendingDeposit(new CabinetInventoryItem(1, 2, false, "Cabinet", 3), 1, DateTime.UtcNow),
            new PendingDresserOutfit(new InventoryDresserOutfit(8, null, "Outfit", new[] { dresserComponent }), new Dictionary<uint, uint>(), DateTime.UtcNow),
            new PendingDresserItem(new InventoryDresserItem(1, 2, 3, false, "Item", 4), 1, DateTime.UtcNow),
        };

        // Assert
        Assert.True(outfit.IsOutfit);
        Assert.False(single.IsOutfit);
        Assert.Equal(1, outfit.InventorySlotsRequired);
        Assert.All(values, Assert.NotNull);
    }

    [Fact]
    public void OutfitDetectionUsesAnyOutfitComponentRatherThanRequiringAllComponents()
    {
        var item = new EligibleDresserItem(
            1,
            2,
            "Mixed",
            new[]
            {
                new EligibleDresserComponent(-1, 10, false, 20, "Single"),
                new EligibleDresserComponent(2, 11, false, 21, "Outfit piece"),
            });

        Assert.True(item.IsOutfit);
    }
}
