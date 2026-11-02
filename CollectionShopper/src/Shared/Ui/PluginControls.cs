using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CollectionShopper;

internal static class PluginControls
{
    internal static bool DrawNativeButton(
        string label,
        Vector2 size,
        bool disabled,
        Vector4? labelColor = null)
    {
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton($"##{label}", size);
        if (disabled)
            ImGui.EndDisabled();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = !disabled && ImGui.IsItemHovered();
        var held = !disabled && ImGui.IsItemActive();
        var drawList = ImGui.GetWindowDrawList();
        var pressOffset = held ? new Vector2(0f, PluginMotion.PressOffset) : Vector2.Zero;
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
        var faceBottom = disabled
            ? new Vector4(0.17f, 0.17f, 0.165f, 0.68f)
            : held
                ? new Vector4(0.24f, 0.22f, 0.18f, 1f)
                : hovered
                    ? new Vector4(0.23f, 0.225f, 0.21f, 1f)
                    : new Vector4(0.19f, 0.19f, 0.185f, 1f);

        drawList.AddRectFilled(min + new Vector2(1.5f, 3f), max + new Vector2(1.5f, 3f),
            ImGui.GetColorU32(new Vector4(0.015f, 0.015f, 0.012f, 0.72f)), rounding);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.10f, 0.10f, 0.095f, 1f)),
            rounding);
        var rimMin = min + new Vector2(1f, 1f) + pressOffset;
        var rimMax = max - new Vector2(1f, 1f) + pressOffset;
        drawList.AddRectFilled(rimMin, rimMax, ImGui.GetColorU32(rim), rounding - 1f);
        var faceMin = min + new Vector2(2.5f, 2.5f) + pressOffset;
        var faceMax = max - new Vector2(2.5f, 2.5f) + pressOffset;
        drawList.AddRectFilled(faceMin, faceMax, ImGui.GetColorU32(faceBottom), rounding - 2.5f);
        var gradientMin = faceMin + new Vector2(3f, 2f);
        var gradientMax = faceMax - new Vector2(3f, 2f);
        drawList.AddRectFilledMultiColor(
            gradientMin,
            gradientMax,
            ImGui.GetColorU32(faceTop),
            ImGui.GetColorU32(faceTop),
            ImGui.GetColorU32(faceBottom),
            ImGui.GetColorU32(faceBottom));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.055f, 0.055f, 0.05f, 1f)), rounding,
            ImDrawFlags.RoundCornersAll, 1.25f);
        drawList.AddRect(rimMin, rimMax, ImGui.GetColorU32(rim), rounding - 1f,
            ImDrawFlags.RoundCornersAll, 1f);
        drawList.AddLine(
            faceMin + new Vector2(7f, 3f),
            new Vector2(faceMax.X - 7f, faceMin.Y + 3f),
            ImGui.GetColorU32(new Vector4(0.93f, 0.89f, 0.78f, disabled ? 0.10f : 0.34f)),
            1f);
        drawList.AddLine(
            new Vector2(faceMin.X + 7f, faceMax.Y - 2f),
            faceMax - new Vector2(7f, 2f),
            ImGui.GetColorU32(new Vector4(0.025f, 0.025f, 0.022f, 0.78f)),
            1.25f);
        var labelSize = ImGui.CalcTextSize(label);
        var labelPosition = ((min + max) * 0.5f) - (labelSize * 0.5f) + pressOffset;
        drawList.AddText(labelPosition + new Vector2(1f, 1.5f),
            ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.018f, disabled ? 0.32f : 0.82f)), label);
        drawList.AddText(labelPosition,
            ImGui.GetColorU32(disabled
                ? PluginTheme.TextMuted
                : labelColor ?? new Vector4(0.93f, 0.92f, 0.88f, 1f)), label);
        return clicked && !disabled;
    }

    internal static void DrawHelpMarker(string tooltip)
    {
        ImGui.SameLine(0f, 5f);
        ImGui.TextDisabled("(?)");
        if (!ImGui.IsItemHovered())
            return;

        DrawTooltip(tooltip);
    }

    internal static void DrawTooltip(string tooltip)
    {
        ImGui.BeginTooltip();
        // Tooltip windows escape table clipping; constrain long details to the screen.
        var width = UiLayoutPolicy.TooltipWidth(ImGui.GetFontSize(),
            ImGui.GetMainViewport().WorkSize.X, ImGui.GetStyle().WindowPadding.X);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextUnformatted(tooltip);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    internal static void DrawWrappedNote(string note)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, PluginTheme.TextMuted);
        ImGui.TextWrapped(note);
        ImGui.PopStyleColor();
    }

}
