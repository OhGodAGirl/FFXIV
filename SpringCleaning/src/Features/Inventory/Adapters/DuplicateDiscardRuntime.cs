using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning;

internal sealed class DuplicateDiscardRuntime : IDuplicateDiscardRuntime
{
    private readonly DresserScanner scanner;
    private readonly NativeUi nativeUi;
    private readonly ICondition condition;
    private readonly Configuration configuration;
    private readonly CollectionDataRefreshController collectionData;

    public DuplicateDiscardRuntime(
                DresserScanner scanner,
                NativeUi nativeUi,
                ICondition condition,
                Configuration configuration,
                CollectionDataRefreshController collectionData)
    {
        this.scanner = scanner;
        this.nativeUi = nativeUi;
        this.condition = condition;
        this.configuration = configuration;
        this.collectionData = collectionData;
    }

    public bool IsDresserOpen => nativeUi.IsDresserOpen;

    public bool IsDiscardConfirmationOpen => nativeUi.IsExistingOutfitMergePromptOpen;

    public bool IsOccupied => collectionData.LatestSnapshot is null || condition.Any(
        ConditionFlag.Occupied,
        ConditionFlag.Occupied30,
        ConditionFlag.OccupiedInEvent,
        ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.Occupied33,
        ConditionFlag.OccupiedInCutSceneEvent,
        ConditionFlag.Occupied38,
        ConditionFlag.Occupied39);

    public bool? IsInventoryItemPresent(InventoryDuplicateItem candidate) =>
        CollectionOwnershipPolicy.HasCandidateScope(collectionData.LatestSnapshot, candidate)
            ? nativeUi.IsInventoryItemPresent(candidate)
            : null;

    public void CloseDresser() => nativeUi.CloseDresser();

    public bool CanSafelyOpenDiscardPrompt(InventoryDuplicateItem candidate)
    {
        var notIgnored = nativeUi.Diagnostics.Compare("DuplicateNotIgnored", 1,
            !configuration.IgnoredDuplicateItemIds.Contains(candidate.ItemId) ? 1 : 0);
        var ownership = nativeUi.Diagnostics.Compare("DuplicateRetainedOwnership", 1,
            CollectionOwnershipPolicy.CanContinueDuplicateRemoval(collectionData.LatestSnapshot, candidate) ? 1 : 0);
        var copySafe = nativeUi.Diagnostics.Compare("DuplicateCopySafety", 1,
            notIgnored && ownership ? (scanner.CanSafelyOpenDiscardPrompt(candidate, configuration.ProtectItemsWithMateria, configuration.ProtectItemsWithSpiritbond) ? 1 : 0) : null);
        return notIgnored && ownership && copySafe;
    }

    public bool RequestDiscard(InventoryDuplicateItem candidate) =>
        nativeUi.RequestDiscard(candidate);

    public bool ConfirmDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) =>
        requestIssued && CanSafelyOpenDiscardPrompt(candidate)
        && nativeUi.ConfirmDiscardConfirmation(candidate, requestIssued);

    public void CancelDiscardConfirmation(InventoryDuplicateItem candidate, bool requestIssued) =>
        nativeUi.CancelDiscardConfirmation(candidate, requestIssued);

    public bool OpenInventoryContext(InventoryDuplicateItem candidate) =>
        scanner.CanOpenDresserFallback(collectionData.LatestSnapshot, candidate)
        && nativeUi.OpenInventoryContext(candidate);
}
