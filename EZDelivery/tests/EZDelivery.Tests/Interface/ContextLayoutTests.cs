using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class ContextLayoutTests
{
    [Fact]
    public void ViewportOriginConversionPreservesAlreadyScaledNativeSize()
    {
        var native = new NativeAnchor(new(300, 100, 900, 700), true);
        var converted = ContextLayout.ToViewport(native, 1920, -500)!;
        Assert.Equal(new UiRect(2220, -400, 900, 700), converted.Bounds);
        Assert.Equal(new UiRect(300, 100, 900, 700), native.Bounds);
        Assert.Null(ContextLayout.ToViewport(null, 1920, 0));
    }

    [Theory]
    [InlineData(1f)] [InlineData(1.25f)] [InlineData(1.5f)] [InlineData(2f)]
    public void AnchorsRemainInScreenCoordinatesAtAllHostScales(float scale)
    {
        var anchor = new NativeAnchor(new(500, 200, 800, 650), true);
        var delivery = ContextLayout.Delivery(anchor, new(0, 0, 3840, 2160), scale)!.Value;
        Assert.Equal(500 - 50 * scale, delivery.X);
        Assert.Equal(200 + 42 * scale, delivery.Y);
        Assert.Equal(48 * scale, delivery.Width);
        var shop = ContextLayout.Shop(anchor, new(0, 0, 3840, 2160), scale, true);
        Assert.Equal(4, shop.Bookmarks.Count);
        Assert.Equal(1300 + 6 * scale, shop.Browser!.Value.X);
        Assert.False(shop.Browser.Value.Intersects(anchor.Bounds));
    }

    [Fact]
    public void ShrinksRightBrowserBeforeFloatingAndKeepsNativeUntouched()
    {
        var anchor = new NativeAnchor(new(300, 100, 750, 650), true);
        var layout = ContextLayout.Shop(anchor, new(0, 0, 1500, 1000), 1, true);
        Assert.False(layout.Floating);
        Assert.Equal(444, layout.Browser!.Value.Width);
        Assert.Equal(anchor.Bounds.Right + 6, layout.Browser.Value.X);
    }

    [Fact]
    public void RightEdgeFloatsInClearSpaceBelowNativeWindow()
    {
        var native = new NativeAnchor(new(700, 60, 600, 420), true);
        var viewport = new UiRect(0, 0, 1400, 1000);
        var layout = ContextLayout.Shop(native, viewport, 1, true);
        Assert.True(layout.Floating);
        Assert.NotNull(layout.Browser);
        Assert.True(layout.Browser.Value.Y >= native.Bounds.Bottom);
        Assert.True(layout.Browser.Value.Right <= viewport.Right);
        Assert.False(layout.Browser.Value.Intersects(native.Bounds));
    }

    [Fact]
    public void ImpossiblePlacementExplainsWhyInsteadOfCoveringNativeActions()
    {
        var native = new NativeAnchor(new(20, 20, 950, 700), true);
        var layout = ContextLayout.Shop(native, new(0, 0, 1000, 750), 1, true);
        Assert.Null(layout.Browser);
        Assert.NotNull(layout.PlacementReason);
        Assert.Empty(layout.Bookmarks);
    }

    [Fact]
    public void InvalidOrUnavailableGeometryCannotCreateUi()
    {
        var viewport = new UiRect(0, 0, 1920, 1080);
        Assert.Null(ContextLayout.Delivery(new(new(float.NaN, 0, 100, 100), true), viewport, 1));
        Assert.Empty(ContextLayout.Shop(new(new(1, 1, 100, 100), false), viewport, 1, true).Bookmarks);
        Assert.Null(ContextLayout.Delivery(null, viewport, 1));
        Assert.Equal(1, ContextLayout.SafeScale(float.NaN));
    }

    [Fact]
    public void NativeOpeningShowsOnlyRailUntilExplicitCategoryChoice()
    {
        var layout = ContextLayout.Shop(new(new(200, 200, 600, 600), true), new(0, 0, 1920, 1080), 1, false);
        Assert.Equal(4, layout.Bookmarks.Count);
        Assert.Null(layout.Browser);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(-300, 3000)]
    public void LeftEdgeOrOversizedNativeWindowNeverGetsOverlappingTabs(float x, float width)
    {
        var anchor = new NativeAnchor(new(x, 20, width, 700), true);
        var viewport = new UiRect(0, 0, 1920, 1080);
        Assert.Null(ContextLayout.Delivery(anchor, viewport, 1));
        var shop = ContextLayout.Shop(anchor, viewport, 1, true);
        Assert.Empty(shop.Bookmarks);
        if (shop.Browser is { } browser) Assert.False(browser.Intersects(anchor.Bounds));
    }

    [Fact]
    public void RailClampsAsOneGroupAndDoesNotStackBookmarksAtBottomEdge()
    {
        var layout = ContextLayout.Shop(new(new(200, 1030, 600, 600), true), new(0, 0, 1920, 1080), 1, false);
        Assert.Equal(4, layout.Bookmarks.Count);
        Assert.Equal(1080, layout.Bookmarks[^1].Bottom);
        for (var i = 1; i < layout.Bookmarks.Count; i++)
            Assert.Equal(5, layout.Bookmarks[i].Y - layout.Bookmarks[i - 1].Bottom);
    }

    [Theory]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(float.NegativeInfinity, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(.1f, .5f)]
    [InlineData(8, 4)]
    public void InvalidOrExtremeScaleUsesFiniteBoundedValue(float input, float expected)
        => Assert.Equal(expected, ContextLayout.SafeScale(input));

    [Fact]
    public void TinyViewportHidesControlsThatCannotFit()
    {
        var native = new NativeAnchor(new(100, 100, 600, 600), true);
        Assert.Null(ContextLayout.Delivery(native, new(0, 0, 47, 1080), 1));
        Assert.Null(ContextLayout.Delivery(native, new(0, 0, 1920, 47), 1));
        Assert.Empty(ContextLayout.Shop(native, new(0, 0, 1920, 206), 1, false).Bookmarks);
    }

    [Fact]
    public void RightEdgeCanFloatAboveNativeWindowWhenBelowIsBlocked()
    {
        var native = new NativeAnchor(new(700, 600, 600, 350), true);
        var viewport = new UiRect(0, 0, 1400, 1000);
        var layout = ContextLayout.Shop(native, viewport, 1, true);
        Assert.True(layout.Floating);
        var browser = Assert.IsType<UiRect>(layout.Browser);
        Assert.True(browser.Bottom < native.Bounds.Y);
        Assert.False(browser.Intersects(native.Bounds));
        Assert.True(browser.X >= viewport.X && browser.Right <= viewport.Right);
    }

    [Fact]
    public void InvalidBoundsIncludingOverflowNeverCreateContextControls()
    {
        var viewport = new UiRect(0, 0, 1920, 1080);
        foreach (var invalid in new[] { new UiRect(0, 0, 0, 100), new UiRect(0, 0, 100, -1),
            new UiRect(float.MaxValue, 0, float.MaxValue, 100), new UiRect(0, float.NaN, 100, 100) })
        {
            Assert.False(invalid.IsValid);
            Assert.Null(ContextLayout.Delivery(new(invalid, true), viewport, 1));
            Assert.Empty(ContextLayout.Shop(new(invalid, true), viewport, 1, true).Bookmarks);
            Assert.Null(ContextLayout.Delivery(new(viewport, true), invalid, 1));
        }
        Assert.Empty(ContextLayout.Shop(null, viewport, 1, true).Bookmarks);
    }

    [Fact]
    public void NativeWindowBeyondRightViewportEdgeDoesNotLeaveOffscreenBookmarks()
    {
        var viewport = new UiRect(0, 0, 1920, 1080);
        var native = new NativeAnchor(new(2000, 200, 600, 600), true);
        var layout = ContextLayout.Shop(native, viewport, 1, false);
        Assert.Empty(layout.Bookmarks);
        Assert.Null(layout.Browser);
        Assert.False(layout.Floating);
    }
}
