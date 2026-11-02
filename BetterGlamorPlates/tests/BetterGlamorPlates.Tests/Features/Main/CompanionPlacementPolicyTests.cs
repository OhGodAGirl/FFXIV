using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Verifies native overlay anchoring and viewport containment.</summary>
public sealed class CompanionPlacementPolicyTests
{
    /// <summary>Keeps the preview unobscured when the panel fits on the right.</summary>
    [Fact]
    public void PreservesPreviewOnLeft()
    {
        var placement = CompanionPlacementPolicy.Calculate(new(20, 50, 580, 860), new(0, 0, 1920, 1080));
        Assert.NotNull(placement);
        Assert.Equal(604, placement.Value.Position.X);
        Assert.Equal(50, placement.Value.Position.Y);
        Assert.Equal(1080, placement.Value.Size.X);
        Assert.Equal(1, placement.Value.Scale);
    }

    /// <summary>Uses the larger free side rather than covering the preview.</summary>
    [Fact]
    public void UsesLeftWhenItHasMoreRoom()
    {
        var placement = CompanionPlacementPolicy.Calculate(new(1200, 50, 580, 860), new(0, 0, 2650, 1080));
        Assert.NotNull(placement);
        Assert.Equal(116, placement.Value.Position.X);
    }

    /// <summary>Small and offset viewports retain complete bounded geometry.</summary>
    [Theory]
    [InlineData(0, 0, 800, 600)]
    [InlineData(-1920, -200, 1920, 1080)]
    [InlineData(100, 100, 400, 900)]
    public void ClampsWholeSurface(float x, float y, float width, float height)
    {
        var placement = CompanionPlacementPolicy.Calculate(new(300, 800, 600, 1200), new(x, y, width, height));
        Assert.NotNull(placement);
        var value = placement.Value;
        Assert.InRange(value.Position.X, x + 4, x + width - 4 - value.Size.X);
        Assert.InRange(value.Position.Y, y + 4, y + height - 4 - value.Size.Y);
    }

    /// <summary>A narrow right region scales the whole panel without hiding the preview.</summary>
    [Fact]
    public void ShrinksToAvailableSideSpace()
    {
        var native = new WindowBounds(40, 50, 800, 1200);
        var placement = CompanionPlacementPolicy.Calculate(native, new(0, 0, 1952, 1274));
        Assert.NotNull(placement);
        Assert.True(placement.Value.Position.X >= native.X + native.Width);
        Assert.True(placement.Value.Position.X + placement.Value.Size.X <= 1948);
        Assert.True(placement.Value.Size.X < 972 * (1200f / 858));
    }

    /// <summary>No available side space never falls back over the native preview.</summary>
    [Fact]
    public void RejectsFullyOccupiedViewport()
        => Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, 800, 600), new(0, 0, 800, 600)));
    /// <summary>Invalid native/viewport data never reaches ImGui placement.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-10)]
    public void RejectsInvalidDimensions(float width)
        => Assert.Null(CompanionPlacementPolicy.Calculate(new(0, 0, width, 858), new(0, 0, 1920, 1080)));
}



