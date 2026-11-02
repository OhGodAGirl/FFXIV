using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Main;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismMiragePlateData;

namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Reports native editor and item facts and performs only an already validated
/// plate plan. Product eligibility and next-action policy remain in the
/// controller and pure planner.
/// </summary>
internal sealed partial class NativePlateEditor : IPlateEditorMechanism, IItemFacts
{
    internal const string AddonName = "MiragePrismMiragePlate";
    private const string DresserAddonName = "MiragePrismPrismBox";
    private const string ArmoireAddonName = "CabinetWithdraw";

    private readonly IClientState clientState;
    private readonly IDataManager dataManager;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private bool previewActive;
    private readonly DiagnosticJournal diagnostics;

    internal NativePlateEditor(
        IClientState clientState,
        IDataManager dataManager,
        IGameGui gameGui,
        IPluginLog log, DiagnosticJournal? diagnostics = null)
    {
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.gameGui = gameGui;
        this.log = log;
        this.diagnostics = diagnostics ?? new DiagnosticJournal();
    }

    public bool IsOpen => IsInnPlateEditorOpen(gameGui);

    internal unsafe bool IsDyeWindowOpen
    {
        get
        {
            var agent = AgentColorant.Instance();
            var stage = FFXIVClientStructs.FFXIV.Component.GUI.AtkStage.Instance();
            if (agent != null && agent->AddonId is > 0 and <= ushort.MaxValue
                && stage != null && stage->RaptureAtkUnitManager != null)
            {
                var unit = stage->RaptureAtkUnitManager->GetAddonById((ushort)agent->AddonId);
                if (unit != null && unit->IsVisible) return true;
            }
            var addon = gameGui.GetAddonByName("Colorant");
            return !addon.IsNull && addon.IsVisible;
        }
    }

    internal IReadOnlyList<DyeOption> ReadDyes()
        => DyePreparation.Options(dataManager.GetExcelSheet<Stain>()
            .Where(dye => DyePreparation.CanReadDyeRow(dye.RowId, dye.Shade, dye.Name.IsEmpty))
            .Select(dye => new DyeRowFact(
            dye.RowId, dye.Shade, dye.SubOrder, dye.Name.ToString(), dye.Color,
            dye.Item.Select(item => new DyeReagentFact(item.RowId, item.RowId == 0 ? string.Empty : item.Value.Name.ToString())).ToArray())));

    internal static bool IsInnPlateEditorOpen(IGameGui gameGui)
    {
        var plate = gameGui.GetAddonByName(AddonName);
        var dresser = gameGui.GetAddonByName(DresserAddonName);
        var armoire = gameGui.GetAddonByName(ArmoireAddonName);
        return !plate.IsNull
            && plate.IsVisible
            && ((!dresser.IsNull && dresser.IsVisible)
                || (!armoire.IsNull && armoire.IsVisible));
    }

    public unsafe PlateCaptureResult CaptureCurrent(string name, DateTime capturedAtUtc, Guid plateId)
    {
        var agent = AgentMiragePrismMiragePlate.Instance();
        return PlateCaptureExecution.Capture(name, capturedAtUtc, plateId, () => ObserveReady(agent), index =>
        {
            var item = agent->Data->CurrentItems[index];
            return new(item.ItemId, item.StainIds[0], item.StainIds[1], item.PendingStainIds[0], item.PendingStainIds[1],
                item.IsPendingStainFromInventoryItem[0], item.IsPendingStainFromInventoryItem[1],
                item.IsPendingStainFromItemId[0], item.IsPendingStainFromItemId[1]);
        }, diagnostics, () => DateTimeOffset.UtcNow);
    }

    public unsafe PlateApplyResult Apply(PlateLoadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var result = PlateApplyExecution.Apply(plan, new ApplyPort(this), diagnostics, () => DateTimeOffset.UtcNow);
        log.Information("Applied validated local plate plan: {Loaded} loaded, {Cleared} cleared, {Failures} failures.",
            result.Loaded, result.Cleared, result.Failures.Count);
        return result;
    }

