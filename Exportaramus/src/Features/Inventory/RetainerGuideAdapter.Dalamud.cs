using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Exportaramus.Features.CollectionData.Capture;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace Exportaramus.Features.Inventory;

public sealed unsafe class RetainerGuideAdapter : IRetainerGuidePort, IDisposable
{
    private readonly InventoryCaptureAdapter capture;
    private readonly IDataManager data;
    private readonly IPlayerState player;
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private readonly IObjectTable objects;
    private readonly ITargetManager target;
    private readonly ICondition conditions;
    private readonly ImmutableHashSet<uint> bellIds;
    private readonly string inventoryPrefix;
    private readonly string quitLabel;
    private readonly RetainerGuideController controller;
    private nint capturedAddon;
    private bool inventoryCloseRequested;
    private string capturedAddonName = string.Empty;
    private long nextUpdate;
    private string selectionStatus = "";
    private ulong selectedRetainer;
    private string selectedName = "";
    private long nextDialogue;
    private readonly IAddonLifecycle lifecycle;
    private readonly IPluginLog log;
    private int tracedEvents;
    private int dialogueAttempts;

    public RetainerGuideAdapter(InventoryCaptureAdapter capture, IDataManager data, IPlayerState player,
        IFramework framework, IGameGui gui, IObjectTable objects, ITargetManager target, ICondition conditions,
        IAddonLifecycle lifecycle, IPluginLog log)
    {
        this.capture = capture; this.data = data; this.player = player; this.framework = framework;
        this.gui = gui; this.objects = objects; this.target = target; this.conditions = conditions;
        bellIds = data.GetExcelSheet<EObjName>(ClientLanguage.English)
            .Where(row => row.Singular.ExtractText().Equals("Summoning Bell", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.RowId).ToImmutableHashSet();
        // Local Addon 2378 is the inventory menu, with the filled-slot number as its first macro.
        // Keep all localized text before that macro and require one unique matching menu entry.
        var template = data.GetExcelSheet<Addon>().GetRow(2378).Text.Data.Span;
        var macro = template.IndexOf((byte)2);
        inventoryPrefix = Encoding.UTF8.GetString(macro >= 0 ? template[..macro] : template).Trim();
        quitLabel = data.GetExcelSheet<Addon>().GetRow(5).Text.ExtractText();
        controller = new(this);
        this.lifecycle = lifecycle; this.log = log;
        lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Talk", TraceTalkInput);
    }

    public bool Active => controller.Active;
    public string Message => controller.Message + (selectionStatus.Length > 0 ? " — " + selectionStatus : "");
    public void Start()
    {
        if (!framework.IsInFrameworkUpdateThread) return;
        capturedAddon = 0; capturedAddonName = string.Empty; inventoryCloseRequested = false;
        selectionStatus = "";
        selectedRetainer = 0; selectedName = ""; nextDialogue = 0;
        tracedEvents = 0; dialogueAttempts = 0;
        controller.Start(player.IsLoaded ? player.ContentId : 0, TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
    public void Cancel() => controller.Cancel();
    public void Dispose()
    {
        Cancel();
        lifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "Talk", TraceTalkInput);
    }
    private void TraceTalkInput(AddonEvent kind, AddonArgs args)
    {
        if (!Active || selectedRetainer == 0 || tracedEvents >= 16 || args is not AddonReceiveEventArgs input) return;
        tracedEvents++;
        log.Warning("Retainer Talk input: step={Step}, type={Type}, parameter={Parameter}, attempts={Attempts}",
            controller.Step, input.AtkEventType, input.EventParam, dialogueAttempts);
        var evt = (AtkEvent*)input.AtkEvent;
        var eventData = (AtkEventData*)input.AtkEventData;
        if (evt != null)
            log.Warning("Retainer Talk input shape: node={HasNode}, stageTarget={StageTarget}, stateType={StateType}, button={Button}",
                evt->Node != null, evt->Target == (AtkEventTarget*)AtkStage.Instance(), evt->State.EventType,
                eventData == null ? -1 : eventData->MouseData.ButtonId);
    }
    public void Update()
    {
        if (!framework.IsInFrameworkUpdateThread || !Active || Environment.TickCount64 < nextUpdate) return;
        nextUpdate = Environment.TickCount64 + 100;
        try
        {
            if (controller.Step is RetainerGuideStep.OpenInventory or RetainerGuideStep.WaitForList
                && IsSafe(player.ContentId)) AdvanceRetainerDialogue();
            controller.Update(TimeSpan.FromMilliseconds(Environment.TickCount64));
        }
        catch (Exception) { controller.Cancel(); }
    }

    public bool IsSafe(ulong contentId) => framework.IsInFrameworkUpdateThread && contentId != 0
        && player.IsLoaded && player.ContentId == contentId && bellIds.Count != 0 && inventoryPrefix.Length > 0
        && data.GameData.Repositories.Values.Single(r => r.ExpansionId == 0).Version == ReaderCompatibility.CurrentGameVersion
        && !conditions[ConditionFlag.InCombat] && !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51]
        && !conditions[ConditionFlag.WatchingCutscene] && !conditions[ConditionFlag.OccupiedInCutSceneEvent] && !conditions[ConditionFlag.Mounted];
    public bool ListReady => Ready(gui.GetAddonByName<AtkUnitBase>("RetainerList", 1));
    public bool InventoryVisible => Visible(gui.GetAddonByName<AtkUnitBase>("InventoryRetainer", 1))
        || Visible(gui.GetAddonByName<AtkUnitBase>("InventoryRetainerLarge", 1));

    public bool OpenBell()
    {
        if (Visible(gui.GetAddonByName<AtkUnitBase>("SelectString", 1)) || InventoryVisible) return false;
        var origin = objects.LocalPlayer?.Position;
        if (origin is null) return false;
        var bell = objects.Take(1024).Where(obj => obj.IsValid() && obj.IsTargetable && obj.ObjectKind == ObjectKind.EventObj && bellIds.Contains(obj.BaseId))
            .OrderBy(obj => Vector3.DistanceSquared(origin.Value, obj.Position)).ThenBy(obj => obj.GameObjectId).FirstOrDefault();
        if (bell is null) return false;
        if (target.Target?.GameObjectId != bell.GameObjectId) target.Target = bell;
        if (Vector3.Distance(origin.Value, bell.Position) > 2.5f) return false;
        var system = TargetSystem.Instance();
        if (system == null || target.Target?.GameObjectId != bell.GameObjectId || !bell.IsValid() || !bell.IsTargetable) return false;
        _ = system->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bell.Address, true);
        return true;
    }

