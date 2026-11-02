using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace EZDelivery.Features.ExpertDelivery;

/// <summary>October 6, 2026 reviewed ABI. All entry points require the framework thread.</summary>
public sealed unsafe class DeliveryNativeAdapter : IDeliveryNativeAdapter, IDisposable
{
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly IAddonLifecycle lifecycle;
    private readonly bool reviewedBuild;
    private readonly nint rewardReceiverVtable;
    private bool disposed;
    private long mapGeneration;
    private ulong mapHash;
    private ulong selectionBuffs;
    private long selectionAgent;
    /// <summary>Most recent delivery dialog correlation diagnostic.</summary>
    public string LastDialogCheck { get; private set; } = "No pending delivery warning observed";
    /// <summary>UTC time of the most recent dialog correlation check.</summary>
    public DateTimeOffset? LastDialogCheckAtUtc { get; private set; }
    /// <summary>Most recent native row-to-inventory mapping diagnostic.</summary>
    public string LastMappingCheck { get; private set; } = "No mapping failure observed";
    /// <summary>Most recent delivery-list refresh diagnostic.</summary>
    public string RefreshCheck { get; private set; } = "Not observed";
    /// <summary>UTC time of the latest observed native delivery-list refresh.</summary>
    public DateTimeOffset? LastNativeRefreshAtUtc { get; private set; }
    private void RecordDialogCheck(string value)
    {
        if (LastDialogCheck == value) return;
        LastDialogCheck = value;
        LastDialogCheckAtUtc = DateTimeOffset.UtcNow;
    }
    private readonly DispatchLedger dispatchLedger = new();

