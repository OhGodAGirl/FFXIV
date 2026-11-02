using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SpringCleaning;

internal static class SpringCleaningUi
{
    internal static void DrawBookmarkTab(ImDrawListPtr draw, Vector2 min, Vector2 max, uint fill, uint border, float scale, BookmarkTabStyle style, Vector4? insetColor = null)
    {
        var radius = 5 * scale;
        var center = (min.Y + max.Y) / 2;
        switch (style)
        {
            case BookmarkTabStyle.InsetSockets:
                draw.AddRectFilled(min, max, ImGui.GetColorU32(insetColor ?? new Vector4(.12f, .14f, .15f, 1)), radius);
                draw.AddRect(min, max, border, radius);
                var inset = new Vector2(3 * scale);
                draw.AddRectFilled(min + inset, max - inset, fill, 3 * scale);
                draw.AddRect(min + inset, max - inset, border, 3 * scale);
                break;
            default:
            case BookmarkTabStyle.RoundTabs:
                var round = (max.Y - min.Y) / 2;
                draw.AddCircleFilled(new(min.X + round, center), round, fill, 24);
                draw.AddRectFilled(new(min.X + round, min.Y), max, fill);
                // Retain the caller's native-window occlusion clip.
                draw.PushClipRect(min - Vector2.One, max + Vector2.One, true);
                draw.PathLineTo(new(max.X, max.Y));
                draw.PathArcTo(new(min.X + round, center), round, MathF.PI / 2, MathF.PI * 1.5f, 16);
                draw.PathLineTo(new(max.X, min.Y));
                draw.PathStroke(border, ImDrawFlags.None, 1.5f * scale);
                draw.PopClipRect();
                break;
            case BookmarkTabStyle.RibbonTabs:
                var cut = min.X + 5 * scale;
                draw.AddRectFilled(new(cut, min.Y), max, fill);
                draw.AddTriangleFilled(min, new(cut, min.Y), new(cut, center), fill);
                draw.AddTriangleFilled(new(min.X, max.Y), new(cut, center), new(cut, max.Y), fill);
                draw.AddLine(min, new(max.X, min.Y), border, scale);
                draw.AddLine(new(min.X, max.Y), max, border, scale);
                draw.AddLine(min, new(cut, center), border, scale);
                draw.AddLine(new(cut, center), new(min.X, max.Y), border, scale);
                break;
            case BookmarkTabStyle.BeveledIndex:
                var bevel = 5 * scale;
                draw.AddRectFilled(new(min.X + bevel, min.Y), max, fill);
                draw.AddRectFilled(new(min.X, min.Y + bevel), new(min.X + bevel, max.Y - bevel), fill);
                draw.AddTriangleFilled(new(min.X + bevel, min.Y), new(min.X + bevel, min.Y + bevel), new(min.X, min.Y + bevel), fill);
                draw.AddTriangleFilled(new(min.X, max.Y - bevel), new(min.X + bevel, max.Y - bevel), new(min.X + bevel, max.Y), fill);
                draw.AddLine(new(min.X + bevel, min.Y), new(max.X, min.Y), border, scale);
                draw.AddLine(new(min.X + bevel, max.Y), max, border, scale);
                draw.AddLine(new(min.X + bevel, min.Y), new(min.X, min.Y + bevel), border, scale);
                draw.AddLine(new(min.X, min.Y + bevel), new(min.X, max.Y - bevel), border, scale);
                draw.AddLine(new(min.X, max.Y - bevel), new(min.X + bevel, max.Y), border, scale);
                break;
            case BookmarkTabStyle.FlushIndex:
                draw.AddRectFilled(min, max, fill, 3 * scale, ImDrawFlags.RoundCornersLeft);
                draw.AddRect(min, max, border, 3 * scale, ImDrawFlags.RoundCornersLeft);
                // Open the inner seam so the tab reads as an index attached to the frame.
                draw.AddLine(new(max.X, min.Y + scale), new(max.X, max.Y - scale), fill, 2 * scale);
                break;
        }
    }

