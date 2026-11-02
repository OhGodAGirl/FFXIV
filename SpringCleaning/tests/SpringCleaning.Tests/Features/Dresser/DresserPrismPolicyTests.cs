namespace SpringCleaning.Tests;

public sealed class DresserPrismPolicyTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(262, true)]
    public void PreflightTreatsZeroAsUnavailableUntilNativeDialogIsPopulated(
        uint held,
        bool expected)
    {
        // Arrange

        // Act
        var reliable = DresserPrismPolicy.IsReliablePreflightCount(held);

        // Assert
        Assert.Equal(expected, reliable);
    }

    [Fact]
    public void ResolveHeldUsesTrackedCurrencyWhenPrismsAreNotInInventoryBags()
    {
        // Arrange
        const uint inventoryCount = 0;
        const uint trackedCurrencyCount = 262;

        // Act
        var held = DresserPrismPolicy.ResolveHeld(inventoryCount, trackedCurrencyCount);

        // Assert
        Assert.Equal(262u, held);
    }

    [Fact]
    public void ResolveHeldKeepsInventoryFallbackForOlderStorageBehavior()
    {
        // Arrange
        const uint inventoryCount = 12;
        const uint trackedCurrencyCount = 0;

        // Act
        var held = DresserPrismPolicy.ResolveHeld(inventoryCount, trackedCurrencyCount);

        // Assert
        Assert.Equal(12u, held);
    }

    [Fact]
    public void ResolveHeldPrefersTheNativeGlamourCreationBalance()
    {
        // Arrange
        const uint inventoryCount = 0;
        const uint trackedCurrencyCount = 0;
        const uint nativeGlamourCreationCount = 262;

        // Act
        var held = DresserPrismPolicy.ResolveHeld(
            inventoryCount,
            trackedCurrencyCount,
            nativeGlamourCreationCount);

        // Assert
        Assert.Equal(262u, held);
    }

    [Fact]
    public void EvaluateReportsEnoughWhenHeldMeetsRequired()
    {
        // Arrange
        const uint held = 12;
        const int required = 12;

        // Act
        var result = DresserPrismPolicy.Evaluate(held, required, isReady: true);

        // Assert
        Assert.True(result.HasEnough);
        Assert.Equal(0, result.Missing);
    }

    [Fact]
    public void EvaluateReportsExactShortageWithoutUnderflow()
    {
        // Arrange
        const uint held = 3;
        const int required = 8;

        // Act
        var result = DresserPrismPolicy.Evaluate(held, required, isReady: true);

        // Assert
        Assert.False(result.HasEnough);
        Assert.Equal(5, result.Missing);
    }

    [Fact]
    public void EvaluateKeepsPendingScanFromStartingStorage()
    {
        // Arrange
        const uint held = 999;

        // Act
        var result = DresserPrismPolicy.Evaluate(held, required: 1, isReady: false);

        // Assert
        Assert.False(result.HasEnough);
        Assert.Equal(0, result.Missing);
    }

    [Fact]
    public void PendingScanSuppressesARealShortageUntilTheCountIsReady()
    {
        var result = DresserPrismPolicy.Evaluate(held: 0, required: 5, isReady: false);

        Assert.False(result.HasEnough);
        Assert.Equal(0, result.Missing);
    }

    [Theory]
    [InlineData(1, 0, true, "IDLE")]
    [InlineData(8, 1, true, "READY · USES 1")]
    [InlineData(3, 8, true, "NEED 5 PRISMS")]
    [InlineData(2, 3, true, "NEED 1 PRISM")]
    [InlineData(5, 2, false, "CHECKING")]
    public void FormatBannerShowsOnlyActionablePrismInformation(
        uint held,
        int required,
        bool ready,
        string expected)
    {
        // Arrange
        var availability = DresserPrismPolicy.Evaluate(held, required, ready);

        // Act
        var banner = DresserPrismPolicy.FormatBanner(availability);

        // Assert
        Assert.Equal(expected, banner);
    }

    [Fact]
    public void EvaluateRejectsNegativeRequiredCount()
    {
        // Arrange
        const int required = -1;

        // Act
        Action action = () => _ = DresserPrismPolicy.Evaluate(0, required, isReady: true);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Theory]
    [InlineData(0, 0, "WORKING")]
    [InlineData(3, 8, "STORING · 3/8")]
    public void FormatProgressKeepsActiveBannerCompact(int stored, int total, string expected)
    {
        // Arrange

        // Act
        var banner = DresserPrismPolicy.FormatProgress(stored, total);

        // Assert
        Assert.Equal(expected, banner);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void FormatProgressRejectsNegativeCounts(int stored, int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DresserPrismPolicy.FormatProgress(stored, total));
    }
}
