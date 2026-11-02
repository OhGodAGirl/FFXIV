namespace SpringCleaning.Tests.Features.Inventory;

public sealed class RestoredCopyPolicyTests
{
    private static InventoryDuplicateItem Copy(int slot, uint id = 42, bool safe = true) =>
        new(0, slot, id, false, "Item", 1, new(true, safe, ""), true, false,
            InventoryDuplicateDisposition.RemovalReview);

    [Fact]
    public void OnlyNewPhysicalCopiesAreSubmitted()
    {
        var old = Copy(1);
        var restored = Copy(2);
        var result = RestoredCopyPolicy.Observe([old], [restored, old], [42]);
        Assert.False(result.Waiting);
        Assert.Null(result.Failure);
        Assert.Equal(restored, Assert.Single(result.Copies));
    }

    [Fact]
    public void EntireOutfitMustArriveBeforeAnyCopyIsSubmitted()
    {
        var result = RestoredCopyPolicy.Observe([], [Copy(1)], [42, 43]);
        Assert.True(result.Waiting);
        Assert.Empty(result.Copies);
    }

    [Fact]
    public void MovingAnExistingCopyStopsInsteadOfMisattributingIt()
    {
        var result = RestoredCopyPolicy.Observe([Copy(1)], [Copy(2)], [42]);
        Assert.False(result.Waiting);
        Assert.Equal("Inventory changed during restoration. Cleanup stopped, kupo!", result.Failure);
        Assert.NotNull(result.Failure);
        Assert.Empty(result.Copies);
    }

    [Theory]
    [InlineData(43, true)]
    [InlineData(42, false)]
    public void WrongOrProtectedCopyIsNeverSubmitted(uint id, bool safe)
    {
        var result = RestoredCopyPolicy.Observe([], [Copy(1, id, safe)], [42]);
        Assert.False(result.Waiting);
        Assert.Equal("Restored copies need review and remain in Inventory, kupo!", result.Failure);
        Assert.NotNull(result.Failure);
        Assert.Empty(result.Copies);
    }

    [Fact]
    public void ExtraUnattributedCopyStopsCleanup()
    {
        var result = RestoredCopyPolicy.Observe([], [Copy(1), Copy(2)], [42]);
        Assert.NotNull(result.Failure);
        Assert.Empty(result.Copies);
    }

    [Fact]
    public void OneProtectedComponentBlocksTheWholeRestoredOutfit()
    {
        var result = RestoredCopyPolicy.Observe([], [Copy(1, 42), Copy(2, 43, false)], [42, 43]);
        Assert.NotNull(result.Failure);
        Assert.False(result.Waiting);
        Assert.Empty(result.Copies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingCopyQualityAndContainerArePartOfIdentity(bool quality)
    {
        var old = quality ? Copy(1) with { IsHighQuality = true }
            : Copy(1) with { InventoryTypeId = 1 };
        var result = RestoredCopyPolicy.Observe([old], [Copy(1), Copy(2)], [42]);
        Assert.NotNull(result.Failure);
        Assert.Empty(result.Copies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompleteOutfitArrivalOrderDoesNotChangeAttribution(bool reverseExpected)
    {
        var expected = reverseExpected ? new uint[] { 43, 42 } : [42, 43];
        var after = reverseExpected ? new[] { Copy(1, 42), Copy(2, 43) } : [Copy(1, 43), Copy(2, 42)];
        var result = RestoredCopyPolicy.Observe([], after, expected);
        Assert.Null(result.Failure);
        Assert.False(result.Waiting);
        Assert.Equal(2, result.Copies.Count);
    }
}
