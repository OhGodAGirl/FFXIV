using Dalamud.Plugin.Services;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EZDelivery.Features.Shopping;

/// <summary>October 6 EXE-reviewed exchange ABI. Fingerprint approval is supplied by composition.</summary>
public sealed unsafe class ShoppingNative(IGameGui gui, IClientState client, IDataManager data, Func<bool> canMutate) : IShoppingRuntime
{
    private static readonly InventoryType[] CountContainers =
    [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
     InventoryType.EquippedItems, InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead,
     InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets,
     InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings];
    private long requestGeneration;
    private uint requestItem;
    private int requestRow = -1;
    private nint requestReceiver;
    private nint requestHandler;
    private readonly nint transactionCallback = TransactionCallbackAddress();
    private bool equipmentConfirmed;
    private bool exchangeConfirmed;
    private long confirmedGeneration;
    private int confirmedRow = -1;
    private ShopIdentity lastIdentity;
    /// <summary>Current Seal Exchange bounds, or null when unavailable.</summary>
    public ShopAnchor? Anchor { get; private set; }
    /// <summary>Latest purchase confirmation correlation diagnostic.</summary>
    public string LastDialogCheck { get; private set; } = "No pending purchase warning observed";

    /// <summary>Reads shop state, pending offer fields and owned confirmation facts.</summary>
    public PurchaseObservation Observe(ShopOffer? pending)
    {
        try { return Read(pending); }
        catch (Exception) { return Unavailable(lastIdentity, "Native shop data could not be read."); }
    }

