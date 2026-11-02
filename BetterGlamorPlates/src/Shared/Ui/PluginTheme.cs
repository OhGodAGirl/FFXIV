using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace BetterGlamorPlates.Shared.Ui;

internal static class PluginTheme
{
    internal static readonly Vector4 FrameIvory = new(.86f, .85f, .89f, 1);
    internal static readonly Vector4 FrameShadow = new(.62f, .61f, .66f, 1);
    internal static readonly Vector4 PearlMint = new(.66f, .79f, .75f, 1);
    internal static readonly Vector4 PearlText = new(.16f, .17f, .20f, 1);
    internal static readonly Vector4 PearlWarning = new(.36f, .22f, .055f, 1);
    internal static readonly Vector4 Selection = new(.91f, .87f, .73f, 1);
    internal static readonly Vector4 WindowBackground = new(.70f, .69f, .74f, 1);
    internal static readonly Vector4 PanelBackground = new(.23f, .24f, .25f, 1);
    internal static readonly Vector4 FrameBackground = new(.32f, .33f, .35f, 1);
    internal static readonly Vector4 Gold = new(.88f, .78f, .48f, 1);
    internal static readonly Vector4 GoldMuted = new(.52f, .53f, .55f, 1);
    internal static readonly Vector4 Text = new(.94f, .94f, .93f, 1);
    internal static readonly Vector4 TextMuted = new(.69f, .70f, .71f, 1);
    internal static readonly Vector4 Positive = new(.39f, .78f, .53f, 1);
    internal static readonly Vector4 Warning = new(.94f, .67f, .25f, 1);
    internal static readonly Vector4 Negative = new(.82f, .38f, .31f, 1);
    internal static readonly Vector4 Armoire = new(.48f, .70f, .91f, 1);
    internal static readonly Vector4 PrimaryAction = new(.76f, .77f, .78f, 1);
    internal static readonly Vector4 PrimaryActionHovered = new(.85f, .86f, .87f, 1);
    internal static readonly Vector4 PrimaryActionActive = new(.65f, .67f, .69f, 1);

    private const int StyleColorCount = 31;
    private const int StyleVariableCount = 9;

    internal static void Push(float scale = 1f)
    {
        ImGui.SetNextWindowBgAlpha(WindowBackground.W);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 6f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 4f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4f, 2f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 4f) * scale);

        ImGui.PushStyleColor(ImGuiCol.Text, Text);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, TextMuted);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBackground);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0.025f, 0.030f, 0.037f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.Separator, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.170f, 0.175f, 0.182f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.240f, 0.245f, 0.252f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.130f, 0.135f, 0.142f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBackground);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.290f, 0.295f, 0.302f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.350f, 0.355f, 0.362f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.220f, 0.225f, 0.232f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.340f, 0.345f, 0.352f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.430f, 0.435f, 0.442f, 1f));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(0.230f, 0.235f, 0.242f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, GoldMuted);
        ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(0.340f, 0.345f, 0.352f, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.140f, 0.145f, 0.152f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.180f, 0.185f, 0.192f, 0.92f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.080f, 0.085f, 0.092f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, new Vector4(0.360f, 0.365f, 0.372f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.500f, 0.505f, 0.512f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Gold);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.350f, 0.355f, 0.362f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.460f, 0.465f, 0.472f, 1f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.540f, 0.545f, 0.552f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, new Vector4(0.550f, 0.555f, 0.562f, 0.55f));
    }

    internal static void Pop()
    {
        ImGui.PopStyleColor(StyleColorCount);
        ImGui.PopStyleVar(StyleVariableCount);
    }
}
