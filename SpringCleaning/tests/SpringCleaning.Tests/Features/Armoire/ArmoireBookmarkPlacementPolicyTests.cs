namespace SpringCleaning.Tests;

public sealed class ArmoireBookmarkPlacementPolicyTests
{
    [Fact]
    public void TabsEndOutsideTheArmoireFrame()
    {
        // Arrange
        const float armoireLeft = 136f;
        const float totalTabWidth = 111f;
        const float frameGap = 3f;

        // Act
        var left = ArmoireBookmarkPlacementPolicy.GetLeft(
            armoireLeft,
            totalTabWidth,
            frameGap);

        // Assert
        Assert.Equal(22f, left);
        Assert.Equal(armoireLeft - frameGap, left + totalTabWidth);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, -1f)]
    public void NegativeDimensionsAreRejected(float totalTabWidth, float frameGap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ArmoireBookmarkPlacementPolicy.GetLeft(100f, totalTabWidth, frameGap));
    }
}
