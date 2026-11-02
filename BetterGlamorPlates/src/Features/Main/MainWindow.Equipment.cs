using System.Numerics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.Ui;
using Dalamud.Bindings.ImGui;

namespace BetterGlamorPlates.Features.Main;

internal sealed partial class MainWindow
{
    private void DrawEquipment(MainViewState view)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.BeginDisabled(view.ActivePlate is null);
        // Both rows share the same rounded column centers and icon bounds.
        for (var index = 0; index < view.Slots.Count && index < 12; index++)
        {
            var cell = MainLayoutPolicy.EquipmentCell(index, width, designScale);
            DrawSlotCard(view, view.Slots[index], origin + cell.Position, cell.Width, cell.IconSize);
        }
        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(origin + new Vector2(0, S(282)));
        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawSlotCard(MainViewState view, SavedSlotView slot, Vector2 minimum, float width, float iconSize)
    {
        ImGui.PushID((int)slot.Slot);
        CardText(minimum, width, 0, slot.SlotName, PluginTheme.Text);
        var iconPosition = minimum + new Vector2(MathF.Round((width - iconSize) / 2), S(26));
        ImGui.SetCursorScreenPos(iconPosition);
        var selected = view.SelectedSlot == slot.Slot;
        var clicked = ImGui.Selectable("##outfit-item", selected, ImGuiSelectableFlags.None, new Vector2(iconSize));
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            IssueDrawerCommand(OutfitInteraction.ClearSlot(view, slot));
        DrawIconWell(iconPosition, slot.IconId, iconSize);
        if (slot.MissingFromLastLoad)
            ImGui.GetWindowDrawList().AddRectFilled(iconPosition, iconPosition + new Vector2(iconSize),
                ImGui.GetColorU32(new Vector4(0.20f, 0.20f, 0.20f, 0.50f)), S(3));
        if (selected)
            ImGui.GetWindowDrawList().AddRect(iconPosition - new Vector2(S(2)), iconPosition + new Vector2(iconSize + S(2)),
                ImGui.GetColorU32(PluginTheme.Gold), S(4), ImDrawFlags.None, S(2));
        if (clicked)
            issueCommand(new MainCommand.SelectSlot(slot.Slot));
        if (hovered)
            ImGui.SetTooltip($"{slot.SlotName}\n{slot.ItemName}\n{slot.SharedOwnershipLabel ?? "Empty"}"
                + (slot.HasItem ? "\nRight-click to clear" : string.Empty)
                + (slot.HasItem && !slot.DyeFactsAvailable ? "\nDye information is unavailable." : string.Empty));
        DrawDyeSwatches(view, slot, iconPosition, iconSize);
        ImGui.PopID();
    }

    private static void CardText(Vector2 minimum, float width, float y, string text, Vector4 color)
    {
        var visible = FitText(text, width - 4);
        ImGui.GetWindowDrawList().AddText(minimum + new Vector2((width - ImGui.CalcTextSize(visible).X) / 2, y),
            ImGui.GetColorU32(color), visible);
    }

    private void DrawIconWell(Vector2 position, uint iconId, float size)
    {
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(position - new Vector2(S(2)), position + new Vector2(size + S(2)),
            ImGui.GetColorU32(PluginTheme.FrameBackground), S(4));
        draw.AddRect(position - new Vector2(S(2)), position + new Vector2(size + S(2)),
            ImGui.GetColorU32(PluginTheme.GoldMuted), S(4));
        ImGui.SetCursorScreenPos(position);
        DrawIcon(iconId, size);
    }
}

