using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SpringCleaning;

internal static class SettingsPanel
{
    internal static void Draw(Configuration configuration, Action configurationChanged)
    {
        var tabStyle = (int)configuration.TabStyle;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.58f);
        if (ImGui.Combo("Tab Style", ref tabStyle,
            "Inset Sockets\0Flush Index\0Ribbon Tabs\0Round Tabs\0Beveled Index\0"))
        {
            configuration.TabStyle = (BookmarkTabStyle)tabStyle;
            configurationChanged();
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        var contentTop = ImGui.GetCursorPosY();
        var sectionHeight = ImGui.GetContentRegionAvail().Y / 4f;
        PositionSection(0, "Store eligible pieces from incomplete outfits. Disable to store complete outfits only.");
        var storePartialOutfits = !configuration.OnlyMoveCompleteOutfits;
        if (ImGui.Checkbox("Store Partial Outfits", ref storePartialOutfits))
        {
            configuration.OnlyMoveCompleteOutfits = !storePartialOutfits;
            configurationChanged();
        }

        var descriptionIndent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.Indent(descriptionIndent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.78f, 1f));
        ImGui.TextWrapped(
            "Store eligible pieces from incomplete outfits. "
            + "Disable to store complete outfits only.");
        ImGui.PopStyleColor();
        ImGui.Unindent(descriptionIndent);

        ImGui.Spacing();
        PositionSection(1, "Skips items that have materia when discarding duplicates.");
        var protectMateria = configuration.ProtectItemsWithMateria;
        if (ImGui.Checkbox("Protect Items with Materia", ref protectMateria))
        {
            configuration.ProtectItemsWithMateria = protectMateria;
            configurationChanged();
        }
        ImGui.Indent(descriptionIndent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.78f, 1f));
        ImGui.TextWrapped("Skips items that have materia when discarding duplicates.");
        ImGui.PopStyleColor();
        ImGui.Unindent(descriptionIndent);
        ImGui.Spacing();
        PositionSection(2, "Skips items that have 100% Spiritbond when discarding duplicates");
        var protectSpiritbond = configuration.ProtectItemsWithSpiritbond;
        if (ImGui.Checkbox("Protect Items with Spiritbond", ref protectSpiritbond))
        {
            configuration.ProtectItemsWithSpiritbond = protectSpiritbond;
            configurationChanged();
        }
        ImGui.Indent(descriptionIndent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.78f, 1f));
        ImGui.TextWrapped("Skips items that have 100% Spiritbond when discarding duplicates");
        ImGui.PopStyleColor();
        ImGui.Unindent(descriptionIndent);
        ImGui.Spacing();
        PositionSection(3, "Remember the positions of Spring Cleaning and Glamour Creation windows. Disable to use default positions.");
        var rememberWindowPlacement = configuration.RememberWindowPlacement;
        if (ImGui.Checkbox("Remember Window Placement", ref rememberWindowPlacement))
        {
            configuration.RememberWindowPlacement = rememberWindowPlacement;
            if (!rememberWindowPlacement)
            {
                configuration.DresserArmoireOverlayX = null;
                configuration.DresserArmoireOverlayY = null;
            }
            configurationChanged();
        }

        ImGui.Indent(descriptionIndent);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.78f, 0.78f, 0.78f, 1f));
        ImGui.TextWrapped(
            "Remember the positions of Spring Cleaning and Glamour Creation windows. "
            + "Disable to use default positions.");
        ImGui.PopStyleColor();
        ImGui.Unindent(descriptionIndent);

        void PositionSection(int index, string description)
        {
            var indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
            var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - indent);
            var height = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y
                + ImGui.CalcTextSize(description, false, width).Y;
            var top = contentTop + sectionHeight * index + MathF.Max(0f, (sectionHeight - height) / 2f);
            ImGui.SetCursorPosY(MathF.Max(ImGui.GetCursorPosY(), top));
        }
    }
}
