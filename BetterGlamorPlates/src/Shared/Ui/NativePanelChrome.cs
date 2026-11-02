using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterGlamorPlates.Shared.Ui;

/// <summary>Draws the small geometric markers used by the native-style panels.</summary>
internal static class NativePanelChrome
{
    internal static void DrawDiamond(
        ImDrawListPtr drawList,
        Vector2 center,
        float radius,
        Vector4 color)
    {
        var packed = ImGui.GetColorU32(color);
        var top = center - new Vector2(0f, radius);
        var right = center + new Vector2(radius, 0f);
        var bottom = center + new Vector2(0f, radius);
        var left = center - new Vector2(radius, 0f);
        drawList.AddTriangleFilled(top, right, bottom, packed);
        drawList.AddTriangleFilled(top, bottom, left, packed);
    }

    internal static void DrawSourceGlyph(
        ImDrawListPtr drawList,
        CollectionSourceGlyph glyph,
        Vector2 center,
        Vector4 color)
    {
        if (glyph == CollectionSourceGlyph.Dresser)
            drawList.AddCircleFilled(center, 4f, ImGui.GetColorU32(color));
        else
            DrawDiamond(drawList, center, 4.5f, color);
    }
}

internal enum CollectionSourceGlyph
{
    Dresser,
    Armoire,
}
