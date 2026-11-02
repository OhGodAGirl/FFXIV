using System.Numerics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace BetterGlamorPlates.Features.Main;

internal sealed partial class MainWindow
{
    private bool plateSearchOpen;
    private bool focusPlateSearch;
    private readonly LibrarySearchState librarySearch = new();
    private const float IconDrawerWidth = 432;
    private bool iconDrawerReserved;
    private bool focusIconPicker;
    private SavedPlateSummary? rowTarget;
    private string? requestedPopup;
    private string iconCategory = "Weapons";
    private string iconSearch = string.Empty;
    private readonly IconChoiceFilter iconChoices = new();
    private int enteredIconId;

    private void DrawLibrary(MainViewState view)
    {
        ImGui.SetWindowFontScale(1f);
        ImGui.SetWindowFontScale(bodyFontSize / ImGui.GetFontSize());
        if (librarySearch.Reconcile(view.PlateSearch, view.TotalSavedPlateCount) is { } acknowledged)
            WriteBuffer(plateSearchBuffer, acknowledged);
        var heading = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        if (plateSearchOpen)
        {
            ImGui.SetNextItemWidth(width - S(32));
            if (focusPlateSearch) { ImGui.SetKeyboardFocusHere(); focusPlateSearch = false; }
            if (ImGui.InputTextWithHint("##plate-search", "Search Saved Plates", plateSearchBuffer.AsSpan()))
            {
                librarySearch.Pending = ReadBuffer(plateSearchBuffer);
                issueCommand(new MainCommand.SetPlateSearch(librarySearch.Pending));
            }
            ImGui.SameLine(0, S(4));
            if (IconButton("close-search", FontAwesomeIcon.Times, "Close Search", S(26)))
            {
                plateSearchOpen = false;
                Array.Clear(plateSearchBuffer);
                librarySearch.Pending = string.Empty;
                issueCommand(new MainCommand.SetPlateSearch(string.Empty));
            }
        }
        else
        {
            using (sectionFont.Push())
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), S(26), heading,
                    ImGui.GetColorU32(PluginTheme.PearlText), "Saved Plates");
            ImGui.SetCursorScreenPos(heading + new Vector2(width - S(26), 0));
            if (IconButton("search", FontAwesomeIcon.Search, "Search Saved Plates", S(26)))
            {
                plateSearchOpen = true;
                focusPlateSearch = true;
            }
        }
        ImGui.SetCursorScreenPos(heading + new Vector2(0, S(38)));
        var listHeight = Math.Max(S(60), ImGui.GetContentRegionAvail().Y - S(88));
        if (ImGui.BeginChild("##saved-list", new Vector2(0, listHeight)))
        {
            ImGui.SetWindowFontScale(1f);
            if (view.SavedPlates.Count == 0)
                ImGui.TextWrapped(view.TotalSavedPlateCount == 0 ? "No local plates yet." : "No plates match this search.");
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(view.SavedPlates.Count, S(60));
                while (clipper.Step())
                    for (var index = clipper.DisplayStart; index < clipper.DisplayEnd; index++)
                        DrawSavedRow(view, view.SavedPlates[index]);
                clipper.End();
            }
            finally { clipper.Destroy(); }
        }
        ImGui.EndChild();
        if (LibraryActionButton("New"))
            issueCommand(new MainCommand.CreatePlate());
        if (LibraryActionButton("Save Current"))
            issueCommand(new MainCommand.CaptureCurrentPlate());
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy the selected native plate into a new local outfit.");
        DrawRowPopups(view);
    }

    private bool LibraryActionButton(string label)
    {
        var clicked = ImGui.Button($"##library-{label}", new Vector2(-1, S(36)));
        var minimum = ImGui.GetItemRectMin();
        var size = ImGui.GetItemRectSize();
        var fontSize = S(23);
        var textSize = ImGui.CalcTextSize(label) * (fontSize / ImGui.GetFontSize());
        ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), fontSize,
            minimum + (size - textSize) / 2, ImGui.GetColorU32(PluginTheme.Text), label);
        return clicked;
    }

    private void DrawSavedRow(MainViewState view, SavedPlateSummary plate)
    {
        ImGui.PushID(plate.Id.ToString());
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var selected = plate.Id == view.SelectedPlateId;
        var draw = ImGui.GetWindowDrawList();
        if (selected)
        {
            draw.AddRectFilled(origin, origin + new Vector2(width, S(56)), ImGui.GetColorU32(PluginTheme.Selection), S(3));
            draw.AddRect(origin, origin + new Vector2(width, S(56)), ImGui.GetColorU32(PluginTheme.Gold), S(3));
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(S(5), S(6)));
        if (ImGui.Button("##assigned-icon", new Vector2(S(44))))
        {
            rowTarget = plate;
            iconSearch = string.Empty;
            enteredIconId = 0;
            drawers.OpenIcon(plate);
            focusIconPicker = true;
        }
        var iconHovered = ImGui.IsItemHovered();
        DrawIconWell(origin + new Vector2(S(5), S(6)), plate.IconId, S(44));
        if (plate.IconId == 0)
            draw.AddText(origin + new Vector2(S(18), S(18)), ImGui.GetColorU32(PluginTheme.Text), "◇");
        if (iconHovered) ImGui.SetTooltip($"Choose a game icon for {plate.Name}");
        var actionsWidth = S(72);
        var nameWidth = Math.Max(S(12), width - S(58) - actionsWidth);
        ImGui.SetCursorScreenPos(origin + new Vector2(S(56), S(10)));
        if (ImGui.Selectable($"##select-{plate.Id}", false, ImGuiSelectableFlags.None, new Vector2(nameWidth, S(36))))
            issueCommand(new MainCommand.SelectPlate(plate.Id));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{plate.Name}\n{plate.FilledSlotCount}/12 items");
        draw.AddText(origin + new Vector2(S(56), S(19)), ImGui.GetColorU32(selected ? PluginTheme.PearlText : PluginTheme.Text), FitText((plate.HasUnsavedChanges ? "* " : "") + plate.Name, nameWidth));
        ImGui.SetCursorScreenPos(origin + new Vector2(width - actionsWidth, S(17)));
        if (IconButton("rename", FontAwesomeIcon.Pen, $"Rename {plate.Name}", S(18)))
        {
            rowTarget = plate;
            WriteBuffer(nameBuffer, plate.Name);
            drawers.IconOpen = false;
            requestedPopup = "##rename-plate";
        }
        ImGui.SameLine(0, S(7));
        if (IconButton("duplicate", FontAwesomeIcon.Clone, $"Duplicate {plate.Name}", S(18)))
            issueCommand(new MainCommand.DuplicatePlate(plate.Id));
        ImGui.SameLine(0, S(7));
        if (IconButton("delete", FontAwesomeIcon.TrashAlt, $"Delete {plate.Name}", S(18)))
        {
            issueCommand(new MainCommand.DeletePlate(plate.Id));
        }
        draw.AddLine(origin + new Vector2(S(4), S(59)), origin + new Vector2(width - S(4), S(59)),
            ImGui.GetColorU32(PluginTheme.GoldMuted));
        ImGui.SetCursorScreenPos(origin + new Vector2(0, S(60)));
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(origin + new Vector2(0, S(60)));
        ImGui.PopID();
    }

    private bool IconButton(string id, FontAwesomeIcon icon, string tooltip, float size)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(S(2)));
        ImGui.PushFont(UiBuilder.IconFont);
        var clicked = ImGui.Button($"{icon.ToIconString()}##{id}", new Vector2(size));
        ImGui.PopFont();
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private void DrawRowPopups(MainViewState view)
    {
        if (requestedPopup is { } popup)
        {
            ImGui.OpenPopup(popup);
            requestedPopup = null;
        }
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, viewport.WorkSize);
        if (ImGui.BeginPopup("##rename-plate"))
        {
            ImGui.SetWindowFontScale(designScale / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted("Rename saved plate");
            ImGui.SetNextItemWidth(Math.Min(S(280), viewport.WorkSize.X - S(32)));
            var submit = ImGui.InputText("##name", nameBuffer.AsSpan(), ImGuiInputTextFlags.EnterReturnsTrue);
            if ((ImGui.Button("Rename") || submit) && rowTarget is { } rename)
            {
                issueCommand(new MainCommand.RenamePlate(rename.Id, ReadBuffer(nameBuffer)));
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

    }

    private void DrawIconPicker(MainViewState view)
    {
        if (!drawers.HasCurrentIconTarget(view)) return;
        if (!iconDrawerReserved) return;
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth() + S(4), 0), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(S(IconDrawerWidth), ImGui.GetWindowHeight()), ImGuiCond.Always);
        if (focusIconPicker) { ImGui.SetNextWindowFocus(); focusIconPicker = false; }
        ImGui.PushStyleColor(ImGuiCol.WindowBg, PluginTheme.PanelBackground);
        var drawerVisible = ImGui.Begin("##icon-picker", ref drawers.IconOpen,
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        ImGui.PopStyleColor();
        if (!drawerVisible)
        {
            ImGui.End();
            return;
        }
        ImGui.SetWindowFontScale(designScale / Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted("Choose a game icon");
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - S(34));
        if (IconButton("close-icon-picker", FontAwesomeIcon.Times, "Close Icon Picker", S(26))) drawers.IconOpen = false;
        if (GameIconsUnavailable) ImGui.TextWrapped("Game icon list unavailable. Enter an installed game icon ID.");
        else if (!GameIconsReady) ImGui.TextUnformatted("Loading game icons…");
        if (ImGui.BeginTabBar("##icon-categories"))
        {
            foreach (var category in new[] { "Weapons", "Armour", "Accessories", "Other" })
                if (ImGui.BeginTabItem(category))
                {
                    iconCategory = category;
                    ImGui.EndTabItem();
                }
            ImGui.EndTabBar();
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##icon-search", "Search icons by name", ref iconSearch, 100);
        var changed = iconChoices.Update(GameIcons, iconCategory, iconSearch);
        var filteredIcons = iconChoices.Items;
        if (ImGui.BeginChild("##game-icons", new Vector2(0, Math.Max(S(48), ImGui.GetContentRegionAvail().Y - S(80)))))
        {
            if (changed) ImGui.SetScrollY(0);
            if (filteredIcons.Length == 0) ImGui.TextWrapped("No icons match this category and search.");
            var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + S(8)) / S(68)));
            var clipper = ImGui.ImGuiListClipper();
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(S(8)));
            try
            {
                clipper.Begin(MainLayoutPolicy.CalculateRowCount(filteredIcons.Length, columns), S(68));
                while (clipper.Step())
                    for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                        for (var column = 0; column < columns; column++)
                        {
                            var index = row * columns + column;
                            if (index >= filteredIcons.Length) break;
                            if (column > 0) ImGui.SameLine(0, S(8));
                            var option = filteredIcons[index];
                            ImGui.PushID((int)option.Id);
                            var position = ImGui.GetCursorScreenPos();
                            var clicked = ImGui.Selectable("##icon", false, ImGuiSelectableFlags.DontClosePopups, new Vector2(S(60)));
                            var hovered = ImGui.IsItemHovered();
                            var after = ImGui.GetCursorScreenPos();
                            ImGui.SetCursorScreenPos(position);
                            DrawIcon(option.Id, S(60));
                            ImGui.SetCursorScreenPos(after);
                            if (hovered) ImGui.SetTooltip(option.Name);
                            if (clicked && rowTarget is { } plate)
                            {
                                IssueDrawerCommand(drawers.AssignIcon(view, option.Id));
                                drawers.IconOpen = false;
                            }
                            ImGui.PopID();
                        }
                clipper.End();
            }
            finally { clipper.Destroy(); ImGui.PopStyleVar(); }
        }
        ImGui.EndChild(); ImGui.SetNextItemWidth(S(160));
        ImGui.InputInt("Game icon ID", ref enteredIconId, 0, 0);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enter another installed game icon ID, including job or interface icons.");
        if (ImGui.Button("Use ID") && rowTarget is { } target && enteredIconId > 0)
            IssueDrawerCommand(drawers.AssignIcon(view, (uint)enteredIconId));
        ImGui.SameLine();
        if (ImGui.Button("Use outfit icon") && rowTarget is { } reset)
            IssueDrawerCommand(drawers.AssignIcon(view, 0));
        ImGui.End();
    }
}

