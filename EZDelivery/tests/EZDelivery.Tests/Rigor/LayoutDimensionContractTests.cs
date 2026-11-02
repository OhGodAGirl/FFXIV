using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class LayoutDimensionContractTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void DeliveryBookmarkFitsExactlySizedViewportWithoutChangingItsDimensions(float scale)
    {
        var size = 48 * scale;
        var viewport = new UiRect(0, 0, size, size);
        var anchor = new NativeAnchor(new(1000, 1000, 100, 100), true);
        Assert.Equal(new UiRect(0, 0, size, size), ContextLayout.Delivery(anchor, viewport, scale));
        Assert.Null(ContextLayout.Delivery(anchor, viewport with { Width = size - 1 }, scale));
        Assert.Null(ContextLayout.Delivery(anchor, viewport with { Height = size - 1 }, scale));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void RailUsesExactScaleOffsetsAndFitsAtBothViewportBoundaries(float scale)
    {
        var viewport = new UiRect(0, 0, 1920, 207 * scale);
        var anchor = new NativeAnchor(new(50 * scale, 0, 600, 600), true);
        var rail = ContextLayout.Shop(anchor, viewport, scale, false).Bookmarks;
        Assert.Equal(4, rail.Count);
        for (var index = 0; index < 4; index++)
            Assert.Equal(new UiRect(0, index * 53 * scale, 48 * scale, 48 * scale), rail[index]);
        var free = ContextLayout.Shop(new(new(300, 100, 600, 600), true), new(0, 0, 1920, 1080), scale, false);
        Assert.Equal(100 + 8 * scale, free.Bookmarks[0].Y);
        Assert.Equal(48 * scale, free.Bookmarks[0].Height);
        var right = ContextLayout.Shop(new(new(1920 + 2 * scale, 100, 100, 100), true), new(0, 0, 1920, 1080), scale, false);
        Assert.Equal(4, right.Bookmarks.Count);
        Assert.Equal(1920, right.Bookmarks[0].Right);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void RightBrowserUsesScaledMinimumMaximumAndNativeHeight(float scale)
    {
        var anchor = new NativeAnchor(new(100, 50, 600, 100), true);
        var exact = ContextLayout.Shop(anchor, new(0, 0, 700 + 6 * scale + 360 * scale, 2000), scale, true);
        Assert.False(exact.Floating);
        Assert.Equal(360 * scale, exact.Browser!.Value.Width);
        Assert.Equal(300 * scale, exact.Browser.Value.Height);
        var ample = ContextLayout.Shop(anchor, new(0, 0, 4000, 2000), scale, true);
        Assert.Equal(640 * scale, ample.Browser!.Value.Width);
        var tooNarrow = ContextLayout.Shop(anchor, new(0, 0, 700 + 6 * scale + 360 * scale - 1, 300 * scale), scale, true);
        Assert.True(tooNarrow.Floating);
        Assert.Null(tooNarrow.Browser);
    }

    [Fact]
    public void FloatingBrowserFitsExactAboveAndBelowSpaceAndPrefersBelowOnTie()
    {
        var above = ContextLayout.Shop(new(new(1100, 506, 800, 400), true), new(0, 200, 1920, 800), 1, true);
        Assert.True(above.Floating);
        Assert.Equal(new UiRect(1100, 200, 640, 300), above.Browser);
        var below = ContextLayout.Shop(new(new(1100, 0, 800, 400), true), new(0, 0, 1920, 706), 1, true);
        Assert.Equal(new UiRect(1100, 406, 640, 300), below.Browser);
        var tie = ContextLayout.Shop(new(new(1100, 406, 800, 300), true), new(0, 0, 1920, 1112), 1, true);
        Assert.Equal(new UiRect(1100, 712, 640, 300), tie.Browser);
    }

    [Fact]
    public void InvalidOrImpossibleShopGeometryHasExplicitPlacementState()
    {
        var absent = ContextLayout.Shop(null, new(0, 0, 1920, 1080), 1, true);
        Assert.False(absent.Floating);
        Assert.Null(absent.PlacementReason);
        var impossible = ContextLayout.Shop(new(new(20, 20, 950, 700), true), new(0, 0, 1000, 750), 1, true);
        Assert.True(impossible.Floating);
        Assert.Equal("Move the native shop left or upward to make room for the item browser.", impossible.PlacementReason);
    }
}