    private sealed unsafe class ApplyPort(NativePlateEditor owner) : IPlateApplyPort
    {
        private readonly AgentMiragePrismMiragePlate* agent = AgentMiragePrismMiragePlate.Instance();
        private AgentMiragePrismMiragePlateData* initialData;
        private uint initialPlate;
        public bool ObserveReady() => owner.ObserveReady(agent);
        public uint SelectedSlot { get => agent->Data->SelectedItemIndex; set => agent->Data->SelectedItemIndex = value; }
        public bool HasChanges { get => agent->Data->HasChanges; set => agent->Data->HasChanges = value; }
        public PlateDyePlan PrepareDyes(PlateLoadPlan plan) => owner.PrepareDyes(plan, agent);
        public void CaptureContext()
        {
            initialData = agent->Data;
            initialPlate = initialData->SelectedMiragePlateIndex;
        }
        public bool ContextMatches(bool observe) => observe
            ? owner.ObserveContext(agent, initialData, initialPlate)
            : owner.IsReady(agent) && agent->Data == initialData && agent->Data->SelectedMiragePlateIndex == initialPlate;
        public uint ReadItemId(PlateSlot slot) => agent->Data->CurrentItems[(int)slot].ItemId;
        public void Clear(PlateSlot slot) => agent->ClearItemData((uint)slot);
        public void Load(PlateLoadStep step) => agent->SetSelectedItemData(
            step.Source!.Source == CollectionSource.Dresser ? ItemSource.PrismBox : ItemSource.Cabinet,
            step.Source.SourceId, step.Source.ItemId, step.Source.Stain0Id, step.Source.Stain1Id);
        public bool ApplyDyes(PlateLoadStep step, PlateDyePlan plan) => owner.ApplyDyes(agent, step, plan);
        public bool ObserveLoadedItem(PlateLoadStep step)
        {
            var index = (int)step.Slot;
            var diagnostics = owner.diagnostics;
            var log = owner.log;
            var current = agent->Data->CurrentItems[index];
            CollectionSource? actualSource = current.Source switch
            {
                ItemSource.PrismBox => CollectionSource.Dresser,
                ItemSource.Cabinet => CollectionSource.Armoire,
                _ => null,
            };
            diagnostics.Check(DiagnosticKey.ItemId, step.Source!.ItemId, current.ItemId, index);
            diagnostics.Check(DiagnosticKey.Source, (int)step.Source.Source, (int?)actualSource, index);
            diagnostics.Check(DiagnosticKey.SourceIndex, step.Source.SourceId, current.SourceId, index);
            diagnostics.Check(DiagnosticKey.Dye0, step.Source.Stain0Id, current.StainIds[0], index);
            diagnostics.Check(DiagnosticKey.Dye1, step.Source.Stain1Id, current.StainIds[1], index);
            diagnostics.Check(DiagnosticKey.PendingDye, 0, HasPendingDye(current) ? 1 : 0, index);
            diagnostics.Check(DiagnosticKey.PendingDye0, 0, current.PendingStainIds[0], index);
            diagnostics.Check(DiagnosticKey.PendingDye1, 0, current.PendingStainIds[1], index);
            diagnostics.Check(DiagnosticKey.InventoryDye0, 0, current.IsPendingStainFromInventoryItem[0] ? 1 : 0, index);
            diagnostics.Check(DiagnosticKey.InventoryDye1, 0, current.IsPendingStainFromInventoryItem[1] ? 1 : 0, index);
            diagnostics.Check(DiagnosticKey.ItemDye0, 0, current.IsPendingStainFromItemId[0] ? 1 : 0, index);
            diagnostics.Check(DiagnosticKey.ItemDye1, 0, current.IsPendingStainFromItemId[1] ? 1 : 0, index);
            if (!PlateResultPolicy.Matches(step.Source, current.ItemId, current.SourceId,
                    actualSource, current.StainIds[0], current.StainIds[1], HasPendingDye(current)))
            {
                log.Warning("Load mismatch at {Slot}: expected item {ExpectedItem}, source {ExpectedSource}/{ExpectedIndex}, dyes {ExpectedDye0}/{ExpectedDye1}; actual item {ActualItem}, source {ActualSource}/{ActualIndex}, dyes {ActualDye0}/{ActualDye1}, pending {Pending0}/{Pending1}, inventory flags {Inventory0}/{Inventory1}, item flags {Item0}/{Item1}.",
                    step.Slot, step.Source.ItemId, step.Source.Source, step.Source.SourceId,
                    step.Source.Stain0Id, step.Source.Stain1Id, current.ItemId, current.Source, current.SourceId,
                    current.StainIds[0], current.StainIds[1], current.PendingStainIds[0], current.PendingStainIds[1],
                    current.IsPendingStainFromInventoryItem[0], current.IsPendingStainFromInventoryItem[1],
                    current.IsPendingStainFromItemId[0], current.IsPendingStainFromItemId[1]);
                return false;
            }
            return true;
        }
    }

    internal unsafe OutfitPreviewAttempt PreviewOutfit(IReadOnlyList<SavedSlotView> slots, PlateDyeMode dyeMode = PlateDyeMode.Saved)
        => PlatePreviewExecution.Preview(slots, dyeMode, new PreviewPort(this), diagnostics, () => DateTimeOffset.UtcNow);

