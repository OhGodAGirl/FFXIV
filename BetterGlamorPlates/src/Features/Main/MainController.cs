using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Features.Main;

/// <summary>
/// Owns library and native-operation policy while the window only renders
/// immutable view state and emits deliberate commands.
/// </summary>
internal sealed class MainController : IDisposable
{
    private readonly IPlateLibraryStore store;
    private readonly INativeCollectionValidator nativeCollectionValidator;
    private readonly ISharedCollectionSnapshotSource sharedCollectionSource;
    private readonly IPlateEditorMechanism editor;
    private readonly IItemFacts itemFacts;
    private readonly PlateLibraryState library;
    private readonly Dictionary<Guid, SavedPlate> drafts = [];
    private readonly HashSet<Guid> dirtyPlates = [];
    private readonly Dictionary<PlateSlot, uint> missingLoadItems = [];
    private PlateSlot selectedSlot = PlateSlot.Body;
    private PlateSlot selectedAccessory = PlateSlot.Ears;
    private Guid? deleteArmedId;
    private string plateSearch = string.Empty;
    private string filter = string.Empty;
    private const CollectionSourceFilter CollectionSources = CollectionSourceFilter.All;
    private long outfitRevision;
    private long persistenceRevision;
    private string? operationFeedback;
    private string? previewFeedback;
    private readonly Func<uint, bool> isGameIcon;
    private string status = "Select a saved plate or create a new one.";
    private bool sharedCollectionChecked;
    private CollectionDataFailure sharedCollectionFailure;
    private bool disposed;
    private PlateDyeMode previewDyeMode = PlateDyeMode.Saved;
    private readonly DiagnosticJournal diagnostics;
    private readonly IReadOnlyList<DyeOption> dyes;
    internal CollectionDataFailure ProviderFailure => sharedCollectionFailure;

