namespace SpringCleaning.Tests;

public sealed class MigrationBatchPlannerTests
{
    [Fact]
    public void GetBatchPieceCountPreservesWholeOutfits()
    {
        var eligible = new[]
        {
            CreateItem("Large outfit", 4),
            CreateItem("Small outfit", 2),
        };

        var selectedPieces = MigrationBatchPlanner.GetBatchPieceCount(eligible, 3);

        Assert.Equal(2, selectedPieces);
    }

    [Fact]
    public void GetBatchPieceCountFillsAvailableSlotsWithoutSplittingItems()
    {
        var eligible = new[]
        {
            CreateItem("Outfit", 3),
            CreateItem("Hat", 1),
            CreateItem("Other outfit", 2),
        };

        var selectedPieces = MigrationBatchPlanner.GetBatchPieceCount(eligible, 4);

        Assert.Equal(4, selectedPieces);
    }

    [Fact]
    public void GetBatchPieceCountReturnsZeroWithoutUsableInventorySpace()
    {
        var selectedPieces = MigrationBatchPlanner.GetBatchPieceCount(
            new[] { CreateItem("Hat", 1) },
            0);

        Assert.Equal(0, selectedPieces);
    }

    [Fact]
    public void GetBatchPieceCountSkipsEmptyEntriesAndNegativeCapacity()
    {
        // Arrange
        var entries = new[] { CreateItem("Empty", 0), CreateItem("Hat", 1) };

        // Act
        var withOneSlot = MigrationBatchPlanner.GetBatchPieceCount(entries, 1);
        var withNegativeSlots = MigrationBatchPlanner.GetBatchPieceCount(entries, -1);

        // Assert
        Assert.Equal(1, withOneSlot);
        Assert.Equal(0, withNegativeSlots);
    }

    [Fact]
    public void FindNextItemSkipsAnOutfitThatDoesNotFit()
    {
        var eligible = new[]
        {
            CreateItem("Large outfit", 5) with { PrismBoxIndex = 41, RawItemId = 5001 },
            CreateItem("Hat", 1) with { PrismBoxIndex = 73, RawItemId = 9002 },
        };

        var selected = MigrationBatchPlanner.FindNextItem(eligible, 2);

        Assert.NotNull(selected);
        Assert.Equal("Hat", selected.Name);
        Assert.Equal(73, selected.PrismBoxIndex);
        Assert.Equal(9002u, selected.RawItemId);
    }

    [Fact]
    public void FindNextItemReturnsNullWhenNothingFits()
    {
        var selected = MigrationBatchPlanner.FindNextItem(
            new[] { CreateItem("Outfit", 3) },
            2);

        Assert.Null(selected);
    }

    [Fact]
    public void FindNextItemRejectsEmptyEntriesAndNonPositiveCapacity()
    {
        // Arrange
        var entries = new[] { CreateItem("Empty", 0), CreateItem("Hat", 1) };

        // Act
        var selected = MigrationBatchPlanner.FindNextItem(entries, 1);
        var none = MigrationBatchPlanner.FindNextItem(entries, 0);

        // Assert
        Assert.Equal("Hat", selected?.Name);
        Assert.Null(none);
    }

    private static EligibleDresserItem CreateItem(string name, int componentCount)
    {
        var components = Enumerable.Range(0, componentCount)
            .Select(index => new EligibleDresserComponent(
                index,
                (uint)(1000 + index),
                false,
                (uint)(2000 + index),
                $"Piece {index + 1}"))
            .ToArray();

        return new EligibleDresserItem(0, 1000, name, components);
    }
}