    private PurchaseObservation Read(ShopOffer? pending)
    {
        Anchor = null;
        var player = PlayerState.Instance();
        var inventory = InventoryManager.Instance();
        if (player == null || !player->IsLoaded || player->ContentId == 0 || inventory == null)
        { lastIdentity = default; return Unavailable(default, "No character is available."); }
        var addon = (AtkUnitBase*)gui.GetAddonByName("GrandCompanyExchange").Address;
        var identity = new ShopIdentity(player->ContentId, client.TerritoryType, player->GrandCompany,
            player->GetGrandCompanyRank(), addon == null ? 0 : (ulong)addon->Id);
        lastIdentity = identity;
        var count = pending == null ? 0 : Count(inventory, pending.ItemId);
        var seals = identity.Company is >= 1 and <= 3 ? inventory->GetCompanySeals(identity.Company) : 0;
        if (addon == null || !addon->IsReady || !addon->IsVisible)
            return Unavailable(identity, "Open your Grand Company Seal Exchange.", seals, count);
        Anchor = new(addon->X, addon->Y, addon->GetScaledWidth(true), addon->GetScaledHeight(true));
        // The typed addon/agent structures do not cover these fields. Refuse all custom offsets on a new build.
        if (!canMutate()) return Unavailable(identity, "This game build has not been reviewed for native shopping.", seals, count);
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanyExchange);
        if (agent == null || !agent->IsAgentActive() || agent->AddonId != addon->Id || *((byte*)agent + 0x68) != identity.Company)
            return Unavailable(identity, "The shop does not match your Grand Company.", seals, count);
        var tier = *((byte*)agent + 0x69);
        var category = *((byte*)agent + 0x6a);
        if (tier > 2 || category > 3 || !Number(addon, 1, out var rows) || rows > 50 || !Number(addon, 2, out var visibleTier) || visibleTier != tier)
            return Unavailable(identity, "The native shop row mapping is inconsistent.", seals, count);
        var nativeRows = *(byte**)((byte*)addon + 0x3f0);
        var allocation = *(int*)((byte*)addon + 0x3f8);
        if (allocation < rows || allocation > 50 || (rows > 0 && nativeRows == null))
            return Unavailable(identity, "The native shop row storage is unavailable.", seals, count);
        var found = -1;
        uint liveId = 0, liveCost = 0;
        if (pending != null && tier == pending.Tier && category == pending.NativeCategory)
        {
            for (var i = 0; i < rows; i++)
            {
                if (!Number(addon, 317 + i, out var item) || !Number(addon, 67 + i, out var price) ||
                    !Number(addon, 167 + i, out var icon) || !Number(addon, 367 + i, out var rankIcon))
                    return Unavailable(identity, "The native offer fields are unreadable.", seals, count);
                var row = nativeRows + i * 0x30;
                if (*(uint*)(row + 0x20) != item || *(uint*)(row + 0x10) != price || *(uint*)(row + 0x18) != icon ||
                    *(uint*)(row + 0x1c) != rankIcon)
                    return Unavailable(identity, "The native list is still refreshing.", seals, count);
                if (item != pending.ItemId) continue;
                if (found != -1)
                    return Unavailable(identity, "The native shop contains duplicate matches for this item.", seals, count);
                if (price != pending.UnitCost)
                    return Unavailable(identity, $"The native price differs: shop {price}, catalog {pending.UnitCost}.", seals, count);
                // This is the shop's category glyph, not Item.Icon. Its equality to
                // the native backing row was checked above. Catalog identity uses
                // exact item, price and required rank; the currency dialog separately
                // uses the actual item artwork and is checked against Item.Icon below.
                // Value 367 is the company-specific rank glyph, not the numeric rank.
                var expectedRankIcon = ShopRankIcon.Expected(pending.Company, pending.RequiredRank);
                if (rankIcon != expectedRankIcon)
                    return Unavailable(identity, $"The native rank icon differs: shop {rankIcon}, expected {expectedRankIcon} (rank {pending.RequiredRank}).", seals, count);
                found = i;
                liveId = item;
                liveCost = price;
            }
        }
        PurchaseDialog? dialog = null;
        var currency = (AtkUnitBase*)gui.GetAddonByName("ShopExchangeCurrencyDialog").Address;
        if (currency != null && currency->IsVisible)
        {
            var correlated = pending != null && requestItem == pending.ItemId && requestRow == found &&
                *(uint*)((byte*)agent + 0x70) == currency->Id && *(int*)((byte*)agent + 0x6c) == found &&
                currency->ParentId == addon->Id && currency->IsReady && found >= 0;
            uint total = 0, quantity = 0;
            if (correlated && Number(currency, 2, out var unitCost) && Number(currency, 1, out var balance) &&
                Number(currency, 3, out var icon) && icon == pending!.IconId && balance == seals && unitCost == pending.UnitCost)
            {
                var input = *(byte**)((byte*)currency + 0x258);
                quantity = input == null ? 0 : *(uint*)(input + 0x1f8);
                correlated = quantity == 1 && *(uint*)((byte*)currency + 0x280) == unitCost;
                total = correlated ? unitCost : 0;
            }
            else correlated = false;
            dialog = new(currency->Id, correlated, correlated ? pending!.ItemId : 0, quantity, total);
        }
        var yesno = (AddonSelectYesno*)gui.GetAddonByName("SelectYesno").Address;
        if (yesno != null && yesno->IsVisible)
        {
            var receiver = *(nint*)((byte*)agent + 0x60);
            var ownerMatches = receiver != 0 && receiver == requestReceiver && requestHandler != 0 && *(nint*)(receiver + 0x10) == requestHandler;
            var callback = false;
            var ui = UIModule.Instance();
            if (ownerMatches && ui != null)
            {
                var module = ui->GetRaptureAtkModule();
                callback = module != null && module->AtkModule.AddonCallbackMapping.TryGetValue(yesno->Id, out var entry, false) &&
                    (nint)entry.EventInterface == transactionCallback && transactionCallback != 0 && entry.EventKind == 0;
            }
            var transaction = callback ? *(nint*)(transactionCallback + 0x10) : 0;
            var transactionMatches = transaction != 0 && *(nint*)transaction == requestHandler + 0x1c0;
            var rowMatches = transactionMatches && *(int*)(requestHandler + 0x3d54) == found && *(int*)(requestHandler + 0x3d58) == 1;
            var contentsMatch = transactionMatches && pending != null && *(uint*)(transaction + 0x68) == 1 &&
                TransactionEntryMatches(transaction, 0x38, pending.ItemId, 1) &&
                TransactionEntryMatches(transaction, 0x50, 19u + pending.Company, pending.UnitCost);
            var dialogMatches = callback && *(uint*)(transactionCallback + 0x18) == yesno->Id;
            var observedPrompt = yesno->PromptText == null ? "" : SeString.Parse(yesno->PromptText->NodeText.AsSpan()).TextValue;
            var expectedPrompt = data.GetExcelSheet<Addon>().GetRow(2436).Text.ExtractText();
            var prompt = ShopPrompt.Matches(observedPrompt, expectedPrompt);
            var mode = *((byte*)yesno + 0x2ce) == 0;
            var condition = transactionMatches ? TransactionCondition(transaction) : -1;
            var exchange = condition == 1005;
            var kind = exchange ? PurchaseDialogKind.Exchange : PurchaseDialogKind.Equipment;
            var recognized = exchange ? yesno->YesButton != null && yesno->YesButton->IsEnabled : prompt && yesno->ConfirmCheckBox != null;
            var correlated = pending != null && requestItem == pending.ItemId && requestRow == found && found >= 0 &&
                ownerMatches && callback && rowMatches && contentsMatch && dialogMatches && recognized && mode && yesno->IsReady;
            if (pending != null)
                LastDialogCheck = $"{kind}: Condition={condition}; Owner={ownerMatches}; Callback={callback}; Transaction={transactionMatches}; RowQuantity={rowMatches}; ItemCurrency={contentsMatch}; DialogId={dialogMatches}; Recognized={recognized}; Prompt={prompt}; PromptLength={observedPrompt.Length}; ExpectedLength={expectedPrompt.Length}; NormalizedLength={ShopPrompt.Normalize(observedPrompt).Length}; ExpectedNormalizedLength={ShopPrompt.Normalize(expectedPrompt).Length}; StandardResult={mode}; Ready={yesno->IsReady}; CheckedAtUtc={DateTimeOffset.UtcNow:O}";
            dialog = new(yesno->Id, correlated, correlated ? pending!.ItemId : 0, correlated ? 1u : 0u,
                correlated ? pending!.UnitCost : 0, kind);
        }
        return new(true, "", identity, tier, category, found, liveId, liveCost, seals, count,
            pending == null || HasBagCapacity(inventory, pending), dialog);
    }

    /// <summary>Revalidates native context and sends one purchase or navigation action.</summary>
    public ShopDispatchResult Dispatch(ShopCommand command)
    {
        var entered = false;
        try
        {
            if (!canMutate()) return new(false, false, "Native shopping is unavailable for this game build.");
            var observation = Read(command.Offer);
            if (!observation.Ready || observation.Identity != command.Identity || observation.ItemCount < 0)
                return new(false, false, "Shop context changed before the action.");
            var addon = (AtkUnitBase*)gui.GetAddonByName("GrandCompanyExchange").Address;
            if (addon == null) return new(false, false, "The shop closed.");
            AtkValue* values = stackalloc AtkValue[9];
            new Span<AtkValue>(values, 9).Clear();
            if (command.Kind is ShopCommandKind.SelectTier or ShopCommandKind.SelectCategory)
            {
                if (observation.Dialog != null) return new(false, false, "Close the existing confirmation first.");
                Int(values, command.Kind == ShopCommandKind.SelectTier ? 1 : 2);
                Int(values + 1, command.Kind == ShopCommandKind.SelectTier ? command.Offer.Tier : command.Offer.NativeCategory + 1);
                entered = true;
                addon->FireCallback(2, values);
                return new(true, true);
            }
            if (observation.NativeRow != command.NativeRow || observation.LiveItemId != command.Offer.ItemId ||
                observation.LiveCost != command.Offer.UnitCost || observation.Seals < command.Offer.UnitCost || !observation.HasCapacity ||
                observation.Seals != command.ExpectedSeals || observation.ItemCount != command.ExpectedCount)
                return new(false, false, "The offer or resources changed before purchase.");
            if (command.Kind is ShopCommandKind.ConfirmEquipment or ShopCommandKind.ConfirmExchange)
            {
                var expectedKind = command.Kind == ShopCommandKind.ConfirmEquipment ? PurchaseDialogKind.Equipment : PurchaseDialogKind.Exchange;
                if (requestGeneration != command.Generation || (expectedKind == PurchaseDialogKind.Equipment ? equipmentConfirmed : exchangeConfirmed) ||
                    observation.Dialog is not { Correlated: true } warning || warning.Kind != expectedKind || warning.Identity != command.DialogIdentity)
                    return new(false, false, "The transaction confirmation does not belong to this purchase.");
                var warningAddon = (AtkUnitBase*)gui.GetAddonByName("SelectYesno").Address;
                if (warningAddon == null || !warningAddon->IsReady || !warningAddon->IsVisible)
                    return new(false, false, "The equipment confirmation closed.");
                // Native checked Proceed/OK emits Int0 in this verified standard-result mode.
                Int(values, 0);
                if (expectedKind == PurchaseDialogKind.Equipment) equipmentConfirmed = true;
                else exchangeConfirmed = true;
                entered = true;
                warningAddon->FireCallback(1, values, true);
                return new(true, true, ExpectsConfirmation: false);
            }
            if (command.Kind == ShopCommandKind.ConfirmOnce)
            {
                if (requestGeneration != command.Generation || observation.Dialog is not { Correlated: true } dialog || dialog.Identity != command.DialogIdentity)
                    return new(false, false, "The purchase confirmation does not belong to this operation.");
                var currency = (AtkUnitBase*)gui.GetAddonByName("ShopExchangeCurrencyDialog").Address;
                if (currency == null) return new(false, false, "The purchase confirmation closed.");
                var confirmButton = *(AtkComponentButton**)((byte*)currency + 0x270);
                if (confirmButton == null || !confirmButton->IsEnabled || (confirmedGeneration == command.Generation && confirmedRow == command.NativeRow))
                    return new(false, false, "The confirmation is disabled or was already sent.");
                confirmedGeneration = command.Generation;
                confirmedRow = command.NativeRow;
                entered = true;
                FireCurrencyConfirmation(currency);
                return new(true, true);
            }
            if (observation.Dialog != null || observation.ItemCount != 0) return new(false, false, "The item or dialog state changed before purchase.");
            var row = *(byte**)((byte*)addon + 0x3f0) + command.NativeRow * 0x30;
            var list = *(AtkComponentList**)((byte*)addon + 0x238);
            if (list == null || list->GetItemDisabledState(command.NativeRow) || row[0x2c] != 0)
                return new(false, false, "The native shop has disabled this offer.");
            // Match the game's nine-value selection event, pinning quantity to one.
            Int(values, 0); Int(values + 1, command.NativeRow); Int(values + 2, 1);
            Bool(values + 4, true); Bool(values + 5, row[0x2f] != 0);
            UInt(values + 6, command.Offer.ItemId); UInt(values + 7, *(uint*)(row + 0x18)); UInt(values + 8, command.Offer.UnitCost);
            requestGeneration = command.Generation;
            requestItem = command.Offer.ItemId;
            requestRow = command.NativeRow;
            var requestAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanyExchange);
            requestReceiver = requestAgent == null ? 0 : *(nint*)((byte*)requestAgent + 0x60);
            requestHandler = requestReceiver == 0 ? 0 : *(nint*)(requestReceiver + 0x10);
            equipmentConfirmed = false;
            exchangeConfirmed = false;
            // Row numbers can recur after the fixed queue navigates to another native category.
            confirmedRow = -1;
            var expectsConfirmation = row[0x2f] != 0;
            entered = true;
            addon->FireCallback(9, values);
            return new(true, true, ExpectsConfirmation: expectsConfirmation);
        }
        catch (Exception) { return new(false, entered, "The native shop action failed."); }
    }

    private static nint TransactionCallbackAddress()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var imageBase = process.MainModule?.BaseAddress ?? 0;
        return imageBase == 0 ? 0 : imageBase + 0x28c3658;
    }

    private static void FireCurrencyConfirmation(AtkUnitBase* currency)
    {
        AtkValue* values = stackalloc AtkValue[2];
        new Span<AtkValue>(values, 2).Clear();
        Int(values, 0);
        Int(values + 1, 1);
        // The native Confirm event calls the close=true wrapper (2026.10.06:
        // 0x1412C741E -> 0x14066FEF0), allowing this owned modal to finish.
        currency->FireCallback(2, values, true);
    }

    private static bool TransactionEntryMatches(nint transaction, int offset, uint item, uint amount)
    {
        var begin = *(nint*)(transaction + offset);
        var end = *(nint*)(transaction + offset + 8);
        return begin != 0 && end - begin == 16 && *(uint*)begin == item && *(uint*)(begin + 4) == amount;
    }

    private static int TransactionCondition(nint transaction)
    {
        var slots = *(nint**)(transaction + 0x10);
        var capacity = *(ulong*)(transaction + 0x18);
        var index = *(ulong*)(transaction + 0x20);
        if (slots == null || !NativeShopPolicy.TryConditionIndex(capacity, index, out var slotIndex)) return -1;
        var condition = slots[slotIndex];
        return condition == 0 ? -1 : *(ushort*)(condition + 8);
    }

    private static int Count(InventoryManager* manager, uint item)
    {
        long total = 0;
        foreach (var type in CountContainers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded || !NativeShopPolicy.ContainerSizeValid(container->Size)) return -1;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) return -1;
                if (slot->ItemId == item) total += slot->Quantity;
            }
        }
        return NativeShopPolicy.BoundedCount(total);
    }

    private static bool HasBagCapacity(InventoryManager* manager, ShopOffer offer)
    {
        if (offer.IsUnique && Count(manager, offer.ItemId) > 0) return false;
        foreach (var type in CountContainers.Take(4))
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded || !NativeShopPolicy.ContainerSizeValid(container->Size)) return false;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) return false;
                if (NativeShopPolicy.SlotAccepts(slot->ItemId, slot->Quantity, offer.ItemId, offer.StackSize)) return true;
            }
        }
        return false;
    }

    private static bool Number(AtkUnitBase* addon, int index, out uint number)
    {
        number = 0;
        if (addon->AtkValues == null || index < 0 || index >= addon->AtkValuesCount) return false;
        var value = addon->AtkValues[index];
        if (value.Type == AtkValueType.UInt) { number = value.UInt; return true; }
        if (value.Type == AtkValueType.Int && value.Int >= 0) { number = (uint)value.Int; return true; }
        return false;
    }
    private static void Int(AtkValue* value, int n) { value->Type = AtkValueType.Int; value->Int = n; }
    private static void UInt(AtkValue* value, uint n) { value->Type = AtkValueType.UInt; value->UInt = n; }
    private static void Bool(AtkValue* value, bool b) { value->Type = AtkValueType.Bool; value->Byte = b ? (byte)1 : (byte)0; }
    private static PurchaseObservation Unavailable(ShopIdentity identity, string reason, uint seals = 0, int count = -1)
        => new(false, reason, identity, -1, -1, -1, 0, 0, seals, count, false);
}