    internal MainController(
        IPlateLibraryStore store,
        INativeCollectionValidator nativeCollectionValidator,
        ISharedCollectionSnapshotSource sharedCollectionSource,
        IPlateEditorMechanism editor,
        IItemFacts itemFacts,
        DiagnosticJournal? diagnostics = null,
        IReadOnlyList<DyeOption>? dyes = null, Func<uint, bool>? isGameIcon = null)
    {
        this.isGameIcon = isGameIcon ?? (_ => false);
        this.store = store;
        this.nativeCollectionValidator = nativeCollectionValidator;
        this.sharedCollectionSource = sharedCollectionSource;
        this.editor = editor;
        this.itemFacts = itemFacts;
        this.diagnostics = diagnostics ?? new DiagnosticJournal();
        this.dyes = dyes?.Take(255).ToArray() ?? [];
        library = store.Load();
        var fallbackSelectedId = library.SavedPlates.FirstOrDefault()?.Id;
        if (library.SavedPlates.Any(plate => plate.LibraryOrder is null))
        {
            var ordered = library.SavedPlates.OrderByDescending(plate => plate.UpdatedAtUtc)
                .ThenBy(plate => plate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(plate => plate.Id).ToArray();
            for (var index = 0; index < ordered.Length; index++) ordered[index].LibraryOrder = index;
        }
        library.SavedPlates.Sort((left, right) => Nullable.Compare(left.LibraryOrder, right.LibraryOrder));

        foreach (var plate in library.SavedPlates)
        {
            PlateLibrary.EnsureShape(plate);
            if (plate.IconId != 0 && !this.isGameIcon(plate.IconId)) plate.IconId = 0;
        }

        if (library.SelectedPlateId is not { } selectedId
            || library.SavedPlates.All(plate => plate.Id != selectedId))
        {
            library.SelectedPlateId = fallbackSelectedId;
        }

        View = BuildView();
    }

    internal MainViewState View { get; private set; }

    internal void Execute(MainCommand command, DateTime nowUtc, Guid newPlateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (disposed)
            return;

        if (command is MainCommand.CreatePlate or MainCommand.CaptureCurrentPlate
            or MainCommand.DuplicatePlate or MainCommand.DuplicateActive
            or MainCommand.DeletePlate or MainCommand.ConfirmDelete or MainCommand.ArmOrDelete
            or MainCommand.RenamePlate or MainCommand.RenameActive or MainCommand.AssignIcon
            or MainCommand.SaveOutfit or MainCommand.SelectPlate)
            MutateLibrary(() => ExecuteCore(command, nowUtc, newPlateId));
        else
            ExecuteCore(command, nowUtc, newPlateId);
    }

    private void ExecuteCore(MainCommand command, DateTime nowUtc, Guid newPlateId)
    {

        if (command is MainCommand.AssignIcon or MainCommand.SetDye or MainCommand.RestoreSlotDyes
            or MainCommand.CaptureCurrentPlate or MainCommand.CreatePlate or MainCommand.ClearSlot)
            operationFeedback = null;

        switch (command)
        {
            case MainCommand.DeletePlate deletePlate:
                deleteArmedId = deletePlate.PlateId;
                ArmOrDelete(deletePlate.PlateId);
                break;
            case MainCommand.SaveOutfit save:
                if (dirtyPlates.Contains(save.PlateId) && drafts.TryGetValue(save.PlateId, out var draft))
                {
                    var index = library.SavedPlates.FindIndex(plate => plate.Id == save.PlateId);
                    if (index < 0) break;
                    var previous = library.SavedPlates[index];
                    library.SavedPlates[index] = PlateLibrary.Duplicate(draft, draft.UpdatedAtUtc, draft.Id, draft.Name);
                    try { Persist(); }
                    catch { library.SavedPlates[index] = previous; throw; }
                    drafts.Remove(save.PlateId);
                    dirtyPlates.Remove(save.PlateId);
                }
                break;
            case MainCommand.CloseEditor:
                ResetUnsavedChanges();
                break;
            case MainCommand.DiscardOutfit discard:
                drafts.Remove(discard.PlateId);
                dirtyPlates.Remove(discard.PlateId);
                outfitRevision++;
                break;
            case MainCommand.BeginDelete begin:
                deleteArmedId = library.SavedPlates.Any(plate => plate.Id == begin.PlateId) ? begin.PlateId : null;
                break;
            case MainCommand.ConfirmDelete confirm:
                if (deleteArmedId == confirm.PlateId) ArmOrDelete(confirm.PlateId);
                break;
            case MainCommand.RefreshCollection:
                RefreshCollection();
                break;
            case MainCommand.CreatePlate:
                AddPlate(PlateLibrary.Create(
                    $"Plate {library.SavedPlates.Count + 1}",
                    nowUtc,
                    newPlateId));
                break;
            case MainCommand.CaptureCurrentPlate:
                CaptureCurrent(nowUtc, newPlateId);
                break;
            case MainCommand.SelectPlate select:
                SelectPlate(select.PlateId);
                break;
            case MainCommand.SetPlateSearch search:
                plateSearch = search.Search;
                deleteArmedId = null;
                break;
            case MainCommand.RenamePlate renamePlate:
                RenamePlate(renamePlate.PlateId, renamePlate.Name, nowUtc);
                break;
            case MainCommand.DuplicatePlate duplicate:
                DuplicatePlate(duplicate.PlateId, nowUtc, newPlateId);
                break;
            case MainCommand.AssignIcon icon:
                if (library.SavedPlates.FirstOrDefault(plate => plate.Id == icon.PlateId) is { } target
                    && (icon.IconId == 0 || isGameIcon(icon.IconId)))
                {
                    target.IconId = icon.IconId;
                    if (drafts.TryGetValue(target.Id, out var iconDraft)) iconDraft.IconId = icon.IconId;
                    Touch(target, nowUtc);
                }
                else operationFeedback = "This game icon is unavailable. Choose another icon.";
                break;
            case MainCommand.RenameActive rename:
                RenameActive(rename.Name, nowUtc);
                break;
            case MainCommand.DuplicateActive:
                DuplicateActive(nowUtc, newPlateId);
                break;
            case MainCommand.ArmOrDelete delete:
                ArmOrDelete(delete.PlateId);
                break;
            case MainCommand.DisarmDelete:
                deleteArmedId = null;
                break;
            case MainCommand.PreviewActive preview:
                if (Enum.IsDefined(preview.DyeMode)) previewDyeMode = preview.DyeMode;
                break;
            case MainCommand.UseStoredDyes:
                UseStoredDyes(nowUtc);
                break;
            case MainCommand.SetDye dye:
                SetDye(dye, nowUtc);
                break;
            case MainCommand.ClearOutfitItems clearItems:
                var itemOutfit = GetActivePlate();
                if (itemOutfit?.Id != clearItems.PlateId || outfitRevision != clearItems.Revision) break;
                Array.Clear(itemOutfit.Items);
                Touch(itemOutfit, nowUtc);
                LoadActive(nowUtc, new MainCommand.LoadActive(previewDyeMode, itemOutfit.Id, outfitRevision));
                break;
            case MainCommand.ClearOutfitDyes clearAll:
                var outfit = GetActivePlate();
                if (outfit?.Id != clearAll.PlateId || outfitRevision != clearAll.Revision) break;
                foreach (var item in outfit.Items)
                    if (item is not null) { item.Stain0Id = 0; item.Stain1Id = 0; }
                previewDyeMode = PlateDyeMode.Saved;
                Touch(outfit, nowUtc);
                break;
            case MainCommand.ClearItemDyes clearDyes:
                SetDye(new(clearDyes.PlateId, clearDyes.Slot, clearDyes.ExpectedItemId, 0, 0), nowUtc);
                if (itemFacts.Get(clearDyes.ExpectedItemId)?.DyeCount == 2)
                    SetDye(new(clearDyes.PlateId, clearDyes.Slot, clearDyes.ExpectedItemId, 1, 0), nowUtc);
                break;
            case MainCommand.RestoreSlotDyes restore:
                RestoreSlotDyes(restore, nowUtc);
                break;
            case MainCommand.LoadActive load:
                LoadActive(nowUtc, load);
                break;
            case MainCommand.SelectSlot selectSlot:
                if ((int)selectSlot.Slot is < 0 or >= PlateRules.SlotCount)
                    break;
                selectedSlot = selectSlot.Slot;
                if (EquipmentSummaryPolicy.IsAccessory(selectedSlot))
                    selectedAccessory = selectedSlot;
                deleteArmedId = null;
                break;
            case MainCommand.ClearSlot clear:
                ClearSlot(clear, nowUtc);
                break;
            case MainCommand.SetFilter setFilter:
                filter = setFilter.Filter;
                break;
            case MainCommand.ToggleCollectionSource:
                // Obsolete source preferences cannot hide either collection.
                break;

            case MainCommand.AddItem add:
                AddItem(add.Slot, add.Item, nowUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        if (command is MainCommand.LoadActive or MainCommand.ClearOutfitItems) operationFeedback = status;
        View = BuildView();
    }

    internal void ReportPreviewResult(bool accepted)
    {
        status = accepted
            ? previewDyeMode == PlateDyeMode.Saved
                ? "Previewing the whole outfit with saved dyes. The native plate is unchanged."
                : "Previewing the whole outfit without dyes. Saved colours and the native plate are unchanged."
            : "The native outfit preview is not ready. The native plate is unchanged.";
        previewFeedback = accepted ? null : status;
        View = BuildView();
    }

    internal void ReportLoadCancelled()
    {
        if (disposed) return;
        operationFeedback = "Load cancelled before native changes.";
        View = BuildView();
    }

    public void Dispose()
        => disposed = true;

    internal void ReportOperationFailure(MainCommand command, DateTimeOffset observedAtUtc = default)
    {
        if (disposed)
            return;
        status = command is MainCommand.LoadActive or MainCommand.ClearOutfitItems
            ? "Loading stopped unexpectedly. The native editor may be partially changed. Review it before saving; no automatic retry was made."
            : "The local operation failed. Changes may not have been saved. Check diagnostics before trying again.";
        diagnostics.Finish(DiagnosticStop.Exception, observedAtUtc);
        operationFeedback = status;
        View = View with { Status = status, OperationFeedback = operationFeedback };
    }

    internal void UpdateSharedCollectionData(CollectionDataParseResult refresh)
    {
        if (disposed)
            return;

        sharedCollectionChecked = true;
        sharedCollectionFailure = refresh.Failure;
        View = BuildView();
    }

    internal void ClearSharedCollectionData()
    {
        if (disposed)
            return;

        sharedCollectionChecked = false;
        sharedCollectionFailure = CollectionDataFailure.None;
        View = BuildView();
    }

    private void RefreshCollection()
    {
        var snapshot = sharedCollectionSource.Latest;
        var knownOwnedItems = CollectionViewPlanner.CountKnownOwnedItems(snapshot);
        status = CollectionProviderPolicy.DescribeCollectionView(
            sharedCollectionChecked,
            sharedCollectionFailure,
            snapshot,
            knownOwnedItems);
    }

    private void CaptureCurrent(DateTime capturedAtUtc, Guid plateId)
    {
        var result = editor.CaptureCurrent(
            $"Plate {library.SavedPlates.Count + 1}",
            capturedAtUtc,
            plateId);
        if (result.Plate is null)
        {
            operationFeedback = status = result.Outcome == PlateCaptureOutcome.PendingDyes
                ? "Clear pending dye changes in FFXIV before using Save Current. No local plate was added."
                : "Open the Glamour Plates editor and select a plate first.";
            return;
        }

        AddPlate(result.Plate);
        status = $"Saved {result.Plate.Items.Count(item => item is not null)} slots from the selected native plate.";
    }

    internal void ResetUnsavedChanges()
    {
        if (dirtyPlates.Count == 0) return;
        drafts.Clear();
        dirtyPlates.Clear();
        missingLoadItems.Clear();
        operationFeedback = null;
        previewFeedback = null;
        outfitRevision++;
        View = BuildView();
    }

    private void SelectPlate(Guid plateId)
    {
        if (library.SavedPlates.All(plate => plate.Id != plateId))
            return;

        if (library.SelectedPlateId != plateId) ResetUnsavedChanges();
        outfitRevision++;
        library.SelectedPlateId = plateId;
        missingLoadItems.Clear();
        operationFeedback = null;
        previewFeedback = null;
        deleteArmedId = null;
        Persist();
    }

    private void RenameActive(string name, DateTime nowUtc)
        => RenamePlate(library.SelectedPlateId, name, nowUtc);

    private void RenamePlate(Guid? plateId, string name, DateTime nowUtc)
    {
        var active = library.SavedPlates.FirstOrDefault(plate => plate.Id == plateId);
        if (active is null)
            return;

        active.Name = PlateLibrary.NormalizeName(name);
        if (drafts.TryGetValue(active.Id, out var namedDraft)) namedDraft.Name = active.Name;
        Touch(active, nowUtc);
        status = $"Renamed the local plate to {active.Name}.";
    }

    private void DuplicateActive(DateTime nowUtc, Guid plateId)
        => DuplicatePlate(library.SelectedPlateId, nowUtc, plateId);

    private void DuplicatePlate(Guid? sourceId, DateTime nowUtc, Guid plateId)
    {
        var active = library.SavedPlates.FirstOrDefault(plate => plate.Id == sourceId);
        if (active is null)
            return;

        AddPlate(PlateLibrary.Duplicate(drafts.GetValueOrDefault(active.Id) ?? active, nowUtc, plateId),
            library.SavedPlates.IndexOf(active));
    }

    private void ArmOrDelete(Guid plateId)
    {
        var active = library.SavedPlates.FirstOrDefault(plate => plate.Id == plateId);
        if (active is null)
            return;

        if (deleteArmedId != active.Id)
        {
            deleteArmedId = active.Id;
            status = $"Select Confirm Delete to remove {active.Name} from the local library.";
            return;
        }

        library.SavedPlates.Remove(active);
        NormalizeLibraryOrder();
        drafts.Remove(active.Id);
        dirtyPlates.Remove(active.Id);
        outfitRevision++;
        if (library.SelectedPlateId == active.Id)
            library.SelectedPlateId = library.SavedPlates.FirstOrDefault()?.Id;
        deleteArmedId = null;
        Persist();
        status = "Deleted the local saved plate. The native FFXIV plates were unchanged.";
    }

    private void LoadActive(DateTime capturedAtUtc, MainCommand.LoadActive request)
    {
        missingLoadItems.Clear();
        var dyeMode = request.DyeMode ?? previewDyeMode;
        if (!Enum.IsDefined(dyeMode) || (request.PlateId is { } plateId && plateId != library.SelectedPlateId)
            || (request.Revision is { } revision && revision != outfitRevision))
        {
            status = operationFeedback = "The local outfit changed before loading. Review it and load again. No slots were changed.";
            return;
        }
        diagnostics.Begin(DiagnosticPhase.LoadValidation, capturedAtUtc);
        var active = GetActivePlate();
        diagnostics.Check(DiagnosticKey.ActivePlate, 1, active is null ? 0 : 1);
        if (active is null)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, capturedAtUtc);
            return;
        }

        diagnostics.Check(DiagnosticKey.EditorVisible, 1, editor.IsOpen ? 1 : 0);
        if (!editor.IsOpen)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, capturedAtUtc);
            status = "Open the FFXIV Glamour Plates editor first. No slots were changed.";
            return;
        }

