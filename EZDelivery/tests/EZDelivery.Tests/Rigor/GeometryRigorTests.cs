using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class GeometryRigorTests
{
    [Fact]
    public void GeneratedVisibleControlsRemainInsideViewportAndOutsideNativeActions()
    {
        var random = new Random(20260916);
        float[] scales = [.5f, 1, 1.25f, 1.5f, 2, 4];
        for (var sample = 0; sample < 500; sample++)
        {
            var viewport = new UiRect(random.Next(-1920, 1921), random.Next(-1080, 1081), random.Next(320, 3841), random.Next(240, 2161));
            var native = new NativeAnchor(new(viewport.X + random.Next(-400, (int)viewport.Width + 400),
                viewport.Y + random.Next(-300, (int)viewport.Height + 300), random.Next(200, 1201), random.Next(200, 901)), true);
            var scale = scales[sample % scales.Length];
            var shop = ContextLayout.Shop(native, viewport, scale, true);
            foreach (var bookmark in shop.Bookmarks) AssertPlacement(bookmark, viewport, native.Bounds);
            if (shop.Browser is { } browser) AssertPlacement(browser, viewport, native.Bounds);
            if (ContextLayout.Delivery(native, viewport, scale) is { } delivery) AssertPlacement(delivery, viewport, native.Bounds);
        }
    }

    private static void AssertPlacement(UiRect control, UiRect viewport, UiRect native)
    {
        Assert.True(control.IsValid);
        Assert.True(control.X >= viewport.X && control.Y >= viewport.Y && control.Right <= viewport.Right && control.Bottom <= viewport.Bottom);
        Assert.False(control.Intersects(native));
    }
}
