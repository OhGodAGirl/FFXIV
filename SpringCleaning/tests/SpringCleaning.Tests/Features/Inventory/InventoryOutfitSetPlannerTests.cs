namespace SpringCleaning.Tests;

public sealed class InventoryOutfitSetPlannerTests
{
    [Fact]
    public void BatchQueueOrderIsStableAcrossInputOrderAndQuality()
    {
        var piece = new InventoryDresserComponent(0, 7, 0, 100, false, "Piece", 0);
        var first = InventoryOutfitSetPlanner.BuildBatches(
            [new(piece, [901]), new(piece with { ItemId = 101, IsHighQuality = true }, [900]),
                new(piece with { ItemId = 102 }, [900])], []);
        var reversed = InventoryOutfitSetPlanner.BuildBatches(
            [new(piece with { ItemId = 102 }, [900]),
                new(piece with { ItemId = 101, IsHighQuality = true }, [900]), new(piece, [901])], []);
        Assert.Equal(new[] { (900u, false), (900u, true), (901u, false) },
            first.Select(batch => (batch.SetId, batch.IsHighQuality)));
        Assert.Equal(first.Select(batch => (batch.SetId, batch.IsHighQuality)),
            reversed.Select(batch => (batch.SetId, batch.IsHighQuality)));
    }

    [Fact]
    public void LeatherworkerPiecesAreSeparatedByQualityWithoutLosingPhysicalCopies()
    {
        var hat = new InventoryDresserComponent(0, 7, 0, 2924, true, "Hat", 0);
        var shirt = new InventoryDresserComponent(0, 12, 1, 3233, false, "Shirt", 0);
        var batches = InventoryOutfitSetPlanner.BuildBatches(
            [new(hat, [900]), new(shirt, [900])], [(900, true), (900, false)]);
        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch => Assert.Equal(900u, batch.SetId));
        Assert.Equal(shirt, Assert.Single(batches[0].Components));
        Assert.False(batches[0].IsHighQuality);
        Assert.Equal(hat, Assert.Single(batches[1].Components));
        Assert.True(batches[1].IsHighQuality);
        var mixed = new InventoryDresserOutfit(900, null, "Leatherworker", [hat, shirt]);
        Assert.False(OutfitPieceSelectionPolicy.Matches(mixed, [new(2924, 0, 7), new(3233, 0, 12)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetPreferenceUsesOnlyExistingOutfitsOfTheSelectedQuality(bool quality)
    {
        var piece = new InventoryDresserComponent(0, 7, 0, 100, quality, "Piece", 0);
        var batches = InventoryOutfitSetPlanner.BuildBatches(
            [new(piece, [900, 901]), new(piece with { SlotIndex = 8 }, [900]),
                new(piece with { ItemId = 200 }, [])], [(900, !quality), (901, quality)]);
        var batch = Assert.Single(batches);
        Assert.Equal(901u, batch.SetId);
        Assert.Equal(piece, Assert.Single(batch.Components));
    }

    [Fact]
    public void CompleteDuplicateIsPreservedWhenSkippingIsDisabled()
    {
        Assert.False(InventoryOutfitSetPlanner.ShouldSkipCompleteDuplicate(
            skipDuplicateOutfits: false,
            outfitAlreadyExists: true,
            candidatePieceCount: 5,
            completeOutfitPieceCount: 5));
    }

    [Theory]
    [InlineData(false, 5, 5)]
    [InlineData(true, 3, 5)]
    [InlineData(true, 5, 0)]
    public void PartialNewOrInvalidOutfitIsNeverSkipped(
        bool outfitAlreadyExists,
        int candidatePieces,
        int completePieces)
    {
        Assert.False(InventoryOutfitSetPlanner.ShouldSkipCompleteDuplicate(
            skipDuplicateOutfits: true,
            outfitAlreadyExists,
            candidatePieces,
            completePieces));
    }

    [Fact]
    public void CompleteExistingOutfitIsSkippedWhenEnabled()
    {
        Assert.True(InventoryOutfitSetPlanner.ShouldSkipCompleteDuplicate(
            skipDuplicateOutfits: true,
            outfitAlreadyExists: true,
            candidatePieceCount: 6,
            completeOutfitPieceCount: 5));
    }

    [Fact]
    public void ChooseSetPrefersAnExistingOutfitOverALargerNewCombination()
    {
        var selected = InventoryOutfitSetPlanner.ChooseSet(
            [100u, 200u],
            new Dictionary<uint, int> { [100] = 5, [200] = 1 },
            new HashSet<uint> { 200 });

        Assert.Equal(200u, selected);
    }

    [Fact]
    public void ChooseSetUsesMembershipThenStableIdWhenStorageStateMatches()
    {
        var selected = InventoryOutfitSetPlanner.ChooseSet(
            [300u, 100u, 200u],
            new Dictionary<uint, int> { [100] = 2, [200] = 3, [300] = 3 },
            new HashSet<uint>());

        Assert.Equal(200u, selected);
    }

    [Fact]
    public void ChooseSetRejectsAnEmptyCandidateSet()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryOutfitSetPlanner.ChooseSet(
            Array.Empty<uint>(),
            new Dictionary<uint, int>(),
            new HashSet<uint>()));
    }
}
