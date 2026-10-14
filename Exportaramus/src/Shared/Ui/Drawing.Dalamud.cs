using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ManagedFontAtlas;

namespace Exportaramus.Shared.Ui;

internal static class Drawing
{
    // Draw-list coordinates are pixels; measure against the active host body font.
    internal static float Unit => ImGui.GetFontSize() / 17f;
    internal static Vector2 Offset(float x, float y) => new Vector2(x, y) * Unit;
    internal static float ButtonWidth(string label, bool icon = false) => ImGui.CalcTextSize(label).X + (icon ? 49 : 24) * Unit;
    internal static float WrappedHeight(string text, float width) => ImGui.CalcTextSize(text, false, Math.Max(1, width)).Y;
    internal static void Wrapped(Vector2 pos, float width, string text, Vector4 color)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, width));
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }
    internal static uint Color(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
    internal static void Frame()
    {
        var min = ImGui.GetWindowPos(); var max = min + ImGui.GetWindowSize(); var draw = ImGui.GetWindowDrawList();
        draw.AddRect(min + new Vector2(2, 3), max + new Vector2(2, 3), Color(VisualTokens.Shadow), 7, ImDrawFlags.None, 3);
        draw.AddRect(min + Vector2.One, max - Vector2.One, Color(VisualTokens.Shadow), 7, ImDrawFlags.None, 3);
        draw.AddRect(min + new Vector2(2), max - new Vector2(2), Color(VisualTokens.Gold), 7, ImDrawFlags.None, 1);
        draw.AddRect(min + new Vector2(4), max - new Vector2(4), Color(VisualTokens.Shadow), 5, ImDrawFlags.None, 1);
    }
    internal static void Panel(Vector2 pos, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, Color(VisualTokens.Panel), 4);
        draw.AddRect(pos, pos + size, Color(VisualTokens.Border), 4);
        draw.AddLine(pos + new Vector2(3, 1), pos + new Vector2(size.X - 3, 1), Color(VisualTokens.Current.PanelHighlight));
    }
    internal static void Heading(string text, IFontHandle font)
    {
        using (font.Push()) ImGui.TextColored(VisualTokens.Heading, text);
    }
    internal static void Tooltip(TooltipId id)
    {
        var text = Tooltips.Approved(id);
        if (text is not null && ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }
    internal static bool Button(string label, string id, Vector2 size, bool enabled, bool reduceMotion, string icon = "", bool cancel = false)
    {
        ImGui.BeginDisabled(!enabled);
        var pos = ImGui.GetCursorScreenPos();
        var pressed = ImGui.Button(id, size);
        var held = ImGui.IsItemActive(); var focused = ImGui.IsItemFocused(); var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var rim = hovered || focused ? VisualTokens.Gold : VisualTokens.Border;
        draw.AddRect(pos, pos + size, Color(rim), 3, ImDrawFlags.None, focused ? 2 : 1);
        draw.AddLine(pos + new Vector2(2, 1), pos + new Vector2(size.X - 2, 1), Color(VisualTokens.Current.ButtonHighlight));
        var textSize = ImGui.CalcTextSize(label);
        var total = textSize.X + (icon.Length == 0 ? 0 : 25 * Unit);
        var origin = pos + new Vector2((size.X - total) / 2, (size.Y - textSize.Y) / 2 + LayoutPolicy.PressOffset(reduceMotion, held));
        if (icon.Length != 0) { UiIcons.Draw(icon, origin, 18 * Unit); origin.X += 25 * Unit; }
        draw.AddText(origin, Color(!enabled ? VisualTokens.Muted : cancel ? VisualTokens.Warning : VisualTokens.Primary), label);
        ImGui.EndDisabled();
        return pressed;
    }
}