        // Copy before crossing an adapter boundary: the accepted request has one appearance and mode.
        active = PlateLibrary.Duplicate(active, active.UpdatedAtUtc, active.Id, active.Name);
        var nativeSnapshot = nativeCollectionValidator.Capture(capturedAtUtc);
        diagnostics.Check(DiagnosticKey.DresserLoaded, 1, nativeSnapshot.IsDresserLoaded ? 1 : 0);
        diagnostics.Check(DiagnosticKey.ArmoireLoaded, 1, nativeSnapshot.IsArmoireLoaded ? 1 : 0);
        var plan = PlateLoadPlanner.Create(
            active,
            nativeSnapshot,
            (itemId, slot) => itemFacts.Get(itemId) is { } facts
                && PlateRules.MatchesSlot(facts.SlotMask, slot), dyeMode,
            allowMissingItems: true, validationTimeUtc: capturedAtUtc);
        foreach (var issue in plan.Issues)
            if (issue.Kind == PlateLoadIssueKind.ExactSourceUnavailable && issue.Slot is { } missingSlot)
            {
                missingLoadItems[missingSlot] = issue.ItemId;
                diagnostics.Check(DiagnosticKey.ExactSource, 1, 0, (int)missingSlot);
            }
        if (!plan.CanApply)
        {
            foreach (var issue in plan.Issues)
            {
                if (issue.Kind == PlateLoadIssueKind.CollectionNotReady) continue;
                // Every remaining planner issue identifies the affected slot.
                diagnostics.Check(issue.Kind == PlateLoadIssueKind.InvalidSlot ? DiagnosticKey.SlotCompatible : DiagnosticKey.ExactSource, 1, 0, (int)issue.Slot!.Value);
                if (issue.Kind == PlateLoadIssueKind.ExactSourceUnavailable && issue.Slot is { } slot && active.Items[(int)slot] is { } saved)
                {
                    // ResolveClosest already proved no source with this item ID exists.
                    diagnostics.Check(DiagnosticKey.ItemId, saved.ItemId, null, (int)slot);
                    diagnostics.Check(DiagnosticKey.Dye0, saved.Stain0Id, null, (int)slot);
                    diagnostics.Check(DiagnosticKey.Dye1, saved.Stain1Id, null, (int)slot);
                }
            }
            diagnostics.Finish(DiagnosticStop.Rejected, capturedAtUtc);
            status = string.Join(" ", plan.Issues.Select(DescribeIssue));
            if (plan.Issues.Any(issue =>
                    issue.Kind is PlateLoadIssueKind.CollectionNotReady or PlateLoadIssueKind.ExactSourceUnavailable))
            {
                var validationHint = CollectionProviderPolicy.DescribeActionValidationHint(sharedCollectionSource.Latest, active);
                if (validationHint is not null)
                {
                    status = $"{status} {validationHint}";
                }
            }

            return;
        }

