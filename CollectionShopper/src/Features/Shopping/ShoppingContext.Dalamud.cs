using Dalamud.Plugin.Services;

namespace CollectionShopper;

internal sealed class ShoppingContext(Plugin plugin, IPluginLog log) : IShoppingContext
{
    public IReadOnlyList<CollectionDataOwnershipSourceState> DiagnosticSources => plugin.DiagnosticSources;
    public ulong CurrentContentId => plugin.CurrentContentId;
    public uint CurrentWorldId => plugin.CurrentWorldId;
    public IReadOnlySet<uint> CompletedItemIds => plugin.PurchaseSuppressionIds;
    public ShoppingRunSettings Settings => new(
        plugin.Configuration.MaximumPricePerItem,
        plugin.Configuration.UnlimitedPricePerItem,
        plugin.Configuration.MaximumTotalSpend,
        plugin.Configuration.UnlimitedTotalSpend,
        plugin.Configuration.EnableHighQualityPurchases,
        plugin.Configuration.ActionDelayMilliseconds);

    public ItemOwnership ReadPurchaseReadiness(ShoppingItem item, ulong runContentId)
        => plugin.ReadPurchaseReadiness(item, runContentId);

    public void LogSearchRetry(ShoppingItem item, string reason)
        => log.Warning("Retrying listing search for {Item} ({ItemId}): {Reason}", item.ItemName, item.ItemId, reason);

    public void LogStopped(ShoppingItem item, string reason)
        => log.Warning("Shopping stopped at {Item} ({ItemId}): {Reason}", item.ItemName, item.ItemId, reason);

    public void MarkPurchased(uint itemId, ulong purchaseContentId)
        => plugin.MarkPurchased(itemId, purchaseContentId);

    public void LogRequested(ShoppingItem item, PurchaseListingCandidate listing)
        => log.Information(
            "Purchase requested once: {Item} ({ItemId}), listing {ListingId}, quality {Quality}, {Price} gil total.",
            item.ItemName, item.ItemId, listing.ListingId,
            listing.IsHighQuality ? "HQ" : "NQ", listing.TotalCost);

    public void LogConfirmed(ShoppingItem item, ulong listingId, uint quantity)
        => log.Information(
            "Purchase confirmed: {Item} ({ItemId}), listing {ListingId}, quantity {Quantity}.",
            item.ItemName, item.ItemId, listingId, quantity);

    public void LogSkipped(ShoppingItem item, string reason)
        => log.Warning("Shopping skipped {Item} ({ItemId}): {Reason}", item.ItemName, item.ItemId, reason);
}