    public bool TryReadRoster(out ImmutableArray<InventoryRetainer> roster)
    {
        roster = [];
        if (!ListReady) return false;
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady) return false;
        var count = manager->GetRetainerCount();
        if (count > 10) return false;
        var values = ImmutableArray.CreateBuilder<InventoryRetainer>(count);
        for (uint i = 0; i < count; i++)
        {
            var row = manager->GetRetainerBySortedIndex(i);
            if (row == null || !row->Available || row->RetainerId == 0 || !InventoryPolicy.ValidName(row->NameString)) return false;
            values.Add(new(row->RetainerId, row->NameString));
        }
        if (!manager->IsReady || manager != RetainerManager.Instance() || count != manager->GetRetainerCount()) return false;
        roster = values.ToImmutable();
        return roster.Select(row => row.RetainerId).Distinct().Count() == roster.Length;
    }

    public bool SelectRetainer(ulong retainerId)
    {
        selectionStatus = "Waiting for the Retainer list to be ready.";
        var addon = gui.GetAddonByName<AtkUnitBase>("RetainerList", 1);
        var agent = AgentRetainerList.Instance();
        if (!Ready(addon) || agent == null || !agent->IsAgentActive() || !TryReadRoster(out var roster)) return false;
        var index = -1;
        for (var i = 0; i < roster.Length; i++) if (roster[i].RetainerId == retainerId) index = i;
        if (index < 0 || roster.Count(row => row.Name == roster[index].Name) != 1) return false;
        // Resolve the displayed row independently of undocumented agent count/order fields.
        var list = FindRetainerList(addon, roster[index].Name, out var displayedIndex);
        selectionStatus = "Waiting for an enabled Retainer row with the expected name.";
        if (list == null || list->GetItemDisabledState(displayedIndex) || !Ready(addon)) return false;
        list->DispatchItemEvent(displayedIndex, AtkEventType.ListItemClick);
        selectedRetainer = retainerId; selectedName = roster[index].Name;
        selectionStatus = "";
        return true;
    }

    public bool OpenInventory(ulong retainerId) => ActiveRetainer(retainerId) && SelectMenu(inventoryPrefix, true);
    public bool CaptureInventory(ulong contentId, ulong retainerId)
    {
        if (!capture.CaptureActiveRetainer(contentId, retainerId)) return false;
        capturedAddonName = "InventoryRetainer";
        var addon = gui.GetAddonByName<AtkUnitBase>(capturedAddonName, 1);
        if (!Ready(addon)) { capturedAddonName = "InventoryRetainerLarge"; addon = gui.GetAddonByName<AtkUnitBase>(capturedAddonName, 1); }
        if (!Ready(addon)) return false;
        capturedAddon = (nint)addon;
        inventoryCloseRequested = false;
        return true;
    }
    public bool CloseInventory(ulong retainerId)
    {
        if (capturedAddon == 0) return false;
        var addon = gui.GetAddonByName<AtkUnitBase>(capturedAddonName, 1);
        if (inventoryCloseRequested)
        {
            if (InventoryVisible || !RetainerContextCleared()) return false;
            capturedAddon = 0;
            inventoryCloseRequested = false;
            return true;
        }
        if (!ActiveRetainer(retainerId)) return false;
        if ((nint)addon != capturedAddon || !Ready(addon)) return false;
        var retainer = AgentRetainer.Instance();
        var module = RaptureAtkModule.Instance();
        uint callbackId = addon->ParentId != 0 ? addon->ParentId : addon->Id;
        if (retainer == null || module == null ||
            !module->AddonCallbackMapping.TryGetValue(in callbackId, out var callback, false) ||
            callback.AgentInterface != (AgentInterface*)retainer || callback.EventKind != 0) return false;
        // On the validated client both Retainer layouts send -2 from Close(true), which
        // removes the addon without clearing Entrust mode. The owned -1 callback runs
        // the native exit path. Its return value is not a completion acknowledgement.
        inventoryCloseRequested = true;
        _ = addon->FireCallbackInt(-1);
        return false;
    }
    public bool QuitRetainer(ulong retainerId) => RetainerContextCleared() &&
        (ActiveRetainer(retainerId) ? SelectMenu(quitLabel, false) : ListReady);

    private static bool RetainerContextCleared()
    {
        var inventory = AgentInventory.Instance();
        var retainer = AgentRetainer.Instance();
        return inventory != null && retainer != null &&
            inventory->CurrentInventoryContextEvent != &retainer->InventoryContextEvent &&
            inventory->OpenTitleId is not (4 or 8);
    }

    public bool CloseOverviewAndInventory()
    {
        // Only reached after the last retainer has returned to the overview.
        if (!RetainerContextCleared()) return false;
        foreach (var name in new[] { "Inventory", "InventoryLarge", "InventoryExpansion", "RetainerList" })
        {
            var addon = gui.GetAddonByName<AtkUnitBase>(name, 1);
            if (!Visible(addon)) continue;
            if (Ready(addon)) _ = addon->Close(true);
            return false;
        }
        return true;
    }

    private bool SelectMenu(string label, bool prefix)
    {
        var addon = gui.GetAddonByName<AddonSelectString>("SelectString", 1);
        if (!Ready((AtkUnitBase*)addon) || addon->PopupMenu.EntryCount is < 1 or > 64 || addon->PopupMenu.EntryNames == null) return false;
        var list = addon->PopupMenu.List;
        if (list == null || list->ListLength != addon->PopupMenu.EntryCount || !list->IsItemInteractionEnabled) return false;
        var entries = new string[addon->PopupMenu.EntryCount];
        for (var i = 0; i < addon->PopupMenu.EntryCount; i++)
        {
            entries[i] = ReadText(addon->PopupMenu.EntryNames[i]);
        }
        var index = RetainerMenuPolicy.FindUniqueIndex(entries, label, prefix);
        if (index < 0 || list->GetItemDisabledState(index)) return false;
        list->DispatchItemEvent(index, AtkEventType.ListItemClick);
        return true;
    }

    private static AtkComponentList* FindRetainerList(AtkUnitBase* addon, string name, out int displayedIndex)
    {
        displayedIndex = -1;
        if (addon->UldManager.NodeList == null || addon->UldManager.NodeListCount > 2048) return null;
        AtkComponentList* result = null;
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null) continue;
            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null) continue;
            // TreeList inherits AtkComponentList at offset zero in the installed client structs.
            // RetainerList can use this derived control rather than an exact List component.
            var componentType = componentNode->Component->GetComponentType();
            if (componentType is not (ComponentType.List or ComponentType.TreeList)) continue;
            var list = (AtkComponentList*)componentNode->Component;
            if (list->ListLength is < 1 or > 32 || !list->IsItemInteractionEnabled) continue;
            for (var rowIndex = 0; rowIndex < list->ListLength; rowIndex++)
            {
                var renderer = list->GetItemRenderer(rowIndex);
                if (renderer == null || renderer->UldManager.NodeList == null || renderer->UldManager.NodeListCount > 128) continue;
                var named = false;
                for (var j = 0; j < renderer->UldManager.NodeListCount; j++)
                {
                    var child = renderer->UldManager.NodeList[j];
                    if (child == null) continue;
                    var text = child->GetAsAtkTextNode();
                    if (text != null && ReadText(text->NodeText.StringPtr) == name) named = true;
                }
                if (!named) continue;
                if (result != null) return null;
                result = list;
                displayedIndex = rowIndex;
            }
        }
        return result;
    }

    private void AdvanceRetainerDialogue()
    {
        if (selectedRetainer == 0 || Environment.TickCount64 < nextDialogue) return;
        var talk = gui.GetAddonByName<AtkUnitBase>("Talk", 1);
        if (!Ready(talk)) { selectionStatus = "Waiting for the Retainer dialogue or menu."; return; }
        if (talk->UldManager.NodeList == null || talk->UldManager.NodeListCount > 256)
        { selectionStatus = "Dialogue controls are not ready."; return; }
        var named = false;
        for (var i = 0; i < talk->UldManager.NodeListCount; i++)
        {
            var node = talk->UldManager.NodeList[i];
            if (node == null) continue;
            var text = node->GetAsAtkTextNode();
            if (text != null && ReadText(text->NodeText.StringPtr) == selectedName) named = true;
        }
        if (!named) { selectionStatus = "Waiting for the dialogue speaker to match the selected Retainer."; return; }
        if (controller.Step == RetainerGuideStep.OpenInventory && !ActiveRetainer(selectedRetainer))
        { selectionStatus = "Waiting for the active Retainer identity during dialogue."; return; }
        // Match the user's observed Talk click: normal receiver, stage target, matching state.
        var stage = AtkStage.Instance();
        if (stage == null) { selectionStatus = "Dialogue input is not ready."; return; }
        AtkEvent click = default;
        click.Target = (AtkEventTarget*)stage;
        click.Listener = (AtkEventListener*)talk;
        click.State.EventType = AtkEventType.MouseClick;
        AtkEventData eventData = default;
        nextDialogue = Environment.TickCount64 + 750;
        dialogueAttempts++;
        selectionStatus = $"Dialogue advance attempted ({dialogueAttempts}); waiting for the game.";
        if (dialogueAttempts == 1)
            log.Warning("Retainer Talk: sending observed stage MouseClick through ReceiveEvent, parameter 0; identity checks passed.");
        talk->ReceiveEvent(AtkEventType.MouseClick, 0, &click, &eventData);
    }
    private static bool ActiveRetainer(ulong id)
    {
        var manager = RetainerManager.Instance();
        return id != 0 && manager != null && manager->IsReady && manager->GetActiveRetainer() != null && manager->GetActiveRetainer()->RetainerId == id;
    }
    private static bool Ready(AtkUnitBase* addon) => Visible(addon) && addon->IsReady;
    private static bool Visible(AtkUnitBase* addon) => addon != null && addon->IsVisible;
    private static string ReadText(byte* bytes)
    {
        if (bytes == null) return string.Empty;
        for (var length = 0; length < 1024; length++)
            if (bytes[length] == 0) return SeString.Parse(new ReadOnlySpan<byte>(bytes, length)).TextValue;
        return string.Empty;
    }
}