        var result = editor.Apply(plan);
        status = result.IsComplete
            ? missingLoadItems.Count > 0
                ? "Could not load the entire outfit - some pieces were missing."
                : $"Loaded {result.Loaded} items and cleared {result.Cleared} slots."
            : $"Loaded {result.Loaded} items. {DescribeFailure(result.Failures[0])}";
        operationFeedback = status;
    }

    private void ClearSlot(MainCommand.ClearSlot command, DateTime nowUtc)
    {
        var slot = command.Slot;
        var active = GetActivePlate();
        if (active is null || (int)slot is < 0 or >= PlateRules.SlotCount
            || (command.PlateId is { } plateId && plateId != active.Id)
            || active.Items[(int)slot] is not { } item
            || (command.ExpectedItemId is { } expected && expected != item.ItemId))
            return;

        active.Items[(int)slot] = null;
        Touch(active, nowUtc);
        status = $"Cleared {PlateRules.GetSlotName(slot)} in the local plate.";
    }

    private void AddItem(PlateSlot slot, CollectionItem item, DateTime nowUtc)
    {
        var active = GetActivePlate();
        if (active is null || !PlateRules.MatchesSlot(item.SlotMask, slot))
            return;

        var stored = editor.IsOpen
            ? nativeCollectionValidator.Capture(nowUtc).Items.FirstOrDefault(value => value.ItemId == item.ItemId && value.Source == item.Source)
            : null;
        active.Items[(int)slot] = new SavedPlateItem
        {
            ItemId = item.ItemId,
            Stain0Id = stored?.Stain0Id ?? 0,
            Stain1Id = stored?.Stain1Id ?? 0,
        };
        Touch(active, nowUtc);
        status = $"Added {item.Name} to {PlateRules.GetSlotName(slot)}.";
    }

    private void UseStoredDyes(DateTime nowUtc)
    {
        var active = GetActivePlate();
        if (active is null || !editor.IsOpen)
            return;
        var snapshot = nativeCollectionValidator.Capture(nowUtc);
        var replacements = new List<(int Index, NativeCollectionItem Source)>();
        for (var index = 0; index < active.Items.Length; index++)
        {
            var item = active.Items[index];
            if (item is null) continue;
            var source = CollectionSourceResolver.ResolveExact(snapshot.Items, item)
                ?? snapshot.Items.FirstOrDefault(value => value.ItemId == item.ItemId);
            if (source is null)
            {
                diagnostics.Check(DiagnosticKey.ExactSource, 1, 0, index);
                diagnostics.Finish(DiagnosticStop.Rejected, nowUtc);
                status = "Stored dyes are unavailable for one or more items. The local outfit was not changed.";
                return;
            }
            replacements.Add((index, source));
        }
        foreach (var (index, source) in replacements)
        {
            active.Items[index]!.Stain0Id = source.Stain0Id;
            active.Items[index]!.Stain1Id = source.Stain1Id;
        }
        Touch(active, nowUtc);
        status = "Updated the local outfit to its stored dyes. No dye was consumed.";
    }

    private SavedPlateItem? DyeTarget(Guid plateId, PlateSlot slot, uint expectedItemId)
    {
        var active = GetActivePlate();
        if (active?.Id != plateId || (int)slot is < 0 or >= PlateRules.SlotCount) return null;
        var item = active.Items[(int)slot];
        return item is not null && item.ItemId == expectedItemId && expectedItemId != 0 ? item : null;
    }

    private void SetDye(MainCommand.SetDye command, DateTime nowUtc)
    {
        var item = DyeTarget(command.PlateId, command.Slot, command.ExpectedItemId);
        var facts = item is null ? null : itemFacts.Get(item.ItemId);
        var targetMatches = diagnostics.Check(DiagnosticKey.LocalDyeTarget, 1, item is null ? 0 : 1);
        var channelSupported = diagnostics.Check(DiagnosticKey.DyeChannelSupported, 1,
            facts is null ? null : facts.DyeCount <= 2 && command.Channel < facts.DyeCount ? 1 : 0);
        var dyeAvailable = diagnostics.Check(DiagnosticKey.DyeAvailable, 1,
            command.DyeId == 0 || dyes.Any(dye => dye.Id == command.DyeId) ? 1 : 0);
        if (!targetMatches || !channelSupported || !dyeAvailable || item is null)
        {
            diagnostics.Finish(DiagnosticStop.Rejected, nowUtc);
            operationFeedback = status = "This dye selection is no longer available. Select the item again.";
            return;
        }
        if (command.Channel == 0) item.Stain0Id = command.DyeId;
        else item.Stain1Id = command.DyeId;
        previewDyeMode = PlateDyeMode.Saved;
        Touch(GetActivePlate()!, nowUtc);
        diagnostics.Confirm();
        status = "Updated the draft dye choice. Use Save to keep it. Previewing uses no dye.";
    }

    private void RestoreSlotDyes(MainCommand.RestoreSlotDyes command, DateTime nowUtc)
    {
        var item = DyeTarget(command.PlateId, command.Slot, command.ExpectedItemId);
        if (item is null || !editor.IsOpen)
        {
            operationFeedback = status = "Select the item with the Glamour Plate editor open to restore its stored dyes.";
            return;
        }
        var snapshot = nativeCollectionValidator.Capture(nowUtc);
        var source = CollectionSourceResolver.ResolveExact(snapshot.Items, item)
            ?? snapshot.Items.FirstOrDefault(value => value.ItemId == item.ItemId);
        if (source is null)
        {
            diagnostics.Check(DiagnosticKey.ExactSource, 1, 0, (int)command.Slot);
            diagnostics.Finish(DiagnosticStop.Rejected, nowUtc);
            operationFeedback = status = "Stored dyes for this item are unavailable. The local outfit was not changed.";
            return;
        }
        item.Stain0Id = source.Stain0Id;
        item.Stain1Id = source.Stain1Id;
        previewDyeMode = PlateDyeMode.Saved;
        Touch(GetActivePlate()!, nowUtc);
        status = "Restored this item's stored dyes in the local outfit. No dye was consumed.";
    }
    internal void ImportExaminedOutfit(SavedPlate plate)
    {
        if (disposed) return;
        MutateLibrary(() =>
        {
            AddPlate(plate);
            View = BuildView();
        });
    }

    private void MutateLibrary(Action mutation)
    {
        // Persistence can fail after mutation. Restore both saved and draft state so
        // a later successful save cannot silently commit a previously failed action.
        static SavedPlate Copy(SavedPlate plate)
            => PlateLibrary.Duplicate(plate, plate.UpdatedAtUtc, plate.Id, plate.Name);
        var saved = library.SavedPlates.Select(Copy).ToArray();
        var savedDrafts = drafts.ToDictionary(pair => pair.Key, pair => Copy(pair.Value));
        var dirty = dirtyPlates.ToArray();
        var missing = missingLoadItems.ToArray();
        var selection = library.SelectedPlateId;
        var revision = outfitRevision;
        var search = plateSearch;
        var armed = deleteArmedId;
        var previousStatus = status;
        var previousFeedback = operationFeedback;
        var previousPreviewFeedback = previewFeedback;
        var previousView = View;
        var previousPersistenceRevision = persistenceRevision;
        try { mutation(); }
        catch when (persistenceRevision == previousPersistenceRevision)
        {
            library.SavedPlates.Clear();
            library.SavedPlates.AddRange(saved);
            library.SelectedPlateId = selection;
            drafts.Clear();
            foreach (var pair in savedDrafts) drafts.Add(pair.Key, pair.Value);
            dirtyPlates.Clear();
            dirtyPlates.UnionWith(dirty);
            missingLoadItems.Clear();
            foreach (var pair in missing) missingLoadItems.Add(pair.Key, pair.Value);
            outfitRevision = revision;
            plateSearch = search;
            deleteArmedId = armed;
            status = previousStatus;
            operationFeedback = previousFeedback;
            previewFeedback = previousPreviewFeedback;
            View = previousView;
            throw;
        }
    }

    private void AddPlate(SavedPlate plate, int position = 0)
    {
        PlateLibrary.EnsureShape(plate);
        outfitRevision++;
        ResetUnsavedChanges();
        library.SavedPlates.Insert(Math.Clamp(position, 0, library.SavedPlates.Count), plate);
        NormalizeLibraryOrder();
        library.SelectedPlateId = plate.Id;
        plateSearch = string.Empty;
        deleteArmedId = null;
        Persist();
        status = $"Created local plate {plate.Name}.";
    }

    private void Touch(SavedPlate plate, DateTime nowUtc)
    {
        missingLoadItems.Clear();
        outfitRevision++;
        plate.UpdatedAtUtc = nowUtc;
        if (drafts.TryGetValue(plate.Id, out var draft) && ReferenceEquals(draft, plate))
            dirtyPlates.Add(plate.Id);
        else
        {
            if (draft is not null) draft.UpdatedAtUtc = nowUtc;
            Persist();
        }
    }

    private void Persist()
    {
        store.Save(library);
        persistenceRevision++;
    }

    private void NormalizeLibraryOrder()
    {
        for (var index = 0; index < library.SavedPlates.Count; index++)
        {
            var plate = library.SavedPlates[index];
            plate.LibraryOrder = index;
            if (drafts.TryGetValue(plate.Id, out var draft)) draft.LibraryOrder = index;
        }
    }

    private SavedPlate? GetActivePlate()
    {
        var saved = library.SavedPlates.FirstOrDefault(plate => plate.Id == library.SelectedPlateId);
        if (saved is null) return null;
        if (!drafts.TryGetValue(saved.Id, out var draft))
            drafts[saved.Id] = draft = PlateLibrary.Duplicate(saved, saved.UpdatedAtUtc, saved.Id, saved.Name);
        return draft;
    }

    private MainViewState BuildView()
    {
        var active = GetActivePlate();
        var sharedCollectionSnapshot = sharedCollectionSource.Latest;
        var sharedPlateCompleteness = CollectionProviderPolicy.CreatePlateCompleteness(
            active,
            sharedCollectionSnapshot);
        var slots = Enum.GetValues<PlateSlot>()
            .Select(slot =>
            {
                var item = active?.Items[(int)slot];
                var facts = item is null ? null : itemFacts.Get(item.ItemId);
                var ownership = CollectionProviderPolicy.ClassifyOwnership(
                    item,
                    sharedCollectionSnapshot);
                return new SavedSlotView(
                    slot,
                    PlateRules.GetSlotName(slot),
                    item is null
                        ? "Empty"
                        : facts?.Name ?? $"item {item.ItemId}",
                    item is not null,
                    facts?.IconId ?? 0,
                    ownership,
                    CollectionProviderPolicy.DescribeSlotOwnership(ownership))
                {
                    Stain0Id = item?.Stain0Id ?? 0,
                    Stain1Id = item?.Stain1Id ?? 0,
                    ItemId = item?.ItemId ?? 0,
                    DyeCount = item is not null && facts?.DyeCount <= 2 ? facts.DyeCount : (byte)0,
                    DyeFactsAvailable = item is not null && facts?.DyeCount <= 2,
                    MissingFromLastLoad = item is not null && missingLoadItems.GetValueOrDefault(slot) == item.ItemId,
                };
            })
            .ToArray();
        var orderedPlates = library.SavedPlates.OrderBy(plate => plate.LibraryOrder);
        var summaries = SavedPlateQuery.Apply(orderedPlates, plateSearch)
            .Select(CreateSummary)
            .ToArray();
        var activeSummary = active is null ? null : CreateSummary(active);
        var sharedCollectionStatus = CollectionProviderPolicy.DescribeSummary(
            sharedCollectionChecked,
            sharedCollectionFailure,
            sharedCollectionSnapshot);
        var sharedCollectionDiagnostics = CollectionProviderPolicy.CreateDiagnostics(
            sharedCollectionChecked,
            sharedCollectionFailure,
            sharedCollectionSnapshot);
        var knownOwnedItems = CollectionViewPlanner.CountKnownOwnedItems(sharedCollectionSnapshot);

        return new MainViewState(
            summaries,
            library.SavedPlates.Count,
            activeSummary,
            library.SelectedPlateId,
            slots,
            CollectionViewPlanner.Create(
                sharedCollectionSnapshot,
                selectedSlot,
                filter,
                CollectionSources,
                itemFacts.Get),
            knownOwnedItems,
            selectedSlot,
            plateSearch,
            filter,
            CollectionSources,
            deleteArmedId,
            active is not null && editor.IsOpen,
            active is null
                ? "Select or create a saved plate first."
                : editor.IsOpen
                    ? null
                    : "Open the FFXIV Glamour Plates editor first.",
            CollectionProviderPolicy.DescribePrimaryStatus(
                sharedCollectionStatus,
                knownOwnedItems),
            status,
            sharedCollectionStatus,
            sharedCollectionDiagnostics,
            sharedPlateCompleteness)
        {
            OutfitRevision = outfitRevision,
            HasUnsavedChanges = active is not null && dirtyPlates.Contains(active.Id),
            OperationFeedback = operationFeedback ?? previewFeedback,
            SelectedAccessory = selectedAccessory,
            PreviewDyeMode = previewDyeMode,
            HasCollectionObservation = sharedCollectionSnapshot is not null,
            CollectionNeedsAttention = sharedCollectionSnapshot is null
                || sharedCollectionFailure != CollectionDataFailure.None
                || sharedCollectionSnapshot.GlamourDresser.Status != CollectionSourceStatus.Fresh
                || sharedCollectionSnapshot.Armoire.Status != CollectionSourceStatus.Fresh,
            EmptyCollectionMessage = sharedCollectionSnapshot is null
                    ? "Ownership is unavailable. Your local plates are still available."
                    : "No known items match this slot and search.",
        };
    }

    private SavedPlateSummary CreateSummary(SavedPlate plate)
        => new(
            plate.Id,
            plate.Name,
            plate.UpdatedAtUtc,
            plate.Items.Count(item => item is not null))
        {
            HasUnsavedChanges = dirtyPlates.Contains(plate.Id),
            IconId = plate.IconId != 0 ? plate.IconId
                : plate.Items.Where(item => item is not null).Select(item => itemFacts.Get(item!.ItemId)?.IconId ?? 0).FirstOrDefault(icon => icon != 0),
        };

    private string DescribeIssue(PlateLoadIssue issue) => issue.Kind switch
    {
        PlateLoadIssueKind.CollectionNotReady
            => "Wait for both the Glamour Dresser and Armoire collections to finish loading.",
        PlateLoadIssueKind.InvalidSlot
            => $"{PlateRules.GetSlotName(issue.Slot!.Value)}: {GetItemName(issue.ItemId)} is not valid for this slot.",
        PlateLoadIssueKind.ExactSourceUnavailable
            => $"{PlateRules.GetSlotName(issue.Slot!.Value)}: {GetItemName(issue.ItemId)} is not available in the Dresser or Armoire.",
        _ => "The local plate could not be validated.",
    };

    private string DescribeFailure(PlateApplyFailure failure) => failure.Kind switch
    {
        PlateApplyFailureKind.EditorNotReady => "The native Glamour Plates editor is not ready.",
        PlateApplyFailureKind.EditorClosed => "The native Glamour Plates editor stopped being ready.",
        PlateApplyFailureKind.PendingDyes => "Save or discard the current native dye changes before loading another outfit. No slots were changed.",
        PlateApplyFailureKind.DyeUnavailable
            => $"{PlateRules.GetSlotName(failure.Slot!.Value)}: not enough {(failure.DyeId == 0 ? "Terebinth" : $"dye for {dyes.FirstOrDefault(dye => dye.Id == failure.DyeId)?.Name ?? "the saved colour"}")} in your inventory. No slots were changed.",
        PlateApplyFailureKind.UnsupportedDye
            => $"{PlateRules.GetSlotName(failure.Slot!.Value)}: this item does not support a saved dye channel. No slots were changed.",
        PlateApplyFailureKind.DyeRejected
            => $"{PlateRules.GetSlotName(failure.Slot!.Value)}: the game did not accept the dye change. Review the native plate before saving.",
        PlateApplyFailureKind.ClearRejected
            => $"{PlateRules.GetSlotName(failure.Slot!.Value)}: the game did not clear this slot.",
        PlateApplyFailureKind.ItemRejected
            => $"{PlateRules.GetSlotName(failure.Slot!.Value)}: the game did not accept {GetItemName(failure.ItemId)}.",
        PlateApplyFailureKind.EditorClosedAfterPartial
            => "The editor closed before completion. Reopen it and review the native plate before trying again.",
        _ => "The native plate operation stopped safely.",
    };

    private string GetItemName(uint itemId)
        => itemFacts.Get(itemId)?.Name ?? $"item {itemId}";
}



