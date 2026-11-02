namespace EZDelivery.Features.Interface;

/// <summary>Computed bookmark and browser rectangles, including any fallback placement reason.</summary>
public sealed record ShopLayout(IReadOnlyList<UiRect> Bookmarks, UiRect? Browser, bool Floating, string? PlacementReason);

/// <summary>Pure placement in ImGui coordinates; scale applies to plugin logical dimensions only.</summary>
public static class ContextLayout
{
    /// <summary>Returns a finite positive UI scale or the safe default.</summary>
    public static float SafeScale(float scale) => float.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, .5f, 4f) : 1f;

    /// <summary>Extend the bookmark across its safety gap, without entering native controls or leaving the viewport.</summary>
    public static UiRect BridgeToFrame(UiRect bookmark, UiRect viewport, UiRect native, float scale)
    {
        var gap = 2 * SafeScale(scale);
        return bookmark.Right + gap <= viewport.Right && bookmark.Right + gap <= native.X
            ? bookmark with { Width = bookmark.Width + gap }
            : bookmark;
    }

    /// <summary>Only the native origin needs conversion; width/height are already scaled pixels.</summary>
    public static NativeAnchor? ToViewport(NativeAnchor? native, float viewportX, float viewportY)
    {
        if (native is null) return null;
        return native with { Bounds = native.Bounds with { X = native.Bounds.X + viewportX, Y = native.Bounds.Y + viewportY } };
    }

    /// <summary>Places the delivery tab beside the native window inside the viewport.</summary>
    public static UiRect? Delivery(NativeAnchor? anchor, UiRect viewport, float scale)
    {
        if (anchor is not { Ready: true } || !anchor.Bounds.IsValid || !viewport.IsValid) return null;
        scale = SafeScale(scale);
        if (viewport.Width < 48 * scale || viewport.Height < 48 * scale) return null;
        var bookmark = Clamp(new(anchor.Bounds.X - 50 * scale, anchor.Bounds.Y + 42 * scale, 48 * scale, 48 * scale), viewport);
        return bookmark.Intersects(anchor.Bounds) ? null : bookmark;
    }

    /// <summary>Places category tabs and the optional browser, using a viewport fallback when needed.</summary>
    public static ShopLayout Shop(NativeAnchor? anchor, UiRect viewport, float scale, bool open)
    {
        if (anchor is not { Ready: true } || !anchor.Bounds.IsValid || !viewport.IsValid)
            return new(Array.Empty<UiRect>(), null, false, null);
        scale = SafeScale(scale);
        var native = anchor.Bounds;
        var rail = Array.Empty<UiRect>();
        var railHeight = 207 * scale;
        if (native.X - 50 * scale >= viewport.X && viewport.Height >= railHeight)
        {
            var railY = Math.Clamp(native.Y + 8 * scale, viewport.Y, viewport.Bottom - railHeight);
            rail = Enumerable.Range(0, 4).Select(i => new UiRect(native.X - 50 * scale,
                railY + i * 53 * scale, 48 * scale, 48 * scale)).ToArray();
            if (rail.Any(r => r.Right > viewport.Right || r.Intersects(native))) rail = Array.Empty<UiRect>();
        }
        if (!open) return new(rail, null, false, null);
        float minWidth = 360 * scale, minHeight = 300 * scale, gap = 6 * scale;
        float rightX = Math.Max(viewport.X, native.Right + gap);
        float room = viewport.Right - rightX;
        float height = Math.Min(viewport.Height, Math.Max(minHeight, native.Height));
        if (room >= minWidth)
            return new(rail, Clamp(new(rightX, native.Y, Math.Min(640 * scale, room), height), viewport), false, null);

        // A floating fallback can occupy a clear strip above/below the native window. Never place over its controls.
        var width = Math.Min(640 * scale, viewport.Width);
        var aboveRoom = Math.Clamp(native.Y - gap - viewport.Y, 0, viewport.Height);
        var belowY = Math.Max(viewport.Y, native.Bottom + gap);
        var belowRoom = Math.Max(0, viewport.Bottom - belowY);
        if (aboveRoom >= minHeight || belowRoom >= minHeight)
        {
            bool below = belowRoom >= aboveRoom;
            var freeHeight = below ? belowRoom : aboveRoom;
            height = Math.Min(height, freeHeight);
            var candidate = Clamp(new(native.X, below ? belowY : native.Y - gap - height, width, height), viewport);
            if (!candidate.Intersects(native)) return new(rail, candidate, true, null);
        }
        return new(rail, null, true, "Move the native shop left or upward to make room for the item browser.");
    }

    /// <summary>Constrains a rectangle to available viewport bounds.</summary>
    public static UiRect Clamp(UiRect rect, UiRect viewport)
    {
        var width = Math.Min(rect.Width, viewport.Width);
        var height = Math.Min(rect.Height, viewport.Height);
        return new(Math.Clamp(rect.X, viewport.X, viewport.Right - width),
            Math.Clamp(rect.Y, viewport.Y, viewport.Bottom - height), width, height);
    }
}
