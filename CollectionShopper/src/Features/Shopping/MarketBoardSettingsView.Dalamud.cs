using Dalamud.Bindings.ImGui;

namespace CollectionShopper;

internal sealed class MarketBoardSettingsView
{
    private readonly Plugin plugin;
    private readonly ShoppingController shoppingController;

    internal MarketBoardSettingsView(Plugin plugin, ShoppingController shoppingController)
    {
        this.plugin = plugin;
        this.shoppingController = shoppingController;
    }

    internal void Draw()
    {
        if (shoppingController.IsRunning)
            ImGui.BeginDisabled();

        ImGui.TextUnformatted("Maximum Gil per Item");
        var maxPerItem = plugin.Configuration.MaximumPricePerItem;
        var unlimitedPerItem = plugin.Configuration.UnlimitedPricePerItem;
        if (unlimitedPerItem)
            ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(GetNumberInputWidth());
        if (ImGui.InputInt("##maximum-per-item", ref maxPerItem, 1_000, 10_000))
        {
            plugin.Configuration.MaximumPricePerItem = Math.Max(0, maxPerItem);
            plugin.SaveConfiguration();
        }
        if (unlimitedPerItem)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Checkbox("Unlimited Gil per Item", ref unlimitedPerItem))
        {
            plugin.Configuration.UnlimitedPricePerItem = unlimitedPerItem;
            plugin.SaveConfiguration();
        }

        ImGui.TextUnformatted("Maximum Gil for This Run");
        var maxTotal = plugin.Configuration.MaximumTotalSpend;
        var unlimitedTotal = plugin.Configuration.UnlimitedTotalSpend;
        if (unlimitedTotal)
            ImGui.BeginDisabled();
        ImGui.SetNextItemWidth(GetNumberInputWidth());
        if (ImGui.InputInt("##maximum-total", ref maxTotal, 10_000, 100_000))
        {
            plugin.Configuration.MaximumTotalSpend = Math.Max(0, maxTotal);
            plugin.SaveConfiguration();
        }
        if (unlimitedTotal)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Checkbox("Unlimited Gil for This Run", ref unlimitedTotal))
        {
            plugin.Configuration.UnlimitedTotalSpend = unlimitedTotal;
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Item Quality");
        var enableHighQuality = plugin.Configuration.EnableHighQualityPurchases;
        if (ImGui.Checkbox("Allow HQ Purchases", ref enableHighQuality))
        {
            plugin.Configuration.EnableHighQualityPurchases = enableHighQuality;
            plugin.SaveConfiguration();
        }
        PluginControls.DrawHelpMarker("Off: NQ only. On: NQ or HQ.");

        var reduceMotion = plugin.Configuration.ReduceMotion;
        if (ImGui.Checkbox("Reduce Motion", ref reduceMotion))
        {
            plugin.Configuration.ReduceMotion = reduceMotion;
            plugin.SaveConfiguration();
        }
        PluginControls.DrawHelpMarker("Static highlight; less auto-scrolling.");

        if (shoppingController.IsRunning)
            ImGui.EndDisabled();
    }

    private static float GetNumberInputWidth()
        => Math.Clamp(ImGui.GetContentRegionAvail().X * 0.45f, 160f, 240f);

}
