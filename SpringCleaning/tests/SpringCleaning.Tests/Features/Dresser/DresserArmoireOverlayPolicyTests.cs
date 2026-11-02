namespace SpringCleaning.Tests;

public sealed class DresserArmoireOverlayPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void EmbeddedViewRequiresAnOpenDresserAndSelectedTab(
        bool dresserOpen,
        bool requested,
        bool expected)
    {
        // Arrange

        // Act
        var visible = DresserArmoireOverlayPolicy.ShouldOpen(dresserOpen, requested);

        // Assert
        Assert.Equal(expected, visible);
    }

    [Theory]
    [InlineData(false, 200f, 300f, 100f, 150f)]
    [InlineData(true, null, 300f, 100f, 150f)]
    [InlineData(true, 200f, null, 100f, 150f)]
    [InlineData(true, 200f, 300f, 200f, 300f)]
    public void InitialPositionOnlyUsesCompleteRememberedPlacement(
        bool rememberPlacement,
        float? savedX,
        float? savedY,
        float expectedX,
        float expectedY)
    {
        // Arrange
        var defaultPosition = new System.Numerics.Vector2(100f, 150f);

        // Act
        var position = DresserArmoireOverlayPolicy.GetInitialPosition(
            defaultPosition,
            rememberPlacement,
            savedX,
            savedY);

        // Assert
        Assert.Equal(new System.Numerics.Vector2(expectedX, expectedY), position);
    }
}
