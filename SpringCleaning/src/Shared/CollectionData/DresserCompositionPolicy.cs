using System.Collections.Immutable;

namespace SpringCleaning.Shared.CollectionData;

internal static class DresserCompositionPolicy
{
    internal static bool IsReady(CollectionDataSnapshot? snapshot) =>
        snapshot?.GlamourDresser.Status == CollectionSourceStatus.Fresh
        && snapshot.DresserEntries.HasValue;

    internal static IReadOnlyList<CollectionDresserEntry> TransferEntries(
        CollectionDataSnapshot? snapshot, IReadOnlySet<uint> cabinetItemIds)
    {
        if (!IsReady(snapshot))
            return [];

        return snapshot!.DresserEntries!.Value
            .Select(entry => entry with
            {
                Components = entry.Components.Where(component =>
                    cabinetItemIds.Contains(component.ItemId)
                    && CollectionOwnershipPolicy.CanPlanTransfer(snapshot,
                        CollectionSourceName.GlamourDresser, CollectionSourceName.Armoire,
                        component.ItemId)).ToImmutableArray(),
            })
            .Where(entry => entry.Components.Length != 0)
            .ToArray();
    }

    internal static int? ExistingOutfit(CollectionDataSnapshot? snapshot, uint outfitId, bool highQuality) =>
        IsReady(snapshot)
            ? snapshot!.DresserEntries!.Value.FirstOrDefault(entry => entry.OutfitId == outfitId
                && (entry.RawItemId >= 1_000_000) == highQuality)?.EntryIndex
            : null;

    internal static bool CanStore(CollectionDataSnapshot? snapshot, DresserStorageSource source,
        uint itemId, bool armoireEligible) =>
        IsReady(snapshot)
        && CollectionOwnershipPolicy.CanPlanDresserStorage(snapshot,
            source == DresserStorageSource.Inventory
                ? CollectionSourceName.Inventory : CollectionSourceName.ArmoryChest,
            itemId, armoireEligible);
}
