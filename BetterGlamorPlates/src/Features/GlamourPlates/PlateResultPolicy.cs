namespace BetterGlamorPlates.Features.GlamourPlates;

/// <summary>Requires exact appearance and locator readback; pending dye operations remain ambiguous.</summary>
internal static class PlateResultPolicy
{
    internal static bool Matches(NativeCollectionItem expected, uint itemId, uint sourceId,
        CollectionSource? source, byte stain0, byte stain1, bool pendingDye)
        => !pendingDye && expected.ItemId == itemId && expected.SourceId == sourceId
            && expected.Source == source && expected.Stain0Id == stain0 && expected.Stain1Id == stain1;

    internal static bool IsExecutable(PlateLoadPlan plan)
        => plan.CanApply && plan.Steps.Count == PlateRules.SlotCount
            && plan.Steps.Select(step => step.Slot).Distinct().Count() == PlateRules.SlotCount
            && plan.Issues.Count == plan.Steps.Count(step => step.IsSkipped)
            && plan.Steps.All(step => IsValidStep(plan, step));

    private static bool IsValidStep(PlateLoadPlan plan, PlateLoadStep step)
    {
        if ((int)step.Slot is < 0 or >= PlateRules.SlotCount) return false;
        if (step.IsSkipped)
            return plan.AllowMissingItems && step.Source is null && step.SavedItem is { ItemId: > 0 } missing
                && plan.Issues.Count(issue => issue.Kind == PlateLoadIssueKind.ExactSourceUnavailable
                    && issue.Slot == step.Slot && issue.ItemId == missing.ItemId) == 1;
        return (step.SavedItem is null && step.Source is null)
            || (step.SavedItem is { ItemId: > 0 } saved && step.Source is { } source
                && Enum.IsDefined(source.Source) && saved.ItemId == source.ItemId);
    }
}
