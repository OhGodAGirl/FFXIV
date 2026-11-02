using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using SpringCleaning.Shared.CollectionData;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning;

internal sealed class DresserScanner
{
    public const uint GlamourPrismItemId = DresserPrismPolicy.ItemId;
    private readonly IDataManager dataManager;
    private readonly Dictionary<uint, uint> cabinetRowByItemId;
    private static readonly InventoryType[] InventoryBagTypes =
                [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] ArmouryInventoryTypes =
            {
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    };

    public DresserScanner(IDataManager dataManager)
    {
        this.dataManager = dataManager;
        cabinetRowByItemId = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>()
            .Where(row => row.RowId != 0 && row.Item.RowId != 0)
            .GroupBy(row => row.Item.RowId)
            .ToDictionary(group => group.Key, group => group.First().RowId);
    }

    public unsafe bool IsDresserLoaded
    {
        get
        {
            var manager = MirageManager.Instance();
            return dataManager is not null && manager != null && manager->PrismBoxLoaded;
        }
    }

    public unsafe bool IsCabinetLoaded
    {
        get
        {
            var uiState = UIState.Instance();
            return uiState != null && uiState->Cabinet.IsCabinetLoaded();
        }
    }

    public IReadOnlyList<EligibleDresserItem> PlanEligibleDresserItems(
        CollectionDataSnapshot? collectionSnapshot)
    {
        var itemSheet = dataManager.GetExcelSheet<Item>();
        string Name(uint id) => itemSheet.TryGetRow(id, out var item) && !item.Name.IsEmpty
            ? item.Name.ToString() : $"Item {id}";
        return DresserCompositionPolicy.TransferEntries(collectionSnapshot, cabinetRowByItemId.Keys.ToHashSet())
            .Select(entry => new EligibleDresserItem(entry.EntryIndex, entry.RawItemId, Name(entry.BaseItemId),
                entry.Components.Select(component => new EligibleDresserComponent(
                    entry.OutfitId.HasValue ? component.SlotIndex : -1,
                    component.ItemId, entry.OutfitId is null && IsHighQuality(entry.RawItemId),
                    cabinetRowByItemId[component.ItemId], Name(component.ItemId))).ToArray()))
            .ToArray();
    }

    // Native reads can remove unavailable pieces from an authorized provider plan.
    // They never discover additional entries or infer missing outfit components.
    public unsafe IReadOnlyList<EligibleDresserItem> RevalidateEligibleItemsForAction(
        CollectionDataSnapshot? snapshot, IReadOnlySet<uint> plannedItemIds)
    {
        var result = new List<EligibleDresserItem>();
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return result;
        foreach (var planned in PlanEligibleDresserItems(snapshot))
        {
            if ((uint)planned.PrismBoxIndex >= manager->PrismBoxItemIds.Length
                || manager->PrismBoxItemIds[planned.PrismBoxIndex] != planned.RawItemId)
                continue;
            var components = new List<EligibleDresserComponent>();
            foreach (var component in planned.Components)
            {
                if (plannedItemIds.Contains(component.ItemId)
                    && !IsInCabinet(component.CabinetRowId)
                    && (component.OutfitSlot < 0
                        || manager->IsSetSlotUnlocked((uint)planned.PrismBoxIndex, component.OutfitSlot)))
                    components.Add(component);
            }
            if (components.Count != 0)
                result.Add(planned with { Components = components });
        }
        return result;
    }

    public unsafe bool StillPresent(EligibleDresserItem candidate)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return false;
        if ((uint)candidate.PrismBoxIndex >= manager->PrismBoxItemIds.Length)
            return false;

        if (manager->PrismBoxItemIds[candidate.PrismBoxIndex] != candidate.RawItemId)
            return false;

