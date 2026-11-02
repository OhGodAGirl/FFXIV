using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpringCleaning;

internal sealed class NativeUi
{
    internal SupportDiagnostics Diagnostics { get; } = new();

    public const int InventoryDresserCategory = 0;
    public const int ArmouryChestDresserCategory = 1;
    public const int ArmouryChestDresserCategoryCount = 5;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly string existingOutfitPrompt;

    public NativeUi(IGameGui gameGui, IPluginLog log, string existingOutfitPrompt)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.existingOutfitPrompt = existingOutfitPrompt;
    }

    public bool IsDresserOpen => IsAddonVisible("MiragePrismPrismBox");

    public bool ShouldShowDresserCompanions => DresserStationVisibilityPolicy.ShouldShowCompanions(
        IsDresserOpen,
        IsAddonVisible("MiragePrismMiragePlate"));

    public bool IsGlamourCreationOpen => IsAddonVisible("MiragePrismPrismBoxCrystallize");

    public bool IsOutfitCreationOpen => IsAddonVisible("MiragePrismPrismSetConvert");

    public bool IsOutfitConfirmationOpen => IsAddonVisible("MiragePrismPrismSetConvertC");

    public unsafe bool IsDresserItemConfirmationOpen
    {
        get
        {
            var addon = GetDresserItemConfirmationAddon();
            return addon != null && addon->IsVisible;
        }
    }

    public bool IsInventoryContextOpen => IsAddonVisible("ContextMenu");

    public bool IsExistingOutfitMergePromptOpen => IsAddonVisible("SelectYesno");

    public bool IsArmoireStoreOpen => IsAddonVisible("Cabinet");

    public bool IsSelectStringOpen => IsAddonVisible("SelectString");

    public nint GetDresserAddon() => gameGui.GetAddonByName("MiragePrismPrismBox").Address;

    public nint GetArmoireAddon() => gameGui.GetAddonByName("Cabinet").Address;

    public nint GetGlamourCreationAddon() => gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize").Address;

    public unsafe bool EnsureGearsetItemsVisible(out bool changed) =>
        DresserNativeControls.EnsureGearsetItemsVisible(gameGui, out changed);

    public unsafe bool TryGetGearsetItemsHidden(out bool hidden) =>
        DresserNativeControls.TryGetGearsetItemsHidden(gameGui, out hidden);

    public unsafe bool SetGearsetItemsHidden(bool hidden) =>
        DresserNativeControls.SetGearsetItemsHidden(gameGui, hidden);

    public unsafe bool TryGetDresserCategoryIndex(out int categoryIndex) =>
        DresserNativeControls.TryGetDresserCategoryIndex(gameGui, out categoryIndex);

    public unsafe bool TryGetArmouryChestCategoryIndexes(out IReadOnlyList<int> categoryIndexes) =>
        DresserNativeControls.TryGetArmouryChestCategoryIndexes(gameGui, out categoryIndexes);

    public unsafe bool EnsureDresserCategory(int desiredCategoryIndex, out bool changed) =>
        DresserNativeControls.EnsureDresserCategory(gameGui, desiredCategoryIndex, out changed);

    public unsafe bool AlignGlamourCreation(float x, float y) =>
        DresserNativeControls.AlignGlamourCreation(gameGui, x, y);

    public unsafe void CloseDresser()
    {
        var agent = AgentMiragePrismPrismBox.Instance();
        if (agent != null && agent->IsAgentActive())
            agent->Hide();
    }

    public unsafe void CloseOutfitCreation()
    {
        var agent = AgentMiragePrismPrismSetConvert.Instance();
        if (agent != null && agent->IsAgentActive())
            agent->Hide();
    }

    public unsafe void CloseArmoire()
    {
        var storeAgent = AgentCabinetWithdraw.Instance();
        if (storeAgent != null && storeAgent->IsAgentActive())
            storeAgent->Hide();

        var cabinetAgent = AgentCabinet.Instance();
        if (cabinetAgent != null && cabinetAgent->IsAgentActive())
            cabinetAgent->Hide();
    }

    public unsafe bool SelectFirstMenuEntry()
    {
        var address = gameGui.GetAddonByName("SelectString");
        if (address.IsNull)
            return false;

        var addon = (AddonSelectString*)address.Address;
        return addon->AtkUnitBase.FireCallbackInt(0);
    }

    public unsafe bool TrySelectOutfitPieces(
        InventoryDresserOutfit outfit,
        out string status)
    {
        var address = gameGui.GetAddonByName("MiragePrismPrismSetConvert");
        if (address.IsNull || !address.IsVisible)
        {
            status = "Waiting for Outfit Glamour Creation to load.";
            return false;
        }

        var agent = AgentMiragePrismPrismSetConvert.Instance();
        if (agent == null
            || agent->Data == null
            || !agent->IsAgentActive()
            || agent->Data->State != SetConvertAddonState.Ready)
        {
            status = "Waiting for Outfit Glamour Creation to become ready.";
            return false;
        }

        if (agent->Data->SelectedSetIndex >= agent->Data->ItemSetCount
            || agent->Data->ItemSets[(int)agent->Data->SelectedSetIndex].ItemId != outfit.SetItemId)
        {
            var targetSetIndex = -1;
            var addon = (AddonMiragePrismPrismSetConvert*)address.Address;
            var visibleChoiceCount = OutfitSelectionBoundsPolicy.GetVisibleOutfitChoiceCount(
                (int)agent->Data->ItemSetCount,
                addon->TabButtons.Length);
            for (var index = 0; index < visibleChoiceCount; index++)
            {
                if (agent->Data->ItemSets[index].ItemId == outfit.SetItemId)
                {
                    targetSetIndex = index;
                    break;
                }
            }

            if (targetSetIndex < 0)
            {
                status = $"The game did not offer {outfit.Name} for the selected piece.";
                return false;
            }

            var tabButtons = addon->TabButtons;
            var targetButton = targetSetIndex < tabButtons.Length
                ? tabButtons[targetSetIndex].Value
                : null;
            if (targetButton == null
                || !targetButton->IsEnabled
                || !targetButton->AtkResNode->IsVisible()
                || !DispatchButtonClick(targetButton))
            {
                status = $"Waiting for the {outfit.Name} outfit choice to become available.";
                return false;
            }

            status = outfit.ExistingPrismBoxIndex.HasValue
                ? $"Selecting the existing {outfit.Name} outfit glamour."
                : $"Selecting {outfit.Name}.";
            return false;
        }

        if (outfit.ExistingPrismBoxIndex is int existingIndex)
        {
            // Selecting the native outfit tab normally establishes both of
            // these values. Keep them explicit so a partial outfit is merged
            // into its existing Dresser entry rather than treated as a new set.
            agent->Data->PrismBoxIndex = (uint)existingIndex;
            agent->Data->StoreInExistingOutfit = true;
        }
        else
            agent->Data->StoreInExistingOutfit = false;

        var selectedCount = 0;
        var changed = false;
        var selectablePieceCount = OutfitSelectionBoundsPolicy.GetSelectablePieceCount(
            (int)agent->Data->NumItemsInSet,
            agent->Data->Items.Length);
        for (var index = 0; index < selectablePieceCount; index++)
        {
            ref var nativeItem = ref agent->Data->Items[index];
            var itemId = nativeItem.ItemId;
            if (nativeItem.InventoryType != InventoryType.Invalid
                && !outfit.Components.Any(component => component.ItemId == itemId))
            {
                nativeItem.InventoryType = InventoryType.Invalid;
                nativeItem.Slot = 0;
                changed = true;
            }
        }
        foreach (var component in outfit.Components)
        {
            var nativeIndex = -1;
            for (var index = 0; index < selectablePieceCount; index++)
            {
                if (agent->Data->Items[index].ItemId == component.ItemId)
                {
                    nativeIndex = index;
                    break;
                }
            }

            if (nativeIndex < 0)
            {
                status = $"{component.Name} is not present in the selected native outfit.";
                return false;
            }

            ref var nativeItem = ref agent->Data->Items[nativeIndex];
            if (nativeItem.InventoryType == (InventoryType)component.InventoryTypeId
                && nativeItem.Slot == (uint)component.SlotIndex)
            {
                selectedCount++;
                continue;
            }

            // Change the agent's documented hand-in selection and ask the game
            // to validate it. A synthetic ReceiveEvent does not contain the
            // event data this addon expects and can access-violate the client.
            nativeItem.InventoryType = (InventoryType)component.InventoryTypeId;
            nativeItem.Slot = (uint)component.SlotIndex;
            changed = true;
        }

        if (changed)
        {
            agent->ValidateItems();
            status = $"Selecting outfit pieces… {selectedCount} / {outfit.Components.Count}";
            return false;
        }

        status = $"Selected all {selectedCount} pieces of {outfit.Name}.";
        return IsOutfitSelectionExact(outfit);
    }

    public unsafe bool IsOutfitSelectionExact(InventoryDresserOutfit outfit)
    {
        if (!Diagnostics.Compare("OutfitCreationMatches", 1, IsOutfitCreationFor(outfit) ? 1 : 0))
            return false;
        var data = AgentMiragePrismPrismSetConvert.Instance()->Data;
        var modeMatches = Diagnostics.Compare("ExistingOutfitMode", outfit.ExistingPrismBoxIndex.HasValue ? 1 : 0,
            data->StoreInExistingOutfit ? 1 : 0);
        var destinationMatches = !outfit.ExistingPrismBoxIndex.HasValue || Diagnostics.Compare("DestinationRow",
            outfit.ExistingPrismBoxIndex.Value, data->PrismBoxIndex);
        if (!modeMatches || !destinationMatches) return false;
        var selected = new List<OutfitPieceSelection>();
        var count = OutfitSelectionBoundsPolicy.GetSelectablePieceCount((int)data->NumItemsInSet, data->Items.Length);
        for (var index = 0; index < count; index++)
        {
            var piece = data->Items[index];
            if (piece.InventoryType != InventoryType.Invalid)
                selected.Add(new(piece.ItemId, (int)piece.InventoryType, (int)piece.Slot));
        }
        return Diagnostics.Compare("SelectedPiecesMatch", 1, OutfitPieceSelectionPolicy.Matches(outfit, selected) ? 1 : 0);
    }

    public unsafe bool IsOutfitCreationFor(InventoryDresserOutfit outfit)
    {
        var address = gameGui.GetAddonByName("MiragePrismPrismSetConvert");
        var agent = AgentMiragePrismPrismSetConvert.Instance();
        return !address.IsNull
            && address.IsVisible
            && agent != null
            && agent->Data != null
            && agent->IsAgentActive()
            && agent->Data->SelectedSetIndex < agent->Data->ItemSetCount
            && agent->Data->ItemSets[(int)agent->Data->SelectedSetIndex].ItemId == outfit.SetItemId;
    }

    public unsafe bool SubmitOutfitCreation(out string status)
    {
        if (IsOutfitConfirmationOpen)
        {
            status = "Outfit storage confirmation is ready.";
            return true;
        }

        var address = gameGui.GetAddonByName("MiragePrismPrismSetConvert");
        if (address.IsNull || !address.IsVisible)
        {
            status = "Waiting for Outfit Glamour Creation to become ready.";
            return false;
        }

        var addon = (AddonMiragePrismPrismSetConvert*)address.Address;
        var button = addon->StoreAsGlamourButton;
        Diagnostics.Compare("StoreButtonPresent", 1, button != null ? 1 : 0);
        Diagnostics.Compare("StoreButtonEnabled", 1, button != null ? (button->IsEnabled ? 1 : 0) : null);
        Diagnostics.Compare("StoreButtonVisible", 1, button != null ? (button->AtkResNode->IsVisible() ? 1 : 0) : null);
        if (button == null || !button->IsEnabled || !button->AtkResNode->IsVisible())
        {
            status = "Waiting for Store as Glamour to become available.";
            return false;
        }

        // Dispatch the button's registered native event so the addon runs the
        // same path as a real click. FireCallbackInt's return value does not
        // report whether the game accepted the action; the confirmation can
        // open even when it returns false.
        if (!DispatchButtonClick(button))
            addon->AtkUnitBase.FireCallbackInt(14);

        status = "Opening the outfit storage confirmation.";
        return true;
    }

    public unsafe bool ConfirmOutfitStorage(bool storeInExistingOutfit, out string status)
    {
        var address = gameGui.GetAddonByName("MiragePrismPrismSetConvertC");
        if (address.IsNull || !address.IsVisible)
        {
            status = "Waiting for the outfit storage confirmation.";
            return false;
        }

        var addon = (AddonMiragePrismPrismSetConvertC*)address.Address;
        var checkbox = addon->StoreAsOutfitGlamourCheckBox;
        if (checkbox == null)
        {
            status = "The Store as Outfit Glamour option is unavailable.";
            return false;
        }

        if (!checkbox->IsChecked)
        {
            if (!checkbox->AtkComponentButton.IsEnabled && !storeInExistingOutfit)
            {
                status = "Waiting for Store as Outfit Glamour to become selectable.";
                return false;
            }

            if (checkbox->AtkComponentButton.IsEnabled)
            {
                // A real mouse click first toggles the component, then emits
                // its registered ButtonClick event to the addon. DispatchEvent
                // starts at the second half of that path, so mirror the
                // component toggle before notifying the addon.
                checkbox->SetChecked(true);
                if (!DispatchButtonClick(&checkbox->AtkComponentButton))
                {
                    status = "Waiting for Store as Outfit Glamour to become selectable.";
                    return false;
                }
            }
        }

        // When pieces are appended to an outfit already in the Dresser, the
        // game can lock this checkbox off: the agent's existing-outfit state,
        // rather than the checkbox, determines where the pieces are merged.
        if (!checkbox->IsChecked && !storeInExistingOutfit)
        {
            status = "Waiting for Store as Outfit Glamour to be selected.";
            return false;
        }

        var yesButton = addon->YesButton;
        if (yesButton == null)
        {
            status = "Waiting for the outfit storage confirmation to become ready.";
            return false;
        }

        if (!yesButton->IsEnabled || !yesButton->AtkResNode->IsVisible())
        {
            status = "Waiting for the outfit storage confirmation to become ready.";
            return false;
        }

        // Use the registered Yes-button event as well. This lets the addon
        // include its checkbox state when it submits the storage request.
        if (!DispatchButtonClick(yesButton))
            addon->AtkUnitBase.FireCallbackInt(0);

        status = "Storing the outfit in the Glamour Dresser.";
        return true;
    }

    public unsafe bool ConfirmExistingOutfitMerge(out string status)
    {
        var address = gameGui.GetAddonByName("SelectYesno");
        if (address.IsNull || !address.IsVisible)
        {
            status = "Waiting for the existing-outfit confirmation.";
            return false;
        }

        var addon = (AddonSelectYesno*)address.Address;
        // Addon row 15663 is a static localized prompt, not an item-name template.
        // Never operate a different SelectYesno merely because an outfit is pending.
        if (addon->PromptText == null || !ExistingOutfitConfirmationPolicy.Matches(
                new Lumina.Text.ReadOnly.ReadOnlySeStringSpan(addon->PromptText->NodeText.AsSpan()).ExtractText(), existingOutfitPrompt))
        {
            status = "Waiting for the matching outfit confirmation, kupo!";
            return false;
        }
        var yesButton = addon->YesButton;
        if (yesButton == null || !yesButton->IsEnabled || !yesButton->AtkResNode->IsVisible())
        {
            status = "Waiting for Add to pre-existing outfit glamour to become ready.";
            return false;
        }

        // This standard Yes/No addon owns its callback directly. Its callback
        // return value does not indicate whether the game accepted the click,
        // so issue the native Yes callback and let inventory state confirm it.
        addon->AtkUnitBase.FireCallbackInt(0);

        status = "Adding the selected pieces to the existing outfit glamour.";
        return true;
    }

    internal static unsafe bool DispatchButtonClick(AtkComponentButton* button)
    {
        if (button == null)
            return false;

        var ownerNode = button->AtkComponentBase.OwnerNode == null
            ? null
            : &button->AtkComponentBase.OwnerNode->AtkResNode;
        var componentNode = button->AtkComponentBase.AtkResNode;
        var node = ownerNode != null && ownerNode->IsEventRegistered(AtkEventType.ButtonClick)
            ? ownerNode
            : componentNode != null && componentNode->IsEventRegistered(AtkEventType.ButtonClick)
                ? componentNode
                : null;
        if (node == null)
            return false;

        AtkEventDispatcher.Event clickEvent = default;
        clickEvent.State.EventType = AtkEventType.ButtonClick;
        node->DispatchEvent(&clickEvent);
        return true;
    }

    public static unsafe bool Interact(nint objectAddress)
    {
        if (objectAddress == 0)
            return false;

        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
            return false;

        targetSystem->InteractWithObject((GameObject*)objectAddress, true);
        return true;
    }

    public unsafe bool OpenInventoryContext(InventoryDuplicateItem candidate)
                => OpenInventoryContext(candidate.InventoryTypeId, candidate.SlotIndex, candidate.ItemId);

    public unsafe bool RequestDiscard(InventoryDuplicateItem candidate)
    {
        if (IsExistingOutfitMergePromptOpen)
            return false;

        var inventory = InventoryManager.Instance();
        var agent = AgentInventoryContext.Instance();
        if (inventory == null || agent == null)
            return false;

        var inventoryType = (InventoryType)candidate.InventoryTypeId;
        var container = inventory->GetInventoryContainer(inventoryType);
        if (container == null
            || !container->IsLoaded
            || candidate.SlotIndex < 0
            || candidate.SlotIndex >= container->Size)
        {
            return false;
        }

        var item = container->GetInventorySlot(candidate.SlotIndex);
        if (item == null || item->IsEmpty() || item->GetBaseItemId() != candidate.ItemId)
            return false;

        uint ownerAddonId = 0;
        var dresserAddress = GetDresserAddon();
        if (dresserAddress != 0)
            ownerAddonId = ((AtkUnitBase*)dresserAddress)->Id;

        agent->DiscardItem(item, inventoryType, candidate.SlotIndex, ownerAddonId);
        return true;
    }

    public unsafe bool? IsInventoryItemPresent(InventoryDuplicateItem candidate)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return null;

        var container = inventory->GetInventoryContainer((InventoryType)candidate.InventoryTypeId);
        if (container == null
            || !container->IsLoaded
            || candidate.SlotIndex < 0
            || candidate.SlotIndex >= container->Size)
        {
            return null;
        }

        var item = container->GetInventorySlot(candidate.SlotIndex);
        return item == null ? null : !item->IsEmpty() && item->GetBaseItemId() == candidate.ItemId;
    }

    public unsafe bool CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued)
    {
        if (!Diagnostics.Compare("DiscardRequestOwned", 1, DiscardConfirmationPolicy.CanControlOwnedPrompt(requestIssued) ? 1 : 0))
            return false;

        var address = gameGui.GetAddonByName("SelectYesno");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonSelectYesno*)address.Address;
        if (addon->PromptText == null || !DiscardConfirmationPolicy.MatchesExpectedItem(
                addon->PromptText->NodeText.ToString().ReplaceLineEndings(string.Empty), candidate.Name))
            return false;
        var noButton = addon->NoButton;
        if (noButton == null || !noButton->IsEnabled || !noButton->AtkResNode->IsVisible())
            return false;

        if (!DispatchButtonClick(noButton))
            addon->AtkUnitBase.FireCallbackInt(1);
        return true;
    }

    public unsafe bool ConfirmDiscardConfirmation(
        InventoryDuplicateItem candidate,
        bool requestIssued)
    {
        if (!Diagnostics.Compare("DiscardRequestOwned", 1, DiscardConfirmationPolicy.CanControlOwnedPrompt(requestIssued) ? 1 : 0))
            return false;

        var address = gameGui.GetAddonByName("SelectYesno");
        if (address.IsNull || !address.IsVisible)
            return false;

        var addon = (AddonSelectYesno*)address.Address;
        if (addon->PromptText == null || addon->YesButton == null
            || !addon->YesButton->IsEnabled
            || !addon->YesButton->AtkResNode->IsVisible())
        {
            return false;
        }

        var prompt = addon->PromptText->NodeText
            .ToString()
            .ReplaceLineEndings(string.Empty);
        if (!Diagnostics.Compare("DiscardPromptItemNameMatches", 1, DiscardConfirmationPolicy.MatchesExpectedItem(prompt, candidate.Name) ? 1 : 0))
            return false;

        // This controller owns the prompt and has matched its item name. The
        // callback return value does not report whether the game accepted the
        // choice, so inventory removal remains the authoritative outcome.
        addon->AtkUnitBase.FireCallbackInt(0);
        return true;
    }

    public unsafe bool OpenDresserStorage(InventoryDresserItem candidate)
    {
        var agent = AgentMiragePrismPrismBox.Instance();
        if (agent == null || agent->Data == null || !agent->IsAgentActive())
            return false;

        var count = Math.Min((int)agent->Data->CrystallizeItemCount, 140);
        var candidateIndex = -1;
        for (var index = 0; index < count; index++)
        {
            var listed = agent->Data->CrystallizeItems[index];
            if (listed.Inventory == (InventoryType)candidate.InventoryTypeId
                && listed.Slot == candidate.SlotIndex
                && listed.ItemId == candidate.ItemId)
            {
                candidateIndex = index;
                break;
            }
        }

        if (candidateIndex < 0)
            return false;

        // This is the same callback emitted when the player selects a normal
        // (non-outfit) candidate in Glamour Creation. Use it for both bag and
        // Armoury Chest containers so automation does not depend on localized
        // context-menu text or leave a menu waiting for manual input.
        AtkValue returnValue = default;
        Span<AtkValue> values = stackalloc AtkValue[3];
        values.Clear();
        values[0].SetUInt(14);
        values[1].SetUInt((uint)candidateIndex);
        values[2].SetUInt(0);
        fixed (AtkValue* valuesPointer = values)
            agent->ReceiveEvent(&returnValue, valuesPointer, 3, 0);
        return true;
    }

    public unsafe void CloseInventoryContext()
    {
        var agent = AgentInventoryContext.Instance();
        if (agent != null && agent->IsAgentActive())
            agent->Hide();
    }

    public unsafe bool ConfirmDresserItemStorage()
    {
        var addon = GetDresserItemConfirmationAddon();
        if (addon == null || !addon->IsVisible)
            return false;

        // The Armoury-to-Dresser prompt is an agent-owned glamour dialog, not
        // MaterializeDialog despite sharing its visual layout. Callback 0 is
        // this confirmation's native affirmative response. Its return value
        // does not report whether the game accepted the request, so inventory
        // and Dresser state remain the controller's source of truth.
        log.Debug(
            "Submitting Dresser confirmation callback 0 to addon {AddonName} "
            + "(id {AddonId}, {AtkValueCount} values).",
            addon->NameString,
            addon->Id,
            addon->AtkValuesCount);
        addon->FireCallbackInt(0);
        return true;
    }

    public unsafe bool OpenOutfitCreation(InventoryDresserOutfit outfit)
    {
        if (outfit.Components.Count == 0)
            return false;

        var source = outfit.Components[0];

        var inventory = InventoryManager.Instance();
        var agent = AgentMiragePrismPrismSetConvert.Instance();
        if (inventory == null || agent == null)
            return false;

        // The agent can remain active briefly after its addon has disappeared.
        // Open rejects a new outfit in that state, so close the stale agent and
        // let the controller retry after the native UI has settled.
        if (agent->IsAgentActive() && !IsOutfitCreationOpen)
        {
            agent->Hide();
            return false;
        }

        var inventoryType = (InventoryType)source.InventoryTypeId;
        var container = inventory->GetInventoryContainer(inventoryType);
        if (container == null
            || !container->IsLoaded
            || source.SlotIndex < 0
            || source.SlotIndex >= container->Size)
        {
            return false;
        }

        var item = container->GetInventorySlot(source.SlotIndex);
        if (item == null || item->IsEmpty() || item->GetBaseItemId() != source.ItemId
            || item->IsHighQuality() != source.IsHighQuality)
            return false;

        ushort crystallizeAddonId = 0;
        var crystallizeAddress = gameGui.GetAddonByName("MiragePrismPrismBoxCrystallize").Address;
        if (crystallizeAddress != 0)
            crystallizeAddonId = ((AtkUnitBase*)crystallizeAddress)->Id;

        ushort prismBoxAddonId = 0;
        var dresserAddress = GetDresserAddon();
        if (dresserAddress != 0)
            prismBoxAddonId = ((AtkUnitBase*)dresserAddress)->Id;

        return agent->Open(
            source.ItemId,
            inventoryType,
            source.SlotIndex,
            crystallizeAddonId,
            prismBoxAddonId,
            true);
    }

    private unsafe bool OpenInventoryContext(
        int inventoryTypeId,
        int slotIndex,
        uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return false;

        var inventoryType = (InventoryType)inventoryTypeId;
        var container = inventory->GetInventoryContainer(inventoryType);
        if (container == null
            || !container->IsLoaded
            || slotIndex < 0
            || slotIndex >= container->Size)
        {
            return false;
        }

        var item = container->GetInventorySlot(slotIndex);
        if (item == null || item->IsEmpty() || item->GetBaseItemId() != itemId)
            return false;

        var agent = AgentInventoryContext.Instance();
        if (agent == null)
            return false;

        uint ownerAddonId = 0;
        var ownerAddress = GetDresserAddon();
        if (ownerAddress != 0)
            ownerAddonId = ((AtkUnitBase*)ownerAddress)->Id;
        agent->OpenForItemSlot(inventoryType, slotIndex, 0, ownerAddonId);
        return true;
    }

    private static unsafe AtkUnitBase* GetDresserItemConfirmationAddon()
    {
        var agent = AgentMiragePrismPrismBox.Instance();
        var unitManager = RaptureAtkUnitManager.Instance();
        if (agent == null
            || unitManager == null
            || agent->ContextAddonId is 0 or > ushort.MaxValue)
        {
            return null;
        }

        return unitManager->AtkUnitManager.GetAddonById((ushort)agent->ContextAddonId);
    }

    private bool IsAddonVisible(string name)
    {
        var addon = gameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsVisible;
    }

}
