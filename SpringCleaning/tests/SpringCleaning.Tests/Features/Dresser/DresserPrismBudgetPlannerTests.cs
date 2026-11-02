namespace SpringCleaning.Tests;

public sealed class DresserPrismBudgetPlannerTests
{
    [Fact]
    public void PartialBudgetKeepsOutfitsAtomicAndUsesRemainingPrismsForItems()
    {
        // Arrange
        var firstOutfit = CreateOutfit("First", 3);
        var oversizedOutfit = CreateOutfit("Oversized", 4);
        var items = new[]
        {
            CreateItem("Hat", 1),
            CreateItem("Gloves", 2),
        };

        // Act
        var plan = DresserPrismBudgetPlanner.Plan(
            new[] { firstOutfit, oversizedOutfit },
            items,
            availablePrisms: 5);

        // Assert
        Assert.Equal(new[] { firstOutfit }, plan.Outfits);
        Assert.Equal(items, plan.Items);
        Assert.Equal(5, plan.SelectedPieces);
        Assert.Equal(4, plan.SkippedPieces);
    }

    [Fact]
    public void ZeroPrismsSelectsNothingWithoutSplittingAnOutfit()
    {
        // Arrange
        var outfit = CreateOutfit("Company Attire", 2);
        var item = CreateItem("Company Hat", 1);

        // Act
        var plan = DresserPrismBudgetPlanner.Plan(
            new[] { outfit },
            new[] { item },
            availablePrisms: 0);

        // Assert
        Assert.Empty(plan.Outfits);
        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.SelectedPieces);
        Assert.Equal(3, plan.SkippedPieces);
    }

    [Fact]
    public void EmptyOutfitIsSkippedRatherThanSelected()
    {
        var empty = CreateOutfit("Empty", 0);

        var plan = DresserPrismBudgetPlanner.Plan(
            new[] { empty },
            Array.Empty<InventoryDresserItem>(),
            availablePrisms: 1);

        Assert.Empty(plan.Outfits);
        Assert.Equal(0, plan.SelectedPieces);
        Assert.Equal(0, plan.SkippedPieces);
    }

    [Fact]
    public void OutfitThatExactlyMatchesTheRemainingBudgetIsSelected()
    {
        var exact = CreateOutfit("Exact", 3);

        var plan = DresserPrismBudgetPlanner.Plan(
            new[] { exact },
            Array.Empty<InventoryDresserItem>(),
            availablePrisms: 3);

        Assert.Equal(new[] { exact }, plan.Outfits);
        Assert.Equal(3, plan.SelectedPieces);
        Assert.Equal(0, plan.SkippedPieces);
    }

    private static InventoryDresserOutfit CreateOutfit(string name, int pieces)
    {
        var components = Enumerable.Range(0, pieces)
            .Select(index => new InventoryDresserComponent(
                InventoryTypeId: 0,
                SlotIndex: index,
                OutfitSlot: index,
                ItemId: (uint)(1_000 + index),
                IsHighQuality: false,
                Name: $"{name} {index}",
                IconId: 0))
            .ToArray();
        return new InventoryDresserOutfit(1, null, name, components);
    }

    private static InventoryDresserItem CreateItem(string name, int slot)
    {
        return new InventoryDresserItem(
            InventoryTypeId: 0,
            SlotIndex: slot,
            ItemId: (uint)(2_000 + slot),
            IsHighQuality: false,
            Name: name,
            IconId: 0);
    }
}
