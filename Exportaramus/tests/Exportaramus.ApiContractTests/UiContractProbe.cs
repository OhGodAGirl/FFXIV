using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace Exportaramus.ApiContractTests;

// Compile-only: never invoked by the probe executable or plugin.
internal sealed class UiContractProbe : Window
{
    internal UiContractProbe() : base("Probe", ImGuiWindowFlags.NoTitleBar)
    {
        Size = new(760, 560); SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(680, 360), MaximumSize = new(float.MaxValue) };
    }
    public override void OnOpen() { }
    public override void OnClose() { }
    public override void PreDraw() { }
    public override void PostDraw() { }
    public override void Draw() { }
    internal static void FontContract(IDalamudPluginInterface plugin, IFontHandle font)
    {
        using var suppression = plugin.UiBuilder.FontAtlas.SuppressAutoRebuild();
        using var owned = plugin.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Jupiter, 24));
        using var scope = font.Push();
        _ = font.Available;
        _ = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
    }
    internal static unsafe void ClientVersionContract()
    {
        var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        if (framework != null) { ReadOnlySpan<byte> version = framework->GameVersion; _ = version.Length; }
    }
    internal static void ImGuiContract()
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new(760, 560), ImGuiCond.FirstUseEver); ImGui.SetNextWindowBgAlpha(.995f);
        ImGuiStyleVar[] variables = [ImGuiStyleVar.WindowRounding, ImGuiStyleVar.WindowBorderSize, ImGuiStyleVar.ChildRounding,
            ImGuiStyleVar.FrameRounding, ImGuiStyleVar.ScrollbarRounding, ImGuiStyleVar.WindowPadding, ImGuiStyleVar.FramePadding, ImGuiStyleVar.CellPadding];
        ImGuiCol[] colors = [ImGuiCol.Text, ImGuiCol.TextDisabled, ImGuiCol.WindowBg, ImGuiCol.ChildBg, ImGuiCol.PopupBg,
            ImGuiCol.Border, ImGuiCol.BorderShadow, ImGuiCol.Separator, ImGuiCol.TitleBg, ImGuiCol.TitleBgActive, ImGuiCol.TitleBgCollapsed,
            ImGuiCol.FrameBg, ImGuiCol.FrameBgHovered, ImGuiCol.FrameBgActive, ImGuiCol.Button, ImGuiCol.ButtonHovered, ImGuiCol.ButtonActive,
            ImGuiCol.CheckMark, ImGuiCol.TableHeaderBg, ImGuiCol.TableBorderStrong, ImGuiCol.TableBorderLight, ImGuiCol.TableRowBg,
            ImGuiCol.TableRowBgAlt, ImGuiCol.ScrollbarBg, ImGuiCol.ScrollbarGrab, ImGuiCol.ScrollbarGrabHovered, ImGuiCol.ScrollbarGrabActive,
            ImGuiCol.Header, ImGuiCol.HeaderHovered, ImGuiCol.HeaderActive, ImGuiCol.TextSelectedBg];
        ImGui.PushStyleVar(variables[0], 7f); ImGui.PopStyleVar(); ImGui.PushStyleVar(variables[5], new Vector2(10)); ImGui.PopStyleVar();
        foreach (var color in colors) ImGui.PushStyleColor(color, Vector4.One); ImGui.PopStyleColor(colors.Length);
        _ = ImGui.GetWindowPos(); _ = ImGui.GetWindowSize(); _ = ImGui.GetContentRegionAvail(); _ = ImGui.GetCursorPosX();
        var pos = ImGui.GetCursorScreenPos(); ImGui.SetCursorScreenPos(pos); _ = ImGui.GetItemRectMin(); _ = ImGui.GetItemRectMax();
        ImGui.BeginDisabled(); _ = ImGui.Button("Label", new(28)); _ = ImGui.InvisibleButton("Hidden", new(28)); ImGui.EndDisabled();
        _ = ImGui.IsItemActive(); _ = ImGui.IsItemHovered(); _ = ImGui.IsItemFocused();
        var selected = false; _ = ImGui.Checkbox("Reduce Motion", ref selected);
        ImGui.TextUnformatted("Text"); ImGui.TextColored(Vector4.One, "Text"); ImGui.TextWrapped("Text");
        ImGui.PushTextWrapPos(400); ImGui.PopTextWrapPos(); ImGui.SetTooltip("Tooltip"); ImGui.Spacing(); ImGui.Separator();
        ImGui.SameLine(); ImGui.Dummy(new(28)); _ = ImGui.CalcTextSize("Text"); _ = ImGui.GetTime(); ImGui.SetClipboardText("{}");
        if (ImGui.BeginTable("Table", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        { ImGui.TableSetupColumn("Column", ImGuiTableColumnFlags.WidthStretch); ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.EndTable(); }
        _ = ImGui.CollapsingHeader("Section", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.SetKeyboardFocusHere(); ImGui.SetItemDefaultFocus(); ImGui.OpenPopup("Prompt");
        if (ImGui.BeginPopupModal("Prompt", ref selected, ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); }
        var draw = ImGui.GetWindowDrawList(); var rgba = ImGui.ColorConvertFloat4ToU32(Vector4.One);
        draw.AddRectFilled(pos, pos + Vector2.One, rgba, 4); draw.AddRect(pos, pos + Vector2.One, rgba, 4, ImDrawFlags.None, 1);
        draw.AddRectFilledMultiColor(pos, pos + Vector2.One, rgba, rgba, rgba, rgba);
        draw.AddLine(pos, pos + Vector2.One, rgba, 1); draw.AddText(pos, rgba, "Text");
        draw.AddCircle(pos, 5, rgba, 20, 1); draw.AddCircleFilled(pos, 5, rgba);
    }
}
