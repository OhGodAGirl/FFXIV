using System.Numerics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace BetterGlamorPlates.Features.Main;

internal sealed partial class MainWindow
{
    private readonly byte[] dyeSearchBuffer = new byte[96];
    private bool focusDyePicker;
    private const float DyeDrawerWidth = 196;
    private bool drawerReserved;
    private bool dyeSearchOpen;
    private bool focusDyeSearch;

    private static Vector4 DyeColor(uint rgb) => new(((rgb >> 16) & 255) / 255f,
        ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    private void DrawDyeSwatches(MainViewState view, SavedSlotView slot, Vector2 iconPosition, float iconSize)
    {
        var count = slot.HasItem ? slot.DyeCount : 0;
        var size = S(20);
        var gap = S(8);
        var left = iconPosition.X + (iconSize - count * size - Math.Max(0, count - 1) * gap) / 2;
        for (byte channel = 0; channel < count; channel++)
        {
            ImGui.SetCursorScreenPos(new Vector2(left + channel * (size + gap), iconPosition.Y + iconSize + S(8)));
            var id = channel == 0 ? slot.Stain0Id : slot.Stain1Id;
            var dye = Dyes.FirstOrDefault(option => option.Id == id);
            var color = dye is null ? new Vector4(0.3f, 0.3f, 0.3f, 1) : DyeColor(dye.Color);
            if (ImGui.ColorButton($"##swatch-{channel}", color,
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(size)))
            {
                if (drawers.OpenDye(view, slot, channel, Dyes))
                {
                    Array.Clear(dyeSearchBuffer);
                    focusDyePicker = true;
                }
            }

        }
    }


    private void DrawDyePicker(MainViewState view)
    {
        var slot = drawers.CurrentDyeSlot(view);
        if (slot is null) return;
        var target = drawers.DyeTarget!;
        if (!drawerReserved) return;
        var position = ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() + S(4), 0);
        var size = new Vector2(S(DyeDrawerWidth), ImGui.GetWindowHeight());
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        if (focusDyePicker) { ImGui.SetNextWindowFocus(); focusDyePicker = false; }
        ImGui.PushStyleColor(ImGuiCol.WindowBg, PluginTheme.PanelBackground);
        var drawerVisible = ImGui.Begin("##local-dye-picker", ref drawers.DyeOpen,
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleColor();
        if (!drawerVisible)
        {
            ImGui.End();
            return;
        }
        ImGui.SetWindowFontScale(designScale / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        var selected = target.Channel == 0 ? slot.Stain0Id : slot.Stain1Id;
        if (dyeSearchOpen)
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - S(32));
            if (focusDyeSearch) { ImGui.SetKeyboardFocusHere(); focusDyeSearch = false; }
            ImGui.InputTextWithHint("##dye-search", "Search dyes", dyeSearchBuffer.AsSpan());
            ImGui.SameLine(0, S(4));
            if (IconButton("close-dye-search", FontAwesomeIcon.Times, "Close Dye Search", S(26)))
            {
                dyeSearchOpen = false;
                Array.Clear(dyeSearchBuffer);
            }
        }
        else
        {
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - S(34));
            if (IconButton("close-drawer", FontAwesomeIcon.Times, "Close Dye Drawer", S(26))) drawers.DyeOpen = false;
        }
        var name = Dyes.FirstOrDefault(dye => dye.Id == selected)?.Name ?? (selected == 0 ? "No dye" : "Unknown colour");
        ImGui.TextUnformatted(FitText(name, ImGui.GetContentRegionAvail().X));
        if (ImGui.IsItemHovered()) ShowWrappedTooltip(name);
        var regularSelected = !drawers.SpecialDyes;
        if (regularSelected) ImGui.PushStyleColor(ImGuiCol.Button, PluginTheme.GoldMuted);
        if (IconButton("regular-dyes", FontAwesomeIcon.Tint, "Regular Dyes", S(24))) drawers.SpecialDyes = false;
        if (regularSelected) ImGui.PopStyleColor();
        ImGui.SameLine(0, S(5));
        var specialSelected = drawers.SpecialDyes;
        if (specialSelected) ImGui.PushStyleColor(ImGuiCol.Button, PluginTheme.GoldMuted);
        if (IconButton("special-dyes", FontAwesomeIcon.Gem, "Special Dyes", S(24))) drawers.SpecialDyes = true;
        if (specialSelected) ImGui.PopStyleColor();
        ImGui.SameLine(0, S(5));
        if (IconButton("remove-dye", FontAwesomeIcon.Eraser, "Clear Selected", S(24))) IssueDrawerCommand(drawers.SelectDye(view, 0));
        ImGui.SameLine(0, S(5));
        if (IconButton("clear-item-dyes", FontAwesomeIcon.Times, "Clear Both", S(24)))
            IssueDrawerCommand(drawers.ClearBoth(view));
        ImGui.SameLine(0, S(5));
        if (IconButton("clear-outfit-dyes", FontAwesomeIcon.TrashAlt, "Clear All", S(24)))
            IssueDrawerCommand(drawers.ClearAll(view));
        ImGui.SameLine(0, S(5));
        if (IconButton("search-dyes", FontAwesomeIcon.Search, "Search Dyes", S(24)))
        { dyeSearchOpen = true; focusDyeSearch = true; }
        var search = ReadBuffer(dyeSearchBuffer);
        ImGui.Separator();
        var visible = DrawerSession.FilterDyes(Dyes, drawers.SpecialDyes, search);
        if (ImGui.BeginChild("##dye-colours", new Vector2(0, 0), false,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var origin = ImGui.GetCursorScreenPos();
            var available = ImGui.GetContentRegionAvail();
            var rows = Math.Max(1, (visible.Length + 3) / 4);
            var pitchX = available.X / 4;
            var pitchY = Math.Min(S(40), available.Y / rows);
            var swatchSize = Math.Max(1, Math.Min(S(34), Math.Min(pitchX, pitchY) - S(4)));
            for (var index = 0; index < visible.Length; index++)
            {
                var dye = visible[index];
                ImGui.SetCursorScreenPos(origin + new Vector2(index % 4 * pitchX + (pitchX - swatchSize) / 2,
                    index / 4 * pitchY + (pitchY - swatchSize) / 2));
                ImGui.PushID(dye.Id);
                if (ImGui.ColorButton("##colour", DyeColor(dye.Color),
                        ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(swatchSize)))
                    IssueDrawerCommand(drawers.SelectDye(view, dye.Id));
                if (selected == dye.Id) OutlineLastItem();
                if (ImGui.IsItemHovered()) ShowWrappedTooltip(dye.Name + "\n" + dye.Materials);
                ImGui.PopID();
            }
            if (Dyes.Count == 0) ImGui.TextWrapped("The dye palette is unavailable.");
        }
        ImGui.EndChild();
        ImGui.End();
    }
}

