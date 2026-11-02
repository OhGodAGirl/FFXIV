using Dalamud.Bindings.ImGui;

namespace CollectionShopper;

internal sealed class ShopperCategorySettingsView
{
    private readonly Plugin plugin;
    private readonly Func<ShoppingCategory, string> getMissingCount;
    private readonly Action categoryVisibilityChanged;

    internal ShopperCategorySettingsView(Plugin plugin, Func<ShoppingCategory, string> getMissingCount,
        Action categoryVisibilityChanged)
    {
        this.plugin = plugin;
        this.getMissingCount = getMissingCount;
        this.categoryVisibilityChanged = categoryVisibilityChanged;
    }

    internal void Draw()
    {
        var flags = ImGuiTableFlags.BordersInnerH
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.SizingStretchProp
            | ImGuiTableFlags.NoSavedSettings;
        if (!ImGui.BeginTable("##category-settings", 4, flags))
            return;

        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed,
            Math.Max(78f, ImGui.CalcTextSize("Unknown").X + ImGui.GetStyle().CellPadding.X * 2f));
        ImGui.TableSetupColumn("Tab", ImGuiTableColumnFlags.WidthFixed, 54f);
        ImGui.TableSetupColumn("Purchases", ImGuiTableColumnFlags.WidthFixed, 112f);
        ImGui.TableHeadersRow();

        foreach (var category in Enum.GetValues<ShoppingCategory>())
        {
            ImGui.PushID((int)category);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(CategorySettingsPolicy.GetCategoryLabel(category));

            ImGui.TableSetColumnIndex(1);
            var missing = getMissingCount(category);
            DrawCenteredCellText(missing);

            ImGui.TableSetColumnIndex(2);
            var showTab = IsCategoryTabVisible(category);
            if (DrawCenteredCheckbox("##show-tab", ref showTab))
            {
                CategorySettingsPolicy.SetCategoryTabVisible(plugin.Configuration, category, showTab);
                plugin.SaveConfiguration();
                categoryVisibilityChanged();
            }
            if (ImGui.IsItemHovered())
                PluginControls.DrawTooltip($"Show {CategorySettingsPolicy.GetCategoryLabel(category)} tab.");

            ImGui.TableSetColumnIndex(3);
            var autoShop = IsAutomaticCategoryEnabled(category);
            if (DrawCenteredCheckbox("##auto-shop", ref autoShop))
            {
                CategorySettingsPolicy.SetAutomaticCategoryEnabled(plugin.Configuration, category, autoShop);
                plugin.SaveConfiguration();
            }
            if (ImGui.IsItemHovered())
                PluginControls.DrawTooltip($"Buy All: include {CategorySettingsPolicy.GetCategoryLabel(category)}.");
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    internal bool IsAutomaticCategoryEnabled(ShoppingCategory category)
        => CategorySettingsPolicy.IsAutomaticCategoryEnabled(plugin.Configuration, category);

    internal bool IsCategoryTabVisible(ShoppingCategory category)
        => CategorySettingsPolicy.IsCategoryTabVisible(plugin.Configuration, category);
    private static bool DrawCenteredCheckbox(string id, ref bool value)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var checkboxWidth = ImGui.GetFrameHeight();
        if (availableWidth > checkboxWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((availableWidth - checkboxWidth) * 0.5f));
        return ImGui.Checkbox(id, ref value);
    }

    private static void DrawCenteredCellText(string text)
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var textWidth = ImGui.CalcTextSize(text).X;
        if (availableWidth > textWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((availableWidth - textWidth) * 0.5f));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
    }

}