    private sealed unsafe class PreviewPort(NativePlateEditor owner) : IPlatePreviewPort
    {
        private readonly AgentMiragePrismMiragePlate* agent = AgentMiragePrismMiragePlate.Instance();
        private AgentMiragePrismMiragePlateData* initialData;
        private uint initialPlate;
        private MiragePlateItem[] originalItems = [];
        private bool originalChanges;
        public bool ObserveReady() => owner.ObserveReady(agent);
        public bool? Initialized => agent == null ? null : agent->CharaViewInitialized;
        public uint? State => agent == null ? null : agent->CharaView.State;
        public bool DyeWindowOpen => owner.IsDyeWindowOpen;
        public bool LoggedIn => owner.clientState.IsLoggedIn;
        public bool EditorOpen => owner.IsOpen;
        public bool MatchesSlot(uint itemId, PlateSlot slot) => owner.Get(itemId) is { } facts && PlateRules.MatchesSlot(facts.SlotMask, slot);
        public void Begin()
        {
            // Preview has a separate item buffer. Never write CurrentItems or inventory dye fields.
            initialData = agent->Data;
            initialPlate = initialData->SelectedMiragePlateIndex;
            originalItems = initialData->CurrentItems.ToArray();
            originalChanges = initialData->HasChanges;
            agent->CharaView.IsUpdatePending = false;
            agent->CharaView.UnequipGear();
        }
        public void SetItem(SavedSlotView slot, PlateDyeMode mode)
            => agent->CharaView.SetItemSlotData(OutfitPreviewPolicy.ToPreviewSlot(slot.Slot), slot.ItemId,
                OutfitPreviewPolicy.PreviewDye(slot.Stain0Id, mode), OutfitPreviewPolicy.PreviewDye(slot.Stain1Id, mode), 0, false);
        public void MarkActive() => owner.previewActive = true;
        public bool ContextMatches() => owner.ObserveContext(agent, initialData, initialPlate);
        public bool PlateUnchanged()
            => originalChanges == agent->Data->HasChanges
                && System.Runtime.InteropServices.MemoryMarshal.AsBytes(originalItems.AsSpan())
                    .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(agent->Data->CurrentItems));
        public void Release()
        {
            owner.log.Error("Native plate state changed during preview. Preview was stopped; inspect the plate before saving.");
            owner.ReleasePreview();
        }
    }

    internal unsafe void ReleasePreview()
    {
        if (!previewActive)
            return;
        previewActive = false;
        var agent = AgentMiragePrismMiragePlate.Instance();
        if (IsReady(agent) && agent->CharaViewInitialized)
            agent->CharaView.IsUpdatePending = true;
    }
    public ItemDisplayFacts? Get(uint itemId)
    {
        var itemSheet = dataManager.GetExcelSheet<Item>();
        var categorySheet = dataManager.GetExcelSheet<EquipSlotCategory>();
        return itemSheet.TryGetRow(itemId, out var item) &&
               !item.Name.IsEmpty &&
               item.EquipSlotCategory.RowId != 0 &&
               categorySheet.TryGetRow(item.EquipSlotCategory.RowId, out var category)
            ? new ItemDisplayFacts(
                item.Name.ToString(),
                item.Icon,
                EquipSlotCategoryAdapter.ToMask(category))
            { DyeCount = item.DyeCount }
            : null;
    }

    private unsafe bool ObserveContext(AgentMiragePrismMiragePlate* agent, AgentMiragePrismMiragePlateData* initialData, uint initialPlate)
    {
        var ready = ObserveReady(agent);
        var current = agent != null && agent == AgentMiragePrismMiragePlate.Instance();
        var sameData = diagnostics.Check(DiagnosticKey.SameData, 1, !current ? null : agent->Data == initialData ? 1 : 0);
        var samePlate = diagnostics.Check(DiagnosticKey.SamePlate, initialPlate,
            !current || agent->Data == null ? null : agent->Data->SelectedMiragePlateIndex);
        return ready && sameData && samePlate;
    }

    private unsafe bool ObserveReady(AgentMiragePrismMiragePlate* agent)
    {
        diagnostics.Check(DiagnosticKey.LoggedIn, 1, clientState.IsLoggedIn ? 1 : 0);
        diagnostics.Check(DiagnosticKey.EditorVisible, 1, IsOpen ? 1 : 0);
        diagnostics.Check(DiagnosticKey.AgentPresent, 1, agent != null ? 1 : 0);
        var current = agent != null && agent == AgentMiragePrismMiragePlate.Instance();
        diagnostics.Check(DiagnosticKey.AgentCurrent, 1, current ? 1 : 0);
        diagnostics.Check(DiagnosticKey.DataPresent, 1, !current ? null : agent->Data != null ? 1 : 0);
        diagnostics.Check(DiagnosticKey.AgentActive, 1, !current ? null : agent->IsAgentActive() ? 1 : 0);
        diagnostics.Check(DiagnosticKey.AddonReady, 1, !current ? null : agent->IsAddonReady() ? 1 : 0);
        diagnostics.Check(DiagnosticKey.PlateIndex, 1, !current || agent->Data == null ? null : agent->Data->SelectedMiragePlateIndex < 20 ? 1 : 0);
        return IsReady(agent);
    }

    private unsafe bool IsReady(AgentMiragePrismMiragePlate* agent)
        => clientState.IsLoggedIn
            && IsOpen
            && agent != null
            && agent == AgentMiragePrismMiragePlate.Instance()
            && agent->Data != null
            && agent->IsAgentActive()
            && agent->IsAddonReady()
            && agent->Data->SelectedMiragePlateIndex < 20;

    private static bool HasPendingDye(MiragePlateItem item)
        => new NativeCaptureItem(item.ItemId, item.StainIds[0], item.StainIds[1],
            item.PendingStainIds[0], item.PendingStainIds[1],
            item.IsPendingStainFromInventoryItem[0], item.IsPendingStainFromInventoryItem[1],
            item.IsPendingStainFromItemId[0], item.IsPendingStainFromItemId[1]).HasPendingDye;
}