    /// <summary>Binds host services and observes native list refreshes for this adapter lifetime.</summary>
    public DeliveryNativeAdapter(IGameGui gameGui, IClientState clientState, IDataManager dataManager, IAddonLifecycle lifecycle, bool reviewedBuild)
    {
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.lifecycle = lifecycle;
        this.reviewedBuild = reviewedBuild;
        if (reviewedBuild)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var imageBase = process.MainModule?.BaseAddress ?? 0;
            if (imageBase != 0) rewardReceiverVtable = imageBase + 0x247B9B8;
        }
        lifecycle.RegisterListener(AddonEvent.PostRefresh, "GrandCompanySupplyList", Invalidate);
        try { lifecycle.RegisterListener(AddonEvent.PreFinalize, "GrandCompanySupplyList", Invalidate); }
        catch { lifecycle.UnregisterListener(AddonEvent.PostRefresh, "GrandCompanySupplyList", Invalidate); throw; }
    }

    private void Invalidate(AddonEvent eventType, AddonArgs args)
    {
        if (eventType == AddonEvent.PostRefresh) LastNativeRefreshAtUtc = DateTimeOffset.UtcNow;
        mapGeneration++;
        mapHash = 0;
    }
    /// <summary>Detaches native list lifecycle observation.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { lifecycle.UnregisterListener(AddonEvent.PostRefresh, "GrandCompanySupplyList", Invalidate); }
        finally
        {
            lifecycle.UnregisterListener(AddonEvent.PreFinalize, "GrandCompanySupplyList", Invalidate);
        }
    }

    /// <summary>Reads current delivery facts and correlates them to the optional pending item.</summary>
    public DeliveryObservation Observe(DeliveryPending? pending)
    {
        try { return Read(pending); }
        catch { return DeliveryObservation.Unavailable(DeliveryReason.NotReady) with { State = DeliveryReadState.Failed }; }
    }

    private DeliveryObservation Read(DeliveryPending? pending)
    {
        RefreshCheck = "Delivery context unavailable";
        if (disposed || !reviewedBuild) return DeliveryObservation.Unavailable(DeliveryReason.UnsupportedBuild) with { State = DeliveryReadState.Unsupported };
        var player = PlayerState.Instance();
        var inventory = InventoryManager.Instance();
        if (!clientState.IsLoggedIn || player == null || player->ContentId == 0 || inventory == null)
            return DeliveryObservation.Unavailable(DeliveryReason.NoCharacter);
        var company = player->GrandCompany;
        if (company is < 1 or > 3) return DeliveryObservation.Unavailable(DeliveryReason.WrongContext);
        var addon = (AddonGrandCompanySupplyList*)gameGui.GetAddonByName("GrandCompanySupplyList", 1).Address;
        var agent = AgentGrandCompanySupply.Instance();
        var open = addon != null && addon->IsVisible && addon->IsReady;
        if (addon != null && agent != null)
        {
            var refreshList = addon->ExpertDeliveryList;
            RefreshCheck = $"Visible={addon->IsVisible}; Ready={addon->IsReady}; Tab={addon->SelectedTab}; AgentTab={agent->SelectedTab}; DeferredData={*((byte*)agent + 0x96) != 0}; RetryHandlePresent={*((uint*)((byte*)agent + 0x20)) != 0}; ListPresent={refreshList != null}; UpdatePending={refreshList != null && refreshList->IsUpdatePending}; ScrollPending={refreshList != null && refreshList->IsScrollRefreshPending}; Displayed={(refreshList == null ? -1 : refreshList->ListLength)}; PendingDelivery={pending != null}";
        }
        // The game hides this list while its reward modal is open. Keep only the
        // exact pending list instance/generation; visibility still gates new selections.
        var retained = DeliveryRules.CanObserveListContext(addon != null && addon->IsReady, open,
            (long)addon, pending?.Context.Addon ?? 0, pending is not null && pending.Generation == dispatchLedger.SelectionGeneration);
        var context = new DeliveryContext(player->ContentId, clientState.TerritoryType, company, player->GetGrandCompanyRank(), retained ? (long)addon : 0);
        var source = pending is null ? null : ReadSource(pending.Row.Source.Container, pending.Row.Source.Slot, out _);
        var sourceReadable = pending is not null && SourceReadable(pending.Row.Source.Container, pending.Row.Source.Slot);
        var observation = new DeliveryObservation(DeliveryReadState.NotReady, DeliveryReason.WrongContext, context, null,
            retained ? addon->SelectedFilter : -1, retained ? addon->SelectedSortBy : -1, mapGeneration,
            inventory->GetCompanySeals(company), inventory->GetMaxCompanySeals(company), [], source, sourceReadable, None());
        if (!retained || addon->SelectedTab != 2 || agent == null || agent->SelectedTab != 2) return observation;
        if (open) observation = observation with { Anchor = new(addon->X, addon->Y, addon->GetScaledWidth(true), addon->GetScaledHeight(true), addon->GetScale()) };
        var dialog = ReadDialog(addon, agent, pending);
        observation = observation with { Dialog = dialog };
        if (pending is not null) return observation;
        if (addon->ShouldIgnoreInputs())
        {
            LastMappingCheck = $"Native list is ignoring inputs; UTC={DateTimeOffset.UtcNow:O}";
            return observation with { Reason = DeliveryReason.NotReady };
        }
        if (*((byte*)agent + 0x96) != 0)
        {
            LastMappingCheck = $"Waiting for native item-data refresh; RetryHandlePresent={*((uint*)((byte*)agent + 0x20)) != 0}; UTC={DateTimeOffset.UtcNow:O}";
            return observation with { Reason = DeliveryReason.NotReady };
        }
        if (addon->SelectedFilter is < 0 or > 2 || addon->SelectedSortBy is < 0 or > 3 ||
            addon->SelectedFilter != *((byte*)agent + 0x93) || addon->SelectedSortBy != *((byte*)agent + 0x92))
            return observation with { Reason = DeliveryReason.MappingMismatch, State = DeliveryReadState.Inconsistent };
        var list = addon->ExpertDeliveryList;
        if (list == null || list->IsUpdatePending || list->IsScrollRefreshPending || agent->ItemArray == null ||
            agent->NumItems < 11 || agent->NumItems > 2059 || *((byte*)agent + 0x94) == 0)
            return observation with { Reason = DeliveryReason.NotReady };
        var count = list->ListLength;
        var sorted = *(int**)((byte*)agent + 0x70);
        if (count < 0 || count > 2048 || sorted == null)
            return observation with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        // Mirror the native selection callback's sorted-index traversal and filters.
        // Addon +0x288 is a presentation cache and can retain a surrendered source.
        var records = new DeliverySelectionRecord[agent->NumItems];
        var order = new int[agent->NumItems];
        for (var j = 0; j < agent->NumItems; j++)
        {
            var record = (byte*)agent->ItemArray + j * 0xA0;
            records[j] = new(record[0x99], record[0x9B] != 0, *(uint*)(record + 0x80),
                *(int*)(record + 0x68), record[0x9D] != 0);
            order[j] = sorted[j];
        }
        if (!DeliverySelectionMapping.TryResolve(records, order, observation.Filter, count, out var selected, out var mappingCheck))
        {
            LastMappingCheck = $"{mappingCheck}; UTC={DateTimeOffset.UtcNow:O}";
            return observation with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        }
        var rows = new List<DeliveryRow>(count);
        for (var index = 0; index < selected.Count; index++)
        {
            var matched = selected[index];
            ref var item = ref agent->ItemArray[matched];
            var instance = ReadSource((int)item.Inventory, item.Slot, out var readable);
            if (!readable || instance is null || instance.ItemId != item.ItemId % 500000 ||
                instance.Quantity < 1 || item.SealReward <= 0)
            {
                LastMappingCheck = $"Native row={index}; SourceReadable={readable}; SourcePresent={instance != null}; ItemMatch={instance?.ItemId == item.ItemId % 500000}; QuantityValid={instance?.Quantity > 0}; RewardValid={item.SealReward > 0}; UTC={DateTimeOffset.UtcNow:O}";
                return observation with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
            }
            // Localized presentation is not source identity. Native sorted row,
            // exact item ID and inventory instance above bind the selection.
            var name = ItemName(instance.ItemId);
            rows.Add(new(index, matched, instance, !list->GetItemDisabledState(index),
                (uint)item.SealReward, name)); // final owned reward supplies the bonus-inclusive amount
        }
        if (!DeliveryRules.ValidMapping(rows)) return observation with { State = DeliveryReadState.Inconsistent, Reason = DeliveryReason.MappingMismatch };
        var hash = 1469598103934665603UL;
        foreach (var row in rows) { hash = Mix(hash, (ulong)row.AgentIndex); hash = Mix(hash, row.Source.Fingerprint); hash = Mix(hash, (ulong)row.Source.Container); hash = Mix(hash, (ulong)row.Source.Slot); }
        hash = Mix(hash, (ulong)observation.Filter); hash = Mix(hash, (ulong)observation.Sort); hash = Mix(hash, (ulong)context.Addon);
        if (mapHash != hash) { mapHash = hash; mapGeneration++; }
        return observation with { State = rows.Count == 0 ? DeliveryReadState.ReadyEmpty : DeliveryReadState.Ready, Reason = rows.Count == 0 ? DeliveryReason.Empty : DeliveryReason.None, Rows = rows, MapGeneration = mapGeneration };
    }

    private DeliveryDialog ReadDialog(AddonGrandCompanySupplyList* addon, AgentGrandCompanySupply* agent, DeliveryPending? pending)
    {
        var yesno = (AddonSelectYesno*)gameGui.GetAddonByName("SelectYesno", 1).Address;
        var reward = (AddonGrandCompanySupplyReward*)gameGui.GetAddonByName("GrandCompanySupplyReward", 1).Address;
        if (yesno != null && yesno->IsVisible && yesno->IsReady)
        {
            var generationMatches = pending is not null && pending.Generation == dispatchLedger.SelectionGeneration;
            var dialogIdMatches = *((int*)((byte*)agent + 0x80)) == yesno->Id;
            var rowMatches = pending is not null && *((int*)((byte*)agent + 0x8C)) == pending.Row.AgentIndex;
            var hqCallback = CallbackMatches((AtkUnitBase*)yesno, (nint)agent, 4);
            var materiaCallback = CallbackMatches((AtkUnitBase*)yesno, (nint)agent, 3);
            var hqPrompt = PromptMatches(yesno, 102434);
            var materiaPrompt = PromptMatches(yesno, 102433);
            // Retain the failed pending operation's checks after Finish clears pending.
            // Only booleans are exported: no pointers, character IDs or inventory contents.
            if (pending is not null)
                RecordDialogCheck($"Generation={generationMatches}; DialogId={dialogIdMatches}; SelectedRow={rowMatches}; HQCallback={hqCallback}; MateriaCallback={materiaCallback}; HQPrompt={hqPrompt}; MateriaPrompt={materiaPrompt}; PendingHQ={pending.Row.Source.Hq}");
            // Live evidence shows agent+0x80 can differ while this visible addon has
            // the exact supply-agent callback, selected source and localized HQ prompt.
            // Keep that comparison diagnostic-only. CallbackMatches resolves the visible
            // addon ID itself; dispatch also rechecks this identity and the inventory instance.
            var kind = DeliveryRules.WarningKind(generationMatches, rowMatches,
                hqCallback, materiaCallback, hqPrompt, materiaPrompt, pending?.Row.Source.Hq == true);
            return new(kind, yesno->Id, pending?.Row.Source.ItemId ?? 0, kind != DeliveryDialogKind.Unrelated, null, yesno->YesButton != null && yesno->YesButton->IsEnabled);
        }
        if (reward == null || !reward->IsVisible || !reward->IsReady) return None();
        var rewardGeneration = pending is not null && pending.Generation == dispatchLedger.SelectionGeneration;
        // List closure clears agent+0x60 even while the reward remains open.
        // Resolve the actual visible reward callback and verify its exact scene
        // receiver class against the fingerprint-gated executable instead.
        var receiverMatches = (long)agent == selectionAgent;
        var rewardCallback = RewardCallbackMatches((AtkUnitBase*)reward, out var callbackPresent, out var callbackKind, out var receiverClass);
        var rewardRow = pending is not null && *((int*)((byte*)agent + 0x88)) == pending.Row.VisibleIndex;
        var rewardLayout = reward->AtkValuesCount == 12;
        var rewardName = pending is not null && reward->AtkValuesCount > 4 &&
            Normalize(ReadText(reward->AtkValues[4])) == Normalize(ItemName(pending.Row.Source.ItemId));
        var correlated = NativeDeliveryPolicy.RewardCorrelated(rewardGeneration, receiverMatches, rewardCallback, rewardRow, rewardLayout, rewardName);
        if (pending is not null)
            RecordDialogCheck($"Reward: Generation={rewardGeneration}; AgentSame={receiverMatches}; CallbackPresent={callbackPresent}; CallbackKind={callbackKind}; ExpectedKind=2; ReceiverClass={receiverClass}; Callback={rewardCallback}; SelectedRow={rewardRow}; ValueCount={reward->AtkValuesCount}; ItemName={rewardName}; Buffs={BuffFingerprint() == selectionBuffs}; Enabled={reward->DeliverButton != null && reward->DeliverButton->IsEnabled}");
        uint? amount = null;
        if (correlated && NativeDeliveryPolicy.BuffsUnchanged(selectionBuffs, BuffFingerprint()) && TryUInt(reward->AtkValues[9], out var baseReward) && TryUInt(reward->AtkValues[10], out var bonus))
        {
            amount = NativeDeliveryPolicy.RewardAmount(baseReward, bonus);
        }
        return new(DeliveryDialogKind.Reward, reward->Id, pending?.Row.Source.ItemId ?? 0, correlated, amount, reward->DeliverButton != null && reward->DeliverButton->IsEnabled);
    }

    /// <summary>Requests one verified expert-list refresh in the retained context.</summary>
    public DeliveryDispatchResult RefreshExpertList(DeliveryRefreshRequest request)
    {
        if (disposed || !reviewedBuild) return DeliveryDispatchResult.Rejected(DeliveryReason.UnsupportedBuild);
        var current = Observe(null);
        var addon = (AddonGrandCompanySupplyList*)gameGui.GetAddonByName("GrandCompanySupplyList", 1).Address;
        var agent = AgentGrandCompanySupply.Instance();
        if (!dispatchLedger.CanRefresh(request.Generation) ||
            current.Context != request.Context || agent == null || (long)agent != selectionAgent)
            return DeliveryDispatchResult.Rejected(DeliveryReason.WrongContext);
        if (current.Filter != request.Filter || current.Sort != request.Sort || addon == null ||
            addon->SelectedTab != 2 || agent->SelectedTab != 2 ||
            addon->SelectedFilter != *((byte*)agent + 0x93) || addon->SelectedSortBy != *((byte*)agent + 0x92))
            return DeliveryDispatchResult.Rejected(DeliveryReason.FilterChanged);
        if (!addon->IsReady || !addon->IsVisible || addon->ShouldIgnoreInputs() ||
            current.Dialog.Kind != DeliveryDialogKind.None || *((byte*)agent + 0x95) != 0)
            return DeliveryDispatchResult.Rejected(DeliveryReason.NotReady);
        if (!CallbackMatches((AtkUnitBase*)addon, (nint)agent, 0))
            return DeliveryDispatchResult.Rejected(DeliveryReason.WrongContext);
        // Ordinary Expert-tab activation: command 0, same tab 2, no close.
        // Record before the callback; an ambiguous native failure is never retried.
        if (!dispatchLedger.TryRecordRefresh(request.Generation)) return DeliveryDispatchResult.Rejected(DeliveryReason.WrongContext);
        AtkValue* values = stackalloc AtkValue[3];
        values[0] = new() { Type = AtkValueType.Int, Int = 0 };
        values[1] = new() { Type = AtkValueType.Int, Int = 2 };
        values[2] = new() { Type = AtkValueType.Int, Int = 0 };
        try
        {
            addon->FireCallback(3, values, false);
            mapHash = 0;
            mapGeneration++;
            return DeliveryDispatchResult.Sent;
        }
        catch { return new(false, true, DeliveryReason.DispatchRejected); }
    }

    /// <summary>Revalidates and dispatches one generation-bound native delivery action.</summary>
    public DeliveryDispatchResult Dispatch(DeliveryCommand command)
    {
        if (disposed || !reviewedBuild) return DeliveryDispatchResult.Rejected(DeliveryReason.UnsupportedBuild);
        var p = command.Pending;
        // Every observation rebuilds the native binding, including immediately before selection.
        var current = Observe(command.Kind == DeliveryCommandKind.Select ? null : p);
        if (current.Context != p.Context || current.Filter != p.Filter || current.Sort != p.Sort)
            return DeliveryDispatchResult.Rejected(DeliveryReason.WrongContext);
        if (dispatchLedger.IsDuplicate(p.Generation, command.Kind, p.DialogIdentity)) return DeliveryDispatchResult.Rejected(DeliveryReason.DispatchRejected);
        var addon = (AddonGrandCompanySupplyList*)gameGui.GetAddonByName("GrandCompanySupplyList", 1).Address;
        var agent = AgentGrandCompanySupply.Instance();
        if (addon == null || agent == null) return DeliveryDispatchResult.Rejected(DeliveryReason.NotReady);
        if (command.Kind == DeliveryCommandKind.Select)
        {
            if (!dispatchLedger.IsNewSelection(p.Generation) || current.State != DeliveryReadState.Ready || current.MapGeneration != command.MapGeneration ||
                p.Row.VisibleIndex >= current.Rows.Count || current.Rows[p.Row.VisibleIndex] != p.Row || !p.Row.Available || current.Dialog.Kind != DeliveryDialogKind.None)
                return DeliveryDispatchResult.Rejected(DeliveryReason.MappingMismatch);
            if (!dispatchLedger.TryBeginSelection(p.Generation)) return DeliveryDispatchResult.Rejected(DeliveryReason.MappingMismatch);
            RecordDialogCheck("Selection sent; no pending delivery warning observed");
            selectionBuffs = BuffFingerprint();
            // Pin the supply agent. Read its reward receiver only after creation,
            // then require the visible reward's callback to name that exact receiver.
            selectionAgent = (long)agent;
            return Fire((AtkUnitBase*)addon, command, 1, p.Row.VisibleIndex, 0);
        }
        if (!current.PendingSourceReadable || current.PendingSource != p.Row.Source || !current.Dialog.Correlated || current.Dialog.Identity != p.DialogIdentity)
            return DeliveryDispatchResult.Rejected(DeliveryReason.UnrelatedDialog);
        if (command.Kind is DeliveryCommandKind.ConfirmHq or DeliveryCommandKind.ConfirmSpiritbond)
        {
            var required = command.Kind == DeliveryCommandKind.ConfirmHq ? DeliveryDialogKind.HqWarning : DeliveryDialogKind.SpiritbondWarning;
            if (current.Dialog.Kind != required || !current.Dialog.Enabled)
                return DeliveryDispatchResult.Rejected(DeliveryReason.UnrelatedDialog);
            return Fire((AtkUnitBase*)gameGui.GetAddonByName("SelectYesno", 1).Address, command, 0);
        }
        if (current.Dialog.Kind != DeliveryDialogKind.Reward) return DeliveryDispatchResult.Rejected(DeliveryReason.UnrelatedDialog);
        var target = (AtkUnitBase*)gameGui.GetAddonByName("GrandCompanySupplyReward", 1).Address;
        if (command.Kind == DeliveryCommandKind.CancelReward) return Fire(target, command, 1);
        if (command.Kind != DeliveryCommandKind.Submit || !current.Dialog.Enabled || current.Dialog.Reward != p.Reward ||
            current.Seals != p.BeforeSeals || !DeliveryRules.RewardFits(current.Seals, current.SealCap, current.Dialog.Reward))
            return DeliveryDispatchResult.Rejected(DeliveryReason.RewardUnknown);
        return Fire(target, command, 0);
    }

    private DeliveryDispatchResult Fire(AtkUnitBase* addon, DeliveryCommand command, params int[] parameters)
    {
        if (addon == null || !addon->IsReady || !addon->IsVisible) return DeliveryDispatchResult.Rejected(DeliveryReason.NotReady);
        // Record BEFORE entering native code. Any exception is a possible dispatch and may not be retried.
        if (!dispatchLedger.TryRecordDispatch(command.Pending.Generation, command.Kind, command.Pending.DialogIdentity))
            return DeliveryDispatchResult.Rejected(DeliveryReason.DispatchRejected);
        AtkValue* values = stackalloc AtkValue[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) values[i] = new() { Type = AtkValueType.Int, Int = parameters[i] };
        // Selecting a row keeps the list open. Acknowledging an owned modal must
        // close it so the native agent can advance to the reward/next list state.
        var close = command.Kind != DeliveryCommandKind.Select;
        try { addon->FireCallback((uint)parameters.Length, values, close); return DeliveryDispatchResult.Sent; }
        catch { return new(false, true, DeliveryReason.DispatchRejected); }
    }

    private bool RewardCallbackMatches(AtkUnitBase* addon, out bool present, out ulong kind, out bool receiverClass)
    {
        present = false;
        kind = 0;
        receiverClass = false;
        var ui = UIModule.Instance();
        if (!reviewedBuild || rewardReceiverVtable == 0 || ui == null) return false;
        var module = ui->GetRaptureAtkModule();
        if (module == null || !module->AtkModule.AddonCallbackMapping.TryGetValue(addon->Id, out var entry, false)) return false;
        present = entry.EventInterface != null;
        kind = entry.EventKind;
        if (present) receiverClass = *(nint*)entry.EventInterface == rewardReceiverVtable;
        return NativeDeliveryPolicy.RewardCallbackMatches(present, kind, receiverClass);
    }

    private static bool CallbackMatches(AtkUnitBase* addon, nint receiver, ulong kind)
    {
        var ui = UIModule.Instance();
        if (ui == null || receiver == 0) return false;
        var module = ui->GetRaptureAtkModule();
        if (module == null) return false;
        return module->AtkModule.AddonCallbackMapping.TryGetValue(addon->Id, out var entry, false) && (nint)entry.EventInterface == receiver && entry.EventKind == kind;
    }

    private bool PromptMatches(AddonSelectYesno* addon, uint rowId) => addon->PromptText != null &&
        Normalize(SeString.Parse(addon->PromptText->NodeText.AsSpan()).TextValue) == Normalize(dataManager.GetExcelSheet<Addon>().GetRow(rowId).Text.ExtractText());
    private string ItemName(uint id) => dataManager.GetExcelSheet<Item>().GetRow(id).Name.ExtractText();
    private static string Normalize(string value) => value.Replace("\uE03C", "", StringComparison.Ordinal).Trim();
    private static string ReadText(AtkValue value) => value.Type is AtkValueType.String or AtkValueType.ManagedString ? SeString.Parse(value.String.Value).TextValue : "";
    private static bool TryUInt(AtkValue value, out uint result)
    {
        result = 0;
        if (value.Type == AtkValueType.UInt) { result = value.UInt; return true; }
        if (value.Type == AtkValueType.Int && value.Int >= 0) { result = (uint)value.Int; return true; }
        return false;
    }
    private static DeliveryDialog None() => new(DeliveryDialogKind.None, 0, 0, false, null, false);
    private static bool SourceReadable(int container, int slot) { _ = ReadSource(container, slot, out var readable); return readable; }
    private static DeliverySource? ReadSource(int container, int slot, out bool readable)
    {
        readable = false;
        var manager = InventoryManager.Instance();
        if (manager == null || slot < 0) return null;
        var bag = manager->GetInventoryContainer((InventoryType)container);
        if (bag == null || !bag->IsLoaded || slot >= bag->Size) return null;
        var item = bag->GetInventorySlot(slot);
        if (item == null || item->IsSymbolic) return null;
        readable = true;
        if (item->ItemId == 0 && item->Quantity == 0) return null;
        var hash = Mix(1469598103934665603UL, item->ItemId);
        hash = Mix(hash, (byte)item->Flags); hash = Mix(hash, item->SpiritbondOrCollectability); hash = Mix(hash, item->Condition); hash = Mix(hash, item->GlamourId); hash = Mix(hash, item->CrafterContentId);
        foreach (var materia in item->Materia) hash = Mix(hash, materia);
        foreach (var grade in item->MateriaGrades) hash = Mix(hash, grade);
        foreach (var stain in item->Stains) hash = Mix(hash, stain);
        return new(container, slot, item->GetItemId(), item->GetBaseItemId(), item->IsHighQuality(), item->Quantity, hash);
    }
    private static ulong BuffFingerprint()
    {
        var player = Control.GetLocalPlayer();
        if (player == null) return 0;
        var hash = 1469598103934665603UL;
        foreach (var status in player->StatusManager.Status)
        {
            if (status.StatusId == 0) continue;
            hash = Mix(hash, status.StatusId); hash = Mix(hash, status.Param);
        }
        return hash;
    }
    private static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
}
