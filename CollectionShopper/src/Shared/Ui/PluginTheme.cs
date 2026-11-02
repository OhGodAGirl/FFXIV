using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CollectionShopper;

internal static class PluginTheme
{
    private static uint gameTheme;
    internal static void SetGameTheme(uint theme) => gameTheme = ThemeColorPolicy.Normalize(theme);
    private static Vector4 Themed(Vector4 original) => ThemeColorPolicy.Apply(gameTheme, original);
    internal static Vector4 WindowBackground => Themed(new Vector4(0.18f, 0.182f, 0.185f, 0.99f));
    internal static Vector4 PanelBackground => Themed(new Vector4(0.19f, 0.20f, 0.208f, 1f));
    internal static Vector4 FrameBackground => Themed(new Vector4(0.32f, 0.32f, 0.32f, 1f));
    internal static Vector4 Gold => Themed(new Vector4(0.82f, 0.70f, 0.43f, 1f));
    internal static Vector4 GoldMuted => Themed(new Vector4(0.48f, 0.42f, 0.30f, 1f));
    internal static Vector4 Text => Themed(new Vector4(0.90f, 0.89f, 0.85f, 1f));
    internal static Vector4 TextMuted => Themed(new Vector4(0.62f, 0.61f, 0.57f, 1f));

    private const int StyleColorCount = 31;
    private const int StyleVariableCount = 8;

    internal static void Push()
    {
        ImGui.SetNextWindowBgAlpha(WindowBackground.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
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
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, Themed(new Vector4(0.035f, 0.035f, 0.035f, 0.9f)));
        ImGui.PushStyleColor(ImGuiCol.Separator, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Themed(new Vector4(0.23f, 0.235f, 0.23f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Themed(new Vector4(0.27f, 0.278f, 0.27f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Themed(new Vector4(0.20f, 0.205f, 0.20f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Themed(new Vector4(0.37f, 0.37f, 0.365f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Themed(new Vector4(0.42f, 0.415f, 0.40f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.Button, Themed(new Vector4(0.28f, 0.282f, 0.28f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Themed(new Vector4(0.38f, 0.365f, 0.32f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Themed(new Vector4(0.47f, 0.42f, 0.32f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Themed(new Vector4(0.29f, 0.295f, 0.295f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Themed(new Vector4(0.36f, 0.36f, 0.35f, 0.75f)));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, Themed(new Vector4(0.19f, 0.195f, 0.20f, 0.92f)));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, Themed(new Vector4(0.225f, 0.23f, 0.235f, 0.92f)));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Themed(new Vector4(0.13f, 0.135f, 0.14f, 0.9f)));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Themed(new Vector4(0.38f, 0.375f, 0.35f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Themed(new Vector4(0.50f, 0.44f, 0.32f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Gold);
        ImGui.PushStyleColor(ImGuiCol.Header, Themed(new Vector4(0.35f, 0.31f, 0.235f, 0.9f)));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Themed(new Vector4(0.46f, 0.39f, 0.27f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Themed(new Vector4(0.54f, 0.44f, 0.28f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, Themed(new Vector4(0.55f, 0.44f, 0.24f, 0.55f)));
    }

    internal static void Pop()
    {
        ImGui.PopStyleColor(StyleColorCount);
        ImGui.PopStyleVar(StyleVariableCount);
    }

}