        return !candidate.IsOutfit
            || candidate.Components.Any(component =>
                manager->IsSetSlotUnlocked((uint)candidate.PrismBoxIndex, component.OutfitSlot));
    }

    public unsafe bool TryRestore(EligibleDresserItem candidate)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return false;

        if (!candidate.IsOutfit)
            return manager->RestorePrismBoxItem((uint)candidate.PrismBoxIndex);

        var restoreBits = stackalloc byte[2];
        restoreBits[0] = 0;
        restoreBits[1] = 0;
        foreach (var component in candidate.Components)
            restoreBits[component.OutfitSlot / 8] |= (byte)(1 << (component.OutfitSlot % 8));
        return manager->RestorePrismBoxSetItem((uint)candidate.PrismBoxIndex, restoreBits);
    }

    public unsafe bool TryRestore(DresserAlertEntry entry)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return false;

        var prismBoxIndex = entry.PrismBoxIndex;
        if (prismBoxIndex < 0
            || (uint)prismBoxIndex >= manager->PrismBoxItemIds.Length
            || manager->PrismBoxItemIds[prismBoxIndex] != entry.RawItemId)
            return false;
        if (!entry.IsOutfit)
            return manager->RestorePrismBoxItem((uint)prismBoxIndex);
        if (entry.Components.Count == 0)
            return false;
        var restoreBits = stackalloc byte[2];
        restoreBits[0] = 0;
        restoreBits[1] = 0;
        foreach (var component in entry.Components)
        {
            if (!manager->IsSetSlotUnlocked((uint)prismBoxIndex, component.SlotIndex))
                return false;
            restoreBits[component.SlotIndex / 8] |= (byte)(1 << (component.SlotIndex % 8));
        }
        return manager->RestorePrismBoxSetItem((uint)prismBoxIndex, restoreBits);
    }

    internal unsafe bool IsRestorationComplete(DresserAlertEntry entry)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded || entry.PrismBoxIndex < 0
            || entry.PrismBoxIndex >= manager->PrismBoxItemIds.Length)
            return false;
        // A different nonempty entry is an ambiguous replacement, not restore completion.
        var current = manager->PrismBoxItemIds[entry.PrismBoxIndex];
        if (current == 0)
            return true;
        if (!entry.IsOutfit || current != entry.RawItemId || entry.Components.Count == 0)
            return false;
        foreach (var component in entry.Components)
            if (manager->IsSetSlotUnlocked((uint)entry.PrismBoxIndex, component.SlotIndex))
                return false;
        return true;
    }

    internal unsafe bool AreRestorationBagsLoaded()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;
        foreach (var type in InventoryBagTypes)
        {
            var bag = manager->GetInventoryContainer(type);
            if (bag == null || !bag->IsLoaded)
                return false;
        }
        return true;
    }

    public IReadOnlyList<DresserAlertEntry> PlanAlerts(CollectionDataSnapshot? collectionSnapshot)
    {
        if (!DresserCompositionPolicy.IsReady(collectionSnapshot))
        {
            return Array.Empty<DresserAlertEntry>();
        }

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var categorySheet = dataManager.GetExcelSheet<ItemUICategory>();
        var lookupSheet = dataManager.GetExcelSheet<MirageStoreSetItemLookup>();
        var alerts = new List<DresserAlertEntry>();

        foreach (var entry in collectionSnapshot!.DresserEntries!.Value)
        {
            var itemId = entry.BaseItemId;
            if (!itemSheet.TryGetRow(itemId, out var item) || item.ItemUICategory.RowId == 0)
                continue;

            var belongsToOutfit = entry.OutfitId.HasValue || lookupSheet.TryGetRow(itemId, out var lookup)
                && lookup.Item.Any(reference => reference.RowId != 0);
            DresserAlertKind kind;
            if (entry.Components.Any(component => cabinetRowByItemId.ContainsKey(component.ItemId)))
                kind = DresserAlertKind.ArmoireEligible;
            else if (belongsToOutfit)
                kind = DresserAlertKind.OutfitConvertible;
            else
                kind = DresserAlertKind.NotInOutfit;

            var categoryName = categorySheet.TryGetRow(item.ItemUICategory.RowId, out var category)
                ? category.Name.ToString()
                : "Other";
            var alreadyInArmoire = entry.Components.All(component => CollectionOwnershipPolicy.GetFact(
                collectionSnapshot, CollectionSourceName.Armoire, component.ItemId) == CollectionOwnershipFact.Owned);

            alerts.Add(new DresserAlertEntry(
                entry.EntryIndex,
                entry.RawItemId,
                itemId,
                IsHighQuality(entry.RawItemId),
                entry.OutfitId.HasValue,
                item.ItemUICategory.RowId,
                categoryName,
                item.Name.ToString(),
                item.Icon,
                kind,
                alreadyInArmoire,
                false,
                belongsToOutfit)
            { Components = entry.Components });
        }

        return alerts;
    }

    public unsafe IReadOnlyList<CabinetInventoryItem> ScanEligibleInventoryItems(
        CollectionDataSnapshot? collectionSnapshot,
        IReadOnlySet<uint>? allowedItemIds = null)
    {
        if (!IsCabinetLoaded)
            return Array.Empty<CabinetInventoryItem>();

        if (TryScanNativeArmoireStoreItems(collectionSnapshot, allowedItemIds, out var nativeItems))
            return nativeItems;

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return Array.Empty<CabinetInventoryItem>();
        var itemSheet = dataManager.GetExcelSheet<Item>();
        var result = new List<CabinetInventoryItem>();
        var seenCabinetRows = new HashSet<uint>();

        for (var containerIndex = 0; containerIndex < 4; containerIndex++)
        {
            var container = inventory->GetInventoryContainer((InventoryType)containerIndex);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || slot->IsEmpty())
                    continue;

                var itemId = slot->GetBaseItemId();
                if (allowedItemIds is not null && !allowedItemIds.Contains(itemId))
                    continue;
                if (!cabinetRowByItemId.TryGetValue(itemId, out var cabinetRowId)
                    || !CollectionOwnershipPolicy.CanPlanTransfer(
                        collectionSnapshot,
                        CollectionSourceName.Inventory,
                        CollectionSourceName.Armoire,
                        itemId)
                    || !seenCabinetRows.Add(cabinetRowId)
                    || !itemSheet.TryGetRow(itemId, out var item)
                    )
                {
                    continue;
                }

                result.Add(new CabinetInventoryItem(
                    cabinetRowId,
                    itemId,
                    slot->IsHighQuality(),
                    item.Name.ToString(),
                    item.Icon));
            }
        }

        return result;
    }

    public CabinetInventoryItem? FindNextInventoryCabinetItem(CollectionDataSnapshot? collectionSnapshot)
    {
        var items = ScanEligibleInventoryItems(collectionSnapshot);
        return items.Count == 0 ? null : items[0];
    }

    public unsafe bool AreDresserInventoryCandidatesReady
    {
        get
        {
            var agent = AgentMiragePrismPrismBox.Instance();
            return agent != null
                && agent->Data != null
                && !agent->Data->IsPopulatingList
                && agent->Data->IsPopulatingComplete;
        }
    }

    public unsafe IReadOnlyList<InventoryDresserOutfit> ScanEligibleInventoryDresserOutfits(
        CollectionDataSnapshot? collectionSnapshot,
        bool onlyCompleteOutfits = false,
        DresserStorageSource source = DresserStorageSource.Inventory)
                => PlanEligibleInventoryDresserOutfits(
        ScanEligibleInventoryDresserCandidates(collectionSnapshot, source),
        collectionSnapshot,
        onlyCompleteOutfits);

    public unsafe IReadOnlyList<InventoryDresserCandidate> ScanEligibleInventoryDresserCandidates(
        CollectionDataSnapshot? collectionSnapshot,
        DresserStorageSource source = DresserStorageSource.Inventory,
        int sourceCategoryIndex = NativeUi.InventoryDresserCategory)
    {
        var manager = MirageManager.Instance();
        var agent = AgentMiragePrismPrismBox.Instance();
        var inventory = InventoryManager.Instance();
        if (manager == null
            || !manager->PrismBoxLoaded
            || agent == null
            || agent->Data == null
            || inventory == null)
        {
            return Array.Empty<InventoryDresserCandidate>();
        }

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var setSheet = dataManager.GetExcelSheet<MirageStoreSetItem>();
        var lookupSheet = dataManager.GetExcelSheet<MirageStoreSetItemLookup>();
        var candidates = new List<InventoryDresserCandidate>();
        var count = Math.Min((int)agent->Data->CrystallizeItemCount, 140);
        for (var index = 0; index < count; index++)
        {
            var listed = agent->Data->CrystallizeItems[index];
            if (listed.ItemId == 0 || !IsDresserStorageSource(listed.Inventory, source))
            {
                continue;
            }

            var container = inventory->GetInventoryContainer(listed.Inventory);
            if (container == null || !container->IsLoaded || listed.Slot < 0 || listed.Slot >= container->Size)
                continue;

            var slot = container->GetInventorySlot(listed.Slot);
            if (slot == null || slot->IsEmpty())
                continue;

            var itemId = slot->GetBaseItemId();
            var isArmoireEligible = cabinetRowByItemId.ContainsKey(itemId);
            var collectionSource = source == DresserStorageSource.Inventory
                ? CollectionSourceName.Inventory
                : CollectionSourceName.ArmoryChest;
            if (itemId != listed.ItemId
                || !CollectionOwnershipPolicy.CanPlanDresserStorage(
                    collectionSnapshot,
                    collectionSource,
                    itemId,
                    isArmoireEligible)
                || !InventoryRoutingPlanner.ShouldOfferDresser(
                    // CrystallizeItems is the game's own validated storage list.
                    // Item wear/customization is reset by the native confirmation
                    // and must not make otherwise eligible pieces disappear here.
                    true,
                    true,
                    false,
                    isArmoireEligible,
                    false))
            {
                continue;
            }

            var name = itemSheet.TryGetRow(itemId, out var item) && !item.Name.IsEmpty
                ? item.Name.ToString()
                : $"Item {itemId}";
            var sets = lookupSheet.TryGetRow(itemId, out var lookup)
                ? lookup.Item
                    .Select(reference => reference.RowId)
                    .Where(setId => setId != 0 && setSheet.HasRow(setId))
                    .Distinct()
                    .ToArray()
                : Array.Empty<uint>();
            candidates.Add(new InventoryDresserCandidate(new InventoryDresserComponent(
                (int)listed.Inventory,
                listed.Slot,
                -1,
                itemId,
                slot->IsHighQuality(),
                name,
                itemSheet.TryGetRow(itemId, out item) ? item.Icon : 0u,
                sourceCategoryIndex), sets));
        }

        return candidates;
    }

    public unsafe IReadOnlyList<InventoryDresserOutfit> PlanEligibleInventoryDresserOutfits(
        IReadOnlyList<InventoryDresserCandidate> candidates,
        CollectionDataSnapshot? collectionSnapshot,
        bool onlyCompleteOutfits = false)
    {
        var itemSheet = dataManager.GetExcelSheet<Item>();
        var setSheet = dataManager.GetExcelSheet<MirageStoreSetItem>();

        var outfitCandidates = candidates
            .Where(candidate => candidate.OutfitSetIds.Count > 0
                && CanStoreDresserItem(collectionSnapshot, candidate.Component.ItemId,
                    IsDresserStorageSource((InventoryType)candidate.Component.InventoryTypeId, DresserStorageSource.Inventory)
                        ? DresserStorageSource.Inventory : DresserStorageSource.ArmouryChest))
            .DistinctBy(candidate => candidate.Component.ItemId)
            .DistinctBy(candidate => (
                candidate.Component.InventoryTypeId,
                candidate.Component.SlotIndex,
                candidate.Component.ItemId))
            .ToArray();
        var existingSets = collectionSnapshot?.DresserEntries?
            .Where(entry => entry.OutfitId.HasValue)
            .Select(entry => (SetId: entry.OutfitId!.Value, IsHighQuality: IsHighQuality(entry.RawItemId)))
            .ToArray() ?? [];
        var assigned = InventoryOutfitSetPlanner.BuildBatches(outfitCandidates, existingSets);

        var result = new List<InventoryDresserOutfit>();
        foreach (var group in assigned)
        {
            if (!setSheet.TryGetRow(group.SetId, out var set))
                continue;

            var setItems = GetSetItemIds(set);
            var components = group.Components
                .Select(entry =>
                {
                    var outfitSlot = Array.IndexOf(setItems, entry.ItemId);
                    return outfitSlot < 0 ? null : entry with { OutfitSlot = outfitSlot };
                })
                .Where(component => component is not null)
                .Select(component => component!)
                .OrderBy(component => component.OutfitSlot)
                .ToArray();
            if (components.Length == 0)
                continue;
            var completeOutfitPieceCount = setItems.Count(itemId => itemId != 0);
            if (onlyCompleteOutfits
                && components.Select(component => component.ItemId).Distinct().Count()
                    != completeOutfitPieceCount)
            {
                continue;
            }

            var setName = itemSheet.TryGetRow(group.SetId, out var setItem) && !setItem.Name.IsEmpty
                ? setItem.Name.ToString()
                : $"Outfit {group.SetId}";
            result.Add(new InventoryDresserOutfit(group.SetId, DresserCompositionPolicy.ExistingOutfit(collectionSnapshot, group.SetId, group.IsHighQuality), setName, components));
        }

        return result;
    }

    public IReadOnlyList<InventoryDresserItem> ScanEligibleInventoryDresserItems(
        CollectionDataSnapshot? collectionSnapshot,
        DresserStorageSource source = DresserStorageSource.Inventory)
                => PlanEligibleInventoryDresserItems(
        ScanEligibleInventoryDresserCandidates(collectionSnapshot, source));

    public bool CanStoreDresserItem(CollectionDataSnapshot? snapshot, uint itemId, DresserStorageSource source) =>
        DresserCompositionPolicy.CanStore(snapshot, source, itemId, cabinetRowByItemId.ContainsKey(itemId));

    public unsafe bool RevalidateOutfitDestination(InventoryDresserOutfit outfit)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return false;
        if (outfit.ExistingPrismBoxIndex is not int index)
            return true;
        return (uint)index < manager->PrismBoxItemIds.Length
            && GetBaseItemId(manager->PrismBoxItemIds[index]) == outfit.SetItemId
            && outfit.Components.Count > 0
            && IsHighQuality(manager->PrismBoxItemIds[index]) == outfit.Components[0].IsHighQuality;
    }

    public bool CanStoreOutfit(CollectionDataSnapshot? snapshot, InventoryDresserOutfit outfit, DresserStorageSource source) =>
        outfit.Components.Count != 0
        && outfit.Components.All(component => component.IsHighQuality == outfit.Components[0].IsHighQuality)
        && RevalidateOutfitDestination(outfit)
        && DresserCompositionPolicy.ExistingOutfit(snapshot, outfit.SetItemId, outfit.Components[0].IsHighQuality) == outfit.ExistingPrismBoxIndex
        && outfit.Components.All(component => CanStoreDresserCopy(snapshot,
            new InventoryDresserItem(component.InventoryTypeId, component.SlotIndex, component.ItemId,
                component.IsHighQuality, component.Name, component.IconId), source));

    public unsafe bool CanStoreDresserCopy(CollectionDataSnapshot? snapshot, InventoryDresserItem candidate, DresserStorageSource source)
                => CanStoreDresserItem(snapshot, candidate.ItemId, source)
        && CanUseDresserCopy(snapshot, candidate, source);

    public bool CanOpenDresserFallback(CollectionDataSnapshot? snapshot, InventoryDuplicateItem candidate) =>
        CollectionOwnershipPolicy.CanOpenDresserFallback(snapshot, candidate)
        && CanUseDresserCopy(snapshot, new InventoryDresserItem(candidate.InventoryTypeId, candidate.SlotIndex,
            candidate.ItemId, candidate.IsHighQuality, candidate.Name, candidate.IconId), DresserStorageSource.Inventory);

    public static IReadOnlyList<InventoryDresserItem> PlanEligibleInventoryDresserItems(
        IReadOnlyList<InventoryDresserCandidate> candidates) => candidates
        .Where(candidate => candidate.OutfitSetIds.Count == 0)
        .DistinctBy(candidate => candidate.Component.ItemId)
        .Select(candidate => new InventoryDresserItem(
            candidate.Component.InventoryTypeId,
            candidate.Component.SlotIndex,
            candidate.Component.ItemId,
            candidate.Component.IsHighQuality,
            candidate.Component.Name,
            candidate.Component.IconId,
            candidate.Component.SourceCategoryIndex))
        .ToArray();

    public bool IsArmoireEligible(uint itemId) => cabinetRowByItemId.ContainsKey(itemId);

    public uint GetCabinetRowId(uint itemId) => cabinetRowByItemId.GetValueOrDefault(itemId);

    public IReadOnlyList<InventoryDuplicateItem> ScanStoredInventoryDuplicates(
        CollectionDataSnapshot? collectionSnapshot, bool protectMateria, bool protectSpiritbond)
        => ScanDuplicateCopies(collectionSnapshot, protectMateria, protectSpiritbond, null);

    internal IReadOnlyList<InventoryDuplicateItem> CaptureRestorationCopies(CollectionDataSnapshot snapshot,
        IReadOnlySet<uint> itemIds, bool protectMateria, bool protectSpiritbond)
        => ScanDuplicateCopies(snapshot, protectMateria, protectSpiritbond, itemIds);

    private unsafe IReadOnlyList<InventoryDuplicateItem> ScanDuplicateCopies(
        CollectionDataSnapshot? collectionSnapshot, bool protectMateria, bool protectSpiritbond,
        IReadOnlySet<uint>? restorationIds)
    {
        if (collectionSnapshot is null)
            return Array.Empty<InventoryDuplicateItem>();

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return Array.Empty<InventoryDuplicateItem>();

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var duplicates = new List<InventoryDuplicateItem>();
        foreach (var inventoryType in InventoryBagTypes.Concat(ArmouryInventoryTypes))
        {
            if (restorationIds is not null && !InventoryBagTypes.Contains(inventoryType))
                continue;
            var source = InventoryBagTypes.Contains(inventoryType)
                ? CollectionSourceName.Inventory : CollectionSourceName.ArmoryChest;
            var container = inventory->GetInventoryContainer(inventoryType);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slotIndex = 0; slotIndex < container->Size; slotIndex++)
            {
                var slot = container->GetInventorySlot(slotIndex);
                if (slot == null || slot->IsEmpty())
                    continue;

                var itemId = slot->GetBaseItemId();
                if (!itemSheet.TryGetRow(itemId, out var item))
                    continue;

                if (restorationIds is null
                    ? !CollectionOwnershipPolicy.CanPlanInventoryReview(collectionSnapshot, itemId, source)
                    : !restorationIds.Contains(itemId))
                    continue;

                var isInArmoire = CollectionOwnershipPolicy.GetFact(
                    collectionSnapshot,
                    CollectionSourceName.Armoire,
                    itemId) == CollectionOwnershipFact.Owned;
                var isInDresser = CollectionOwnershipPolicy.GetFact(
                    collectionSnapshot,
                    CollectionSourceName.GlamourDresser,
                    itemId) == CollectionOwnershipFact.Owned;

                var disposition = InventoryDuplicatePlanner.GetDisposition(
                    isInArmoire,
                    isInDresser);
                if (disposition == InventoryDuplicateDisposition.None)
                    continue;

                duplicates.Add(new InventoryDuplicateItem(
                    (int)inventoryType,
                    slotIndex,
                    itemId,
                    slot->IsHighQuality(),
                    item.Name.ToString(),
                    item.Icon,
                    disposition == InventoryDuplicateDisposition.DresserFallback
                        ? new InventoryItemSafety(true, GetUnsafeStorageReason(slot) is null, GetUnsafeStorageReason(slot) ?? "Ready")
                        : CreateDiscardSafety(slot, protectMateria, protectSpiritbond),
                    isInArmoire,
                    isInDresser,
                    disposition)
                { Source = source, ProviderContentId = collectionSnapshot.ContentId, ProviderSessionId = collectionSnapshot.ProviderSessionId });
            }
        }

        return duplicates;
    }

    public unsafe bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate, bool protectMateria, bool protectSpiritbond)
    {
        // Source identity must agree with the physical slot; equipped gear is never a candidate.
        var expectedTypes = candidate.Source == CollectionSourceName.Inventory ? InventoryBagTypes
            : candidate.Source == CollectionSourceName.ArmoryChest ? ArmouryInventoryTypes : [];
        if (!expectedTypes.Contains((InventoryType)candidate.InventoryTypeId))
            return false;

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return false;

        var container = inventory->GetInventoryContainer((InventoryType)candidate.InventoryTypeId);
        if (container == null
            || !container->IsLoaded
            || candidate.SlotIndex < 0
            || candidate.SlotIndex >= container->Size)
        {
            return false;
        }

        var item = container->GetInventorySlot(candidate.SlotIndex);
        if (item == null
            || item->IsEmpty()
            || item->GetBaseItemId() != candidate.ItemId
            || item->IsHighQuality() != candidate.IsHighQuality)
        {
            return false;
        }

        var isInArmoire = IsCabinetLoaded
            && cabinetRowByItemId.TryGetValue(candidate.ItemId, out var cabinetRowId)
            && IsInCabinet(cabinetRowId);
        var isInDresser = IsAnyQualityStoredInDresser(candidate.ItemId);
        var current = candidate with
        {
            Safety = CreateDiscardSafety(item, protectMateria, protectSpiritbond),
            IsInArmoire = isInArmoire,
            IsInDresser = isInDresser,
            Disposition = InventoryDuplicatePlanner.GetDisposition(isInArmoire, isInDresser),
        };
        return InventoryDuplicatePlanner.CanBulkDiscard(current);
    }

    public unsafe bool IsItemStoredInDresser(uint rawItemId)
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return false;

        var baseItemId = GetBaseItemId(rawItemId);
        var quality = IsHighQuality(rawItemId);
        var setSheet = dataManager.GetExcelSheet<MirageStoreSetItem>();
        for (var index = 0; index < manager->PrismBoxItemIds.Length; index++)
        {
            var storedRawId = manager->PrismBoxItemIds[index];
            if (storedRawId == rawItemId)
                return true;
            if (storedRawId == 0 || IsHighQuality(storedRawId) != quality)
                continue;
            if (!setSheet.TryGetRow(GetBaseItemId(storedRawId), out var set))
                continue;

            var components = GetSetItemIds(set);
            for (var slot = 0; slot < components.Length; slot++)
            {
                if (components[slot] == baseItemId
                    && manager->IsSetSlotUnlocked((uint)index, slot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsAnyQualityStoredInDresser(uint itemId) =>
        IsItemStoredInDresser(itemId) || IsItemStoredInDresser(itemId + 1_000_000);

    public unsafe bool IsInCabinet(uint cabinetRowId)
    {
        var uiState = UIState.Instance();
        return uiState != null
            && uiState->Cabinet.IsCabinetLoaded()
            && uiState->Cabinet.IsItemInCabinet(cabinetRowId);
    }

    public unsafe bool TryStore(uint cabinetRowId)
    {
        var uiState = UIState.Instance();
        return uiState != null
            && uiState->Cabinet.IsCabinetLoaded()
            && uiState->Cabinet.StoreCabinetItem(cabinetRowId);
    }

    public unsafe uint GetEmptyInventorySlots()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetEmptySlotsInBag();
    }

    public unsafe uint GetInventoryItemCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 0;

        uint count = 0;
        for (var containerIndex = 0; containerIndex < 4; containerIndex++)
        {
            var type = (InventoryType)containerIndex;
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && !item->IsEmpty() && item->GetBaseItemId() == itemId)
                    count += item->GetQuantity();
            }
        }

        return count;
    }

    public unsafe bool TryGetGlamourPrismCount(out uint count)
    {
        var inventoryCount = GetInventoryItemCount(GlamourPrismItemId);
        var currencyManager = CurrencyManager.Instance();
        var trackedCurrencyCount = currencyManager == null
            ? 0u
            : currencyManager->GetItemCount(GlamourPrismItemId);
        uint? nativeGlamourCreationCount = null;
        var outfitAgent = AgentMiragePrismPrismSetConvert.Instance();
        if (outfitAgent != null && outfitAgent->Data != null && outfitAgent->IsAgentActive())
            nativeGlamourCreationCount = outfitAgent->Data->GlamourPrismCount;

        count = DresserPrismPolicy.ResolveHeld(
            inventoryCount,
            trackedCurrencyCount,
            nativeGlamourCreationCount);
        // The outfit agent can remain active with stale zero-valued data before
        // a real Glamour Creation dialog is populated. Zero is therefore an
        // unknown preflight value; the native storage dialog remains the final
        // authority when an account genuinely has no prisms.
        return DresserPrismPolicy.IsReliablePreflightCount(count);
    }

    public unsafe uint GetDresserSourceItemCount(uint itemId, DresserStorageSource source)
    {
        if (source == DresserStorageSource.Inventory)
            return GetInventoryItemCount(itemId);

        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 0;

        uint count = 0;
        foreach (var type in ArmouryInventoryTypes)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && !item->IsEmpty() && item->GetBaseItemId() == itemId)
                    count += item->GetQuantity();
            }
        }

        return count;
    }

    internal static bool IsDresserStorageSource(
        InventoryType inventoryType,
        DresserStorageSource source) => source switch
        {
            DresserStorageSource.Inventory =>
                inventoryType is >= InventoryType.Inventory1 and <= InventoryType.Inventory4,
            DresserStorageSource.ArmouryChest => ArmouryInventoryTypes.Contains(inventoryType),
            _ => false,
        };

    internal static unsafe bool? IsInGearset(InventoryItem* item)
    {
        var gearsets = RaptureGearsetModule.Instance();
        return gearsets == null ? null : gearsets->IsItemRegisteredToGearset(item, null, 14);
    }

    private unsafe bool TryScanNativeArmoireStoreItems(
        CollectionDataSnapshot? collectionSnapshot,
        IReadOnlySet<uint>? allowedItemIds,
        out IReadOnlyList<CabinetInventoryItem> result)
    {
        result = Array.Empty<CabinetInventoryItem>();
        var stage = AtkStage.Instance();
        var agent = AgentCabinet.Instance();
        if (stage == null
            || agent == null
            || !agent->IsAgentActive())
        {
            return false;
        }

        var numberArray = stage->GetNumberArrayData(NumberArrayType.CabinetStore);
        if (numberArray == null
            || numberArray->IntArray == null
            || agent->Items == null)
        {
            return false;
        }

        var itemSheet = dataManager.GetExcelSheet<Item>();
        var cabinetSheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>();
        var items = new List<CabinetInventoryItem>();
        var seenCabinetRows = new HashSet<uint>();
        var itemCount = Math.Clamp(numberArray->IntArray[0], 0, 140);
        for (var index = 0; index < itemCount; index++)
        {
            var agentItemIndex = numberArray->IntArray[12 + index * 7];
            if (agentItemIndex < 0)
                continue;

            var cabinetRowId = agent->Items[agentItemIndex].Id;
            if (cabinetRowId == 0
                || !seenCabinetRows.Add(cabinetRowId)
                || !cabinetSheet.TryGetRow(cabinetRowId, out var cabinetRow))
            {
                continue;
            }

            var itemId = cabinetRow.Item.RowId;
            if (itemId == 0
                || allowedItemIds is not null && !allowedItemIds.Contains(itemId)
                || !CollectionOwnershipPolicy.CanPlanTransfer(
                    collectionSnapshot,
                    CollectionSourceName.Inventory,
                    CollectionSourceName.Armoire,
                    itemId)
                || !itemSheet.TryGetRow(itemId, out var item))
            {
                continue;
            }

            items.Add(new CabinetInventoryItem(
                cabinetRowId,
                itemId,
                false,
                item.Name.ToString(),
                item.Icon));
        }

        result = items;
        return true;
    }

    private unsafe bool CanUseDresserCopy(CollectionDataSnapshot? snapshot, InventoryDresserItem candidate, DresserStorageSource source)
    {
        if (!IsDresserStorageSource((InventoryType)candidate.InventoryTypeId, source)
            || !InventoryCopyAuthority.Matches(snapshot?.InventoryObservation,
                source == DresserStorageSource.Inventory ? "Inventory" : "ArmoryChest", candidate.ItemId,
                GetSourceQualityCount(candidate.ItemId, source, false), GetSourceQualityCount(candidate.ItemId, source, true))
            || IsAnyQualityStoredInDresser(candidate.ItemId))
            return false;
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return false;
        var container = inventory->GetInventoryContainer((InventoryType)candidate.InventoryTypeId);
        if (container == null || !container->IsLoaded || candidate.SlotIndex < 0 || candidate.SlotIndex >= container->Size)
            return false;
        var item = container->GetInventorySlot(candidate.SlotIndex);
        return item != null && !item->IsEmpty() && item->GetBaseItemId() == candidate.ItemId
            && item->IsHighQuality() == candidate.IsHighQuality && GetUnsafeStorageReason(item) is null;
    }

    private static unsafe uint? GetSourceQualityCount(uint itemId, DresserStorageSource source, bool highQuality)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return null;
        uint count = 0;
        var types = source == DresserStorageSource.Inventory
            ? InventoryBagTypes : ArmouryInventoryTypes;
        foreach (var type in types)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                return null;
            for (var index = 0; index < container->Size; index++)
            {
                var item = container->GetInventorySlot(index);
                if (item != null && !item->IsEmpty() && item->GetBaseItemId() == itemId && item->IsHighQuality() == highQuality)
                    count = checked(count + item->GetQuantity());
            }
        }
        return count;
    }

    private static unsafe string? GetUnsafeStorageReason(InventoryItem* item) =>
        InventoryItemSafetyPlanner.StorageReason(item->IsSymbolic, item->GetQuantity(), IsInGearset(item));

    private static unsafe InventoryItemSafety CreateDiscardSafety(InventoryItem* item, bool protectMateria, bool protectSpiritbond)
    {
        var symbolic = item->IsSymbolic;
        var collectable = !symbolic && item->IsCollectable();
        // Only protected discard reads these local fields. They never enter Exportaramus.
        var readProtection = (protectMateria || protectSpiritbond) && !symbolic && !collectable;
        return InventoryItemSafetyPlanner.Discard(symbolic, item->GetQuantity(), IsInGearset(item), protectMateria, protectSpiritbond,
            readProtection ? item->GetMateriaCount() != 0 : null,
            readProtection ? item->GetSpiritbondOrCollectability() : null, collectable);
    }

    private static uint GetBaseItemId(uint rawItemId) => rawItemId % 1_000_000;

    private static bool IsHighQuality(uint rawItemId) => rawItemId >= 1_000_000;

    private static uint[] GetSetItemIds(MirageStoreSetItem set) =>
            [
                set.MainHand.RowId,
        set.OffHand.RowId,
        set.Head.RowId,
        set.Body.RowId,
        set.Hands.RowId,
        set.Legs.RowId,
        set.Feet.RowId,
        set.Earrings.RowId,
        set.Necklace.RowId,
        set.Bracelets.RowId,
        set.Ring.RowId,
    ];
}
