using System.Numerics;
using BetterGlamorPlates.Features.GlamourPlates;
using Dalamud.Bindings.ImGui;

namespace BetterGlamorPlates.Features.Main;

internal sealed partial class MainWindow
{
    private void DrawCollectionPanel(MainViewState view, float height)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(6)));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.65f, .65f, .69f, 1));
        var panelVisible = ImGui.BeginChild("##collection-panel", new Vector2(0, height), true,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        if (!panelVisible)
        {
            ImGui.EndChild();
            return;
        }
        DrawSectionHeading("Collection");
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(S(190));
        if (ImGui.BeginCombo("##slot-selector", PlateRules.GetSlotName(view.SelectedSlot)))
        {
            foreach (var slot in view.Slots)
            {
                if (ImGui.Selectable(slot.SlotName, slot.Slot == view.SelectedSlot))
                    issueCommand(new MainCommand.SelectSlot(slot.Slot));
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine(0, S(MainLayoutPolicy.CollectionTileGap));
        ImGui.SetNextItemWidth(Math.Max(S(100), width - S(197)));
        if (ImGui.InputTextWithHint("##collection-filter", "Search collected items", collectionFilterBuffer.AsSpan()))
            issueCommand(new MainCommand.SetFilter(ReadBuffer(collectionFilterBuffer)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, S(12));
        var gridVisible = ImGui.BeginChild("##collection-grid", Vector2.Zero, false);
        ImGui.PopStyleVar(2);
        if (gridVisible)
        {
            if (view.VisibleItems.Count == 0)
                ImGui.TextWrapped(view.EmptyCollectionMessage);
            else
                DrawCollectionRows(view);
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void DrawCollectionRows(MainViewState view)
    {
        var grid = MainLayoutPolicy.CollectionGrid(ImGui.GetContentRegionAvail().X,
            ImGui.GetWindowHeight(), S(MainLayoutPolicy.CollectionTileGap));
        var columns = grid.Columns;
        var origin = ImGui.GetCursorScreenPos();
        var rowPitch = grid.RowPitch;
        var columnPitch = grid.TileSize + S(MainLayoutPolicy.CollectionTileGap);
        var clipper = ImGui.ImGuiListClipper();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(S(MainLayoutPolicy.CollectionTileGap)));
        try
        {
            clipper.Begin(MainLayoutPolicy.CalculateRowCount(view.VisibleItems.Count, columns), rowPitch);
            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        var index = MainLayoutPolicy.CalculateItemIndex(row, column, columns);
                        if (index >= view.VisibleItems.Count)
                            break;
                        ImGui.SetCursorScreenPos(origin + new Vector2(column * columnPitch, row * rowPitch));
                        DrawCollectionTile(view.SelectedSlot, view.VisibleItems[index], grid.TileSize);
                    }
                    ImGui.SetCursorScreenPos(origin + new Vector2(0, (row + 1) * rowPitch));
                }
            }
            clipper.End();
        }
        finally
        {
            clipper.Destroy();
            ImGui.PopStyleVar();
        }
    }
}

