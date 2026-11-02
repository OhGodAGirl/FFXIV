using System.Numerics;

namespace EZDelivery.Features.Interface;

/// <summary>Drawing boundary: geometry is managed; the host supplies the actual canvas.</summary>
public interface ITabCanvas
{
    /// <summary>Fills a rectangle with optional corner rounding.</summary>
    void FillRect(Vector2 min, Vector2 max, uint color, float rounding = 0, bool leftCornersOnly = false);
    /// <summary>Outlines a rectangle with optional rounding restricted to its left corners.</summary>
    void OutlineRect(Vector2 min, Vector2 max, uint color, float rounding, bool leftCornersOnly = false);
    /// <summary>Fills a circle using the specified segment count.</summary>
    void FillCircle(Vector2 center, float radius, uint color, int segments);
    /// <summary>Fills the triangle defined by three points.</summary>
    void FillTriangle(Vector2 first, Vector2 second, Vector2 third, uint color);
    /// <summary>Draws a line with explicit color and thickness.</summary>
    void Line(Vector2 first, Vector2 second, uint color, float thickness);
    /// <summary>Restricts subsequent drawing to a clipping rectangle.</summary>
    void PushClip(Vector2 min, Vector2 max);
    /// <summary>Appends a point to the current path.</summary>
    void PathPoint(Vector2 point);
    /// <summary>Appends an arc to the current path; angles are in radians.</summary>
    void PathArc(Vector2 center, float radius, float start, float end, int segments);
    /// <summary>Strokes the current path without closing its endpoints.</summary>
    void StrokeOpenPath(uint color, float thickness);
    /// <summary>Restores the previous clipping rectangle.</summary>
    void PopClip();
}

/// <summary>Shared style policy and shape geometry for delivery and shop bookmarks.</summary>
public static class TabPresentation
{
    /// <summary>Checks whether the stored tab style is defined.</summary>
    public static bool IsSupported(ShopTabStyle style) => Enum.IsDefined(style);

    /// <summary>Replaces an unknown tab style with Inset Sockets.</summary>
    public static ShopTabStyle Normalize(ShopTabStyle style) => IsSupported(style) ? style : ShopTabStyle.InsetSockets;

    /// <summary>Returns the player-facing name of a normalized tab style.</summary>
    public static string Name(ShopTabStyle style) => Normalize(style) switch
    {
        ShopTabStyle.FlushIndex => "Flush Index",
        ShopTabStyle.RibbonTabs => "Ribbon Tabs",
        ShopTabStyle.RoundTabs => "Round Tabs",
        ShopTabStyle.BeveledIndex => "Beveled Index",
        _ => "Inset Sockets",
    };

    // The caller supplies the valid, scaled bookmark bounds. A struct canvas avoids per-frame allocations.
    /// <summary>Draws one supported tab shape through the supplied canvas.</summary>
    public static void Draw<TCanvas>(ref TCanvas draw, Vector2 min, Vector2 max, float scale,
        ShopTabStyle style, uint fill, uint border, uint insetColor) where TCanvas : ITabCanvas
    {
        var center = (min.Y + max.Y) / 2;
        switch (style)
        {
            default:
                draw.FillRect(min, max, insetColor, 5 * scale);
                draw.OutlineRect(min, max, border, 5 * scale);
                var inset = new Vector2(3 * scale);
                draw.FillRect(min + inset, max - inset, fill, 3 * scale);
                draw.OutlineRect(min + inset, max - inset, border, 3 * scale);
                break;
            case ShopTabStyle.RoundTabs:
                var round = (max.Y - min.Y) / 2;
                draw.FillCircle(new(min.X + round, center), round, fill, 24);
                draw.FillRect(new(min.X + round, min.Y), max, fill);
                // Include horizontal strokes at the window edges rather than clipping to its inner content.
                draw.PushClip(min - Vector2.One, max + Vector2.One);
                draw.PathPoint(new(max.X, max.Y));
                draw.PathArc(new(min.X + round, center), round, MathF.PI / 2, MathF.PI * 1.5f, 16);
                draw.PathPoint(new(max.X, min.Y));
                draw.StrokeOpenPath(border, 1.5f * scale);
                draw.PopClip();
                break;
            case ShopTabStyle.RibbonTabs:
                var cut = min.X + 5 * scale;
                draw.FillRect(new(cut, min.Y), max, fill);
                draw.FillTriangle(min, new(cut, min.Y), new(cut, center), fill);
                draw.FillTriangle(new(min.X, max.Y), new(cut, center), new(cut, max.Y), fill);
                draw.Line(min, new(max.X, min.Y), border, scale);
                draw.Line(new(min.X, max.Y), max, border, scale);
                draw.Line(min, new(cut, center), border, scale);
                draw.Line(new(cut, center), new(min.X, max.Y), border, scale);
                break;
            case ShopTabStyle.BeveledIndex:
                var bevel = 5 * scale;
                draw.FillRect(new(min.X + bevel, min.Y), max, fill);
                draw.FillRect(new(min.X, min.Y + bevel), new(min.X + bevel, max.Y - bevel), fill);
                draw.FillTriangle(new(min.X + bevel, min.Y), new(min.X + bevel, min.Y + bevel), new(min.X, min.Y + bevel), fill);
                draw.FillTriangle(new(min.X, max.Y - bevel), new(min.X + bevel, max.Y - bevel), new(min.X + bevel, max.Y), fill);
                draw.Line(new(min.X + bevel, min.Y), new(max.X, min.Y), border, scale);
                draw.Line(new(min.X + bevel, max.Y), max, border, scale);
                draw.Line(new(min.X + bevel, min.Y), new(min.X, min.Y + bevel), border, scale);
                draw.Line(new(min.X, min.Y + bevel), new(min.X, max.Y - bevel), border, scale);
                draw.Line(new(min.X, max.Y - bevel), new(min.X + bevel, max.Y), border, scale);
                break;
            case ShopTabStyle.FlushIndex:
                draw.FillRect(min, max, fill, 3 * scale, true);
                draw.OutlineRect(min, max, border, 3 * scale, true);
                // Paint over the inner border to leave the native-window seam open.
                draw.Line(new(max.X, min.Y + scale), new(max.X, max.Y - scale), fill, 2 * scale);
                break;
        }
    }
}
