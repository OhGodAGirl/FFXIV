using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Examine;

/// <summary>Save overlay occupies the native footer without an external tab or off-screen clamping.</summary>
public sealed class ExamineTabPlacementTests
{
    /// <summary>Only overlapping native surfaces in front of Examine suppress the save control.</summary>
    [Theory]
    [InlineData(2u, 0, true)]
    [InlineData(1u, 2, true)]
    [InlineData(1u, 3, false)]
    [InlineData(1u, 4, false)]
    [InlineData(0u, 0, false)]
    public void FollowsNativeLayerOrder(uint layer, int index, bool covered)
    {
        var icon = new WindowBounds(100, 800, 64, 64);
        Assert.Equal(covered, ExamineTabPlacement.IsCovered(1, 3, layer, index,
            icon, new(50, 700, 400, 250)));
    }

    /// <summary>Tooltips elsewhere or merely touching an edge do not hide the icon.</summary>
    [Theory]
    [InlineData(164, 800, 40, 40, false)]
    [InlineData(60, 800, 40, 40, false)]
    [InlineData(100, 864, 40, 40, false)]
    [InlineData(100, 760, 40, 40, false)]
    [InlineData(163, 800, 40, 40, true)]
    [InlineData(100, 863, 40, 40, true)]
    [InlineData(100, 800, 0, 40, false)]
    public void RequiresAreaOverlap(float x, float y, float width, float height, bool covered)
        => Assert.Equal(covered, ExamineTabPlacement.IsCovered(1, 3, 2, 0,
            new(100, 800, 64, 64), new(x, y, width, height)));

    private static readonly WindowBounds Screen = new(0, 0, 1920, 1080);

    /// <summary>The save icon occupies the left equipment column on the soul crystal row.</summary>
    [Fact]
    public void SitsInsideNativeFooter()
    {
        var bounds = new WindowBounds(120, 50, 620, 960);
        var position = ExamineTabPlacement.Calculate(bounds, Screen)!.Value;
        Assert.Equal(167f, position.X);
        Assert.Equal(874f, position.Y);
        Assert.Equal(new System.Numerics.Vector2(64), ExamineTabPlacement.ButtonSize(bounds));
        Assert.True(position.Y + ExamineTabPlacement.ButtonSize(bounds).Y < bounds.Y + bounds.Height);
    }

    /// <summary>Placement and size follow native UI scaling rather than the separate Dalamud scale.</summary>
    [Fact]
    public void FollowsNativeScale()
    {
        var bounds = new WindowBounds(0, 0, 1240, 1920);
        var position = ExamineTabPlacement.Calculate(bounds, new(0, 0, 3840, 2160))!.Value;
        Assert.Equal(94f, position.X);
        Assert.Equal(1648f, position.Y);
        Assert.Equal(new System.Numerics.Vector2(128), ExamineTabPlacement.ButtonSize(bounds));
    }

    /// <summary>No footer space outside the viewport means no displaced icon over another control.</summary>
    [Fact]
    public void HidesWhenFooterIsOffScreen()
        => Assert.Null(ExamineTabPlacement.Calculate(new(120, 1000, 620, 960), Screen));

    /// <summary>Attachment stays inside even when Examine fills the available width.</summary>
    [Fact]
    public void DoesNotNeedExternalSideSpace()
        => Assert.NotNull(ExamineTabPlacement.Calculate(new(0, 0, 1920, 1080), Screen));

    /// <summary>Invalid geometry never reaches ImGui.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidGeometry(float width)
        => Assert.Null(ExamineTabPlacement.Calculate(new(100, 100, width, 900), Screen));
}

