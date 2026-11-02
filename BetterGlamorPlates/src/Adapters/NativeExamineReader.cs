using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BetterGlamorPlates.Adapters;

/// <summary>Reads only the currently displayed, ready Examine character on the framework thread.</summary>
internal sealed class NativeExamineReader(IGameGui gameGui, IObjectTable objects, DiagnosticJournal? diagnostics = null)
{
    internal unsafe ExamineView? ReadView()
    {
        var agent = AgentInspect.Instance();
        var stage = FFXIVClientStructs.FFXIV.Component.GUI.AtkStage.Instance();
        FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* unit = null;
        if (agent != null && agent->AddonId is > 0 and <= ushort.MaxValue
            && stage != null && stage->RaptureAtkUnitManager != null)
            unit = stage->RaptureAtkUnitManager->GetAddonById((ushort)agent->AddonId);
        if (unit == null || !unit->IsVisible)
        {
            var addon = gameGui.GetAddonByName("CharacterInspect");
            if (addon.IsNull || !addon.IsVisible) return null;
            unit = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon.Address;
        }

        var bounds = new WindowBounds(unit->X, unit->Y, unit->GetScaledWidth(true), unit->GetScaledHeight(true));
        var occluded = IsSaveIconOccluded(unit, bounds);
        if (agent == null || !agent->IsAgentActive())
            return new ExamineView(0, string.Empty, bounds, ExamineReadPolicy.UnavailableReason(
                new ExamineReadFacts(false, false, 0, 0, 0, false, 0, false)))
            { OverlayOccluded = occluded };
        var entity = agent->CurrentEntityId;
        var character = objects.SearchByEntityId(entity) as IPlayerCharacter;
        var name = character?.Name.TextValue ?? string.Empty;
        var facts = new ExamineReadFacts(true, agent->IsBuddyInspect,
            agent->FetchCharacterDataStatus, agent->CharaView.State, entity, character is not null,
            agent->RequestEntityId, true);
        var reason = ExamineReadPolicy.UnavailableReason(facts);
        if (reason is null) reason = ExamineReadPolicy.UnavailableReason(facts with { EquipmentMatches = EquipmentMatches(agent) });
        return new ExamineView(entity, name, bounds, reason) { OverlayOccluded = occluded };
    }

    private static unsafe bool IsSaveIconOccluded(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* anchor, WindowBounds bounds)
    {
        var stage = FFXIVClientStructs.FFXIV.Component.GUI.AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return true;
        var layers = stage->RaptureAtkUnitManager->AtkUnitManager.DepthLayers;
        if (anchor->DepthLayer >= layers.Length) return true;
        ref var anchorList = ref layers[(int)anchor->DepthLayer];
        if (anchorList.Count > anchorList.Entries.Length) return true;
        var anchorIndex = -1;
        for (var index = 0; index < anchorList.Count; index++)
            if (anchorList.Entries[index].Value == anchor) { anchorIndex = index; break; }
        if (anchorIndex < 0) return true;
        var windows = new List<ExamineOverlayFact>();
        for (var layerIndex = (int)anchor->DepthLayer; layerIndex < layers.Length; layerIndex++)
        {
            ref var layer = ref layers[layerIndex];
            if (layer.Count > layer.Entries.Length) return true;
            for (var index = 0; index < layer.Count; index++)
            {
                if (windows.Count >= 1024) return true;
                var candidate = layer.Entries[index].Value;
                if (candidate == null || candidate->DepthLayer != (uint)layerIndex) return true;
                windows.Add(new ExamineOverlayFact((uint)layerIndex, index, true, candidate == anchor,
                    candidate->IsVisible, candidate->Alpha, candidate->WindowNode != null, candidate->NameString,
                    new WindowBounds(candidate->X, candidate->Y, candidate->GetScaledWidth(true), candidate->GetScaledHeight(true))));
            }
        }
        return ExamineReadPolicy.IsOccluded(bounds, anchor->DepthLayer, anchorIndex, windows);
    }
    internal unsafe SavedPlate? Capture(uint expectedEntity, string expectedName, DateTime now, Guid id)
    {
        var view = ReadView();
        var agent = AgentInspect.Instance();
        if (diagnostics is not null) ObserveDiagnostics(view, diagnostics);
        ExamineReadPolicy.ObserveExpected(view, expectedEntity, expectedName, diagnostics);
        if (!ExamineReadPolicy.CanCapture(view, expectedEntity, expectedName))
            return null;
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.Examine);
        var appearances = new List<ExaminedAppearance>(13);
        // Inspect uses SetModelData, not the editable CharaView.Items/CharacterLoaded path.
        // Its UI reads the same Examine inventory stains for the displayed appearance.
        for (var index = 0; index < 13; index++)
        {
            var slot = ExaminedOutfitPolicy.ToInventorySlot(index);
            var item = container->GetInventorySlot(slot);
            var displayed = agent->Items[index];
            var stain0 = item->GetStain(0);
            var stain1 = item->GetStain(1);
            appearances.Add(new ExaminedAppearance((byte)slot, displayed.ItemId, displayed.GlamourItemId,
                stain0, stain1, stain0, stain1));
        }
        var after = ReadView();
        ExamineReadPolicy.ObserveExpected(after, expectedEntity, expectedName, diagnostics);
        return ExamineReadPolicy.CanCapture(after, expectedEntity, expectedName)
            ? ExaminedOutfitPolicy.Create(view!.Name, appearances, now, id)
            : null;
    }

    internal unsafe void ObserveDiagnostics(ExamineView? view, DiagnosticJournal journal)
    {
        var agent = AgentInspect.Instance();
        var facts = agent == null ? null : new ExamineReadFacts(agent->IsAgentActive(), agent->IsBuddyInspect,
            agent->FetchCharacterDataStatus, agent->CharaView.State, agent->CurrentEntityId, false,
            agent->RequestEntityId, EquipmentMatches(agent, journal));
        ExamineReadPolicy.ObserveDiagnostics(view, facts, journal);
    }

    private static unsafe bool EquipmentMatches(AgentInspect* agent, DiagnosticJournal? journal = null)
    {
        var manager = InventoryManager.Instance();
        journal?.Check(DiagnosticKey.DataPresent, 1, manager == null ? 0 : 1);
        if (manager == null) return false;
        var container = manager->GetInventoryContainer(InventoryType.Examine);
        journal?.Check(DiagnosticKey.ExamineInventoryLoaded, 1, container == null ? null : container->IsLoaded ? 1 : 0);
        journal?.Check(DiagnosticKey.SlotCount, 14, container == null ? null : container->Size);
        if (container == null || !container->IsLoaded || container->Size < 14) return false;
        var items = new List<ExamineEquipmentFact>(13);
        for (var index = 0; index < 13; index++)
        {
            var item = container->GetInventorySlot(ExaminedOutfitPolicy.ToInventorySlot(index));
            var displayed = agent->Items[index];
            items.Add(new ExamineEquipmentFact(item == null ? 0 : item->GetItemId(), item == null ? 0 : item->GetGlamourId(),
                displayed.ItemId, displayed.GlamourItemId, item != null));
        }
        return ExamineReadPolicy.EquipmentMatches(items, journal);
    }
}