    public const float ResponsiveBreakpoint = 900f;
    public static readonly Vector4 WindowBackground = new(0.18f, 0.182f, 0.185f, 0.99f);
    public static readonly Vector4 PanelBackground = new(0.19f, 0.20f, 0.208f, 1f);
    public static readonly Vector4 FrameBackground = new(0.32f, 0.32f, 0.32f, 1f);
    public static readonly Vector4 Gold = new(0.82f, 0.70f, 0.43f, 1f);
    public static readonly Vector4 GoldMuted = new(0.48f, 0.42f, 0.30f, 1f);
    public static readonly Vector4 Text = new(0.90f, 0.89f, 0.85f, 1f);
    public static readonly Vector4 TextMuted = new(0.62f, 0.61f, 0.57f, 1f);
    public static readonly Vector4 Warning = new(0.92f, 0.72f, 0.25f, 1f);

    public static void PushMainWindowStyle()
    {
        ImGui.SetNextWindowBgAlpha(WindowBackground.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6f, 4f));

        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextMuted);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackground);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Separator, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.23f, 0.235f, 0.23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.27f, 0.278f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.20f, 0.205f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.37f, 0.37f, 0.365f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.42f, 0.415f, 0.40f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.28f, 0.282f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.38f, 0.365f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.47f, 0.42f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.29f, 0.295f, 0.295f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.36f, 0.36f, 0.35f, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.19f, 0.195f, 0.20f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.225f, 0.23f, 0.235f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.13f, 0.135f, 0.14f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.38f, 0.375f, 0.35f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.50f, 0.44f, 0.32f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Gold);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.35f, 0.31f, 0.235f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.46f, 0.39f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.54f, 0.44f, 0.28f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, new Vector4(0.55f, 0.44f, 0.24f, 0.55f));
    }

    public static void PopMainWindowStyle()
    {
        ImGui.PopStyleColor(31);
        ImGui.PopStyleVar(8);
    }

    public static bool DrawNativeButton(
        string id,
        string label,
        Vector2 size,
        bool disabled,
        Vector4? labelColor = null)
    {
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton($"##{id}", size);
        if (disabled)
            ImGui.EndDisabled();

        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var hovered = !disabled && ImGui.IsItemHovered();
        var held = !disabled && ImGui.IsItemActive();
        var drawList = ImGui.GetWindowDrawList();
        var pressOffset = held ? new Vector2(0f, 1.5f) : Vector2.Zero;
        var rounding = MathF.Min(12f, size.Y * 0.36f);
        var rim = disabled
            ? new Vector4(0.34f, 0.33f, 0.30f, 0.52f)
            : hovered || held
                ? new Vector4(0.72f, 0.62f, 0.42f, 1f)
                : new Vector4(0.49f, 0.45f, 0.36f, 1f);
        var faceTop = disabled
            ? new Vector4(0.25f, 0.25f, 0.24f, 0.68f)
            : held
                ? new Vector4(0.36f, 0.33f, 0.27f, 1f)
                : hovered
                    ? new Vector4(0.37f, 0.36f, 0.33f, 1f)
                    : new Vector4(0.31f, 0.31f, 0.30f, 1f);
        drawList.AddRectFilled(minimum + pressOffset, maximum + pressOffset,
            ImGui.GetColorU32(faceTop), rounding);
        drawList.AddRect(minimum + pressOffset, maximum + pressOffset,
            ImGui.GetColorU32(rim), rounding, ImDrawFlags.RoundCornersAll, 1f);

        var labelSize = ImGui.CalcTextSize(label);
        var labelPosition = ((minimum + maximum) * 0.5f) - (labelSize * 0.5f) + pressOffset;
        drawList.AddText(
            labelPosition + new Vector2(1f, 1.5f),
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.018f, disabled ? 0.32f : 0.82f)),
            label);
        drawList.AddText(
            labelPosition,
            ImGui.GetColorU32(disabled ? TextMuted : labelColor ?? new Vector4(0.93f, 0.92f, 0.88f, 1f)),
            label);
        return clicked && !disabled;
    }
    internal enum BookmarkGlyph
    {
        Store,
        ArmouryStore,
        DresserToArmoire,
        Duplicates,
        Settings,
    }

    internal static void DrawSourceFirstGlyph(ImDrawListPtr drawList, Vector2 center,
        float scale, bool enabled, BookmarkGlyph glyph, bool darkSurface = false, Vector4? inkColor = null, Vector4? paperColor = null)
    {
        var ink = ImGui.GetColorU32(darkSurface
            ? new Vector4(0.91f, 0.76f, 0.43f, enabled ? 1f : 0.62f)
            : new Vector4(0.23f, 0.18f, 0.12f, enabled ? 1f : 0.62f));
        var paper = ImGui.GetColorU32(darkSurface
            ? new Vector4(0.27f, 0.275f, 0.27f, enabled ? 1f : 0.62f)
            : new Vector4(0.78f, 0.71f, 0.57f, enabled ? 1f : 0.62f));
        if (inkColor is { } themedInk)
            ink = ImGui.GetColorU32(themedInk with { W = enabled ? 1f : 0.62f });
        if (paperColor is { } themedPaper)
            paper = ImGui.GetColorU32(themedPaper with { W = enabled ? 1f : 0.62f });
        Vector2 P(float x, float y) => center + new Vector2(x, y) * scale;
        void Line(float x1, float y1, float x2, float y2, float width = 2f) =>
            drawList.AddLine(P(x1, y1), P(x2, y2), ink, width * scale);

        if (glyph == BookmarkGlyph.Settings)
        {
            // A single bold cog matches the source silhouettes' visual weight.
            for (var tooth = 0; tooth < 8; tooth++)
            {
                var angle = tooth * MathF.PI / 4f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                drawList.AddLine(center + direction * (12f * scale),
                    center + direction * (20f * scale), ink, 7f * scale);
            }
            drawList.AddCircleFilled(center, 16f * scale, ink, 32);
            drawList.AddCircleFilled(center, 7f * scale, paper, 24);
            return;
        }

        if (glyph == BookmarkGlyph.Duplicates)
        {
            drawList.AddRectFilled(P(-18, -18), P(7, 7), ink, 4f * scale);
            drawList.AddRectFilled(P(-9, -9), P(20, 20), paper, 5f * scale);
            drawList.AddRectFilled(P(-6, -6), P(20, 20), ink, 4f * scale);
            drawList.AddLine(P(2, 2), P(12, 12), paper, 3f * scale);
            drawList.AddLine(P(12, 2), P(2, 12), paper, 3f * scale);
            return;
        }

        switch (glyph)
        {
            case BookmarkGlyph.ArmouryStore:
                drawList.AddRectFilled(P(-19, -20), P(17, 5), ink, 4f * scale);
                drawList.AddLine(P(-19, -10), P(17, -10), paper, 2f * scale);
                drawList.AddRectFilled(P(-4, -13), P(3, -5), paper, scale);
                drawList.AddRectFilled(P(-2, -11), P(1, -7), ink, scale);
                break;
            case BookmarkGlyph.DresserToArmoire:
                // The hook and triangular rail read as a hanger at small native scales.
                Line(-4, -19, -4, -22, 3f);
                Line(-4, -22, 1, -25, 3f);
                Line(1, -25, 6, -22, 3f);
                Line(6, -22, 6, -18, 3f);
                Line(6, -18, 0, -13, 3f);
                Line(0, -13, 0, -9, 3f);
                Line(0, -9, -19, 3, 3.5f);
                Line(-19, 3, 19, 3, 3.5f);
                Line(19, 3, 0, -9, 3.5f);
                break;
            default:
                // Gathered bag, distinct from the Armoury Chest's rigid rectangular lid.
                drawList.AddRectFilled(P(-15, -14), P(15, 6), ink, 9f * scale);
                drawList.AddTriangleFilled(P(-12, -24), P(12, -24), P(7, -14), ink);
                drawList.AddTriangleFilled(P(-12, -24), P(7, -14), P(-7, -14), ink);
                drawList.AddLine(P(-8, -15), P(8, -15), paper, 2f * scale);
                break;
        }

        // Shared lower-right storage badge: a bent arrow entering a compact tray.
        Line(-12, 11, -12, 16, 3f);
        Line(-12, 16, -6, 20, 3f);
        Line(-6, 20, 0, 20, 3f);
        drawList.AddTriangleFilled(P(4, 20), P(-2, 15), P(-2, 25), ink);
        drawList.AddRectFilled(P(8, 14), P(23, 26), ink, 2f * scale);
        drawList.AddLine(P(9, 18), P(22, 18), paper, 1.5f * scale);
        drawList.AddLine(P(12, 21), P(19, 21), paper, 2f * scale);
    }

}
