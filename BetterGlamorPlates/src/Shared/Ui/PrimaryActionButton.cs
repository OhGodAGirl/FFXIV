using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterGlamorPlates.Shared.Ui;

/// <summary>Applies the Native Pearl silver treatment to a real keyboard-aware button.</summary>
internal static class PrimaryActionButton
{
    internal static bool Draw(string label, Vector2 size, bool enabled)
    {
        ImGui.BeginDisabled(!enabled);
        ImGui.PushStyleColor(ImGuiCol.Button, PluginTheme.PrimaryAction);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PluginTheme.PrimaryActionHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PluginTheme.PrimaryActionActive);
        ImGui.PushStyleColor(ImGuiCol.Text, PluginTheme.PearlText);
        var activated = ImGui.Button(label, size);
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        // Decoration wraps the real control without replacing its navigation or hit testing.
        drawList.AddRect(minimum + new Vector2(1), maximum - new Vector2(1),
            ImGui.GetColorU32(enabled ? PluginTheme.Gold : PluginTheme.GoldMuted), 5f);
        drawList.AddLine(minimum + new Vector2(4, 3), new Vector2(maximum.X - 4, minimum.Y + 3),
            ImGui.GetColorU32(enabled ? PluginTheme.FrameIvory : PluginTheme.TextMuted));
        drawList.AddLine(new Vector2(minimum.X + 4, maximum.Y - 3), maximum - new Vector2(4, 3),
            ImGui.GetColorU32(PluginTheme.WindowBackground), 2f);
        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
        return activated;
    }
}
