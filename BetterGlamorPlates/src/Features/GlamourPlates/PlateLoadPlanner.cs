namespace BetterGlamorPlates.Features.GlamourPlates;

internal enum PlateDyeMode { Saved, None }

internal enum PlateLoadIssueKind
{
    CollectionNotReady,
    InvalidSlot,
    ExactSourceUnavailable,
}

internal sealed record PlateLoadIssue(
    PlateLoadIssueKind Kind,
    PlateSlot? Slot = null,
    uint ItemId = 0);

internal sealed record PlateLoadStep(
    PlateSlot Slot,
    SavedPlateItem? SavedItem,
    NativeCollectionItem? Source)
{
    // An occupied but unavailable slot is distinct from an explicitly empty slot.
    internal bool IsSkipped { get; init; }
}

internal sealed record PlateLoadPlan(
    IReadOnlyList<PlateLoadStep> Steps,
    IReadOnlyList<PlateLoadIssue> Issues)
{
    internal bool AllowMissingItems { get; init; }
    internal bool CanApply => Issues.Count == 0
        || (AllowMissingItems && Issues.All(issue => issue.Kind == PlateLoadIssueKind.ExactSourceUnavailable));
}

/// <summary>
/// Validates a complete local plate against one immutable collection snapshot
/// before the native editor receives any changes.
/// </summary>
internal static class PlateLoadPlanner
{
    internal static PlateLoadPlan Create(
        SavedPlate plate,
        NativeCollectionSnapshot snapshot,
        Func<uint, PlateSlot, bool> matchesSlot,
        PlateDyeMode dyeMode = PlateDyeMode.Saved,
        bool allowMissingItems = false,
        DateTime? validationTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(plate);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(matchesSlot);
        if (!Enum.IsDefined(dyeMode)) throw new ArgumentOutOfRangeException(nameof(dyeMode));

        // Skipping requires a complete native observation captured for this deliberate action.
        // A retained snapshot cannot turn unknown availability into a missing-item decision.
        if (!snapshot.IsDresserLoaded || !snapshot.IsArmoireLoaded
            || (allowMissingItems && snapshot.CapturedAtUtc != validationTimeUtc))
        {
            return new PlateLoadPlan(
                [],
                [new PlateLoadIssue(PlateLoadIssueKind.CollectionNotReady)]);
        }

        var steps = new List<PlateLoadStep>(PlateRules.SlotCount);
        var issues = new List<PlateLoadIssue>();
        var items = plate.Items ?? [];

        for (var index = 0; index < PlateRules.SlotCount; index++)
        {
            var slot = (PlateSlot)index;
            var savedItem = index < items.Length ? items[index] : null;
            if (savedItem is null || savedItem.ItemId == 0)
            {
                steps.Add(new PlateLoadStep(slot, null, null));
                continue;
            }

            if (!matchesSlot(savedItem.ItemId, slot))
            {
                issues.Add(new PlateLoadIssue(
                    PlateLoadIssueKind.InvalidSlot,
                    slot,
                    savedItem.ItemId));
                continue;
            }

            var desired = new SavedPlateItem
            {
                ItemId = savedItem.ItemId,
                Stain0Id = dyeMode == PlateDyeMode.None ? (byte)0 : savedItem.Stain0Id,
                Stain1Id = dyeMode == PlateDyeMode.None ? (byte)0 : savedItem.Stain1Id,
            };
            // Prefer an exact appearance, then the source needing the fewest dye changes.
            var source = CollectionSourceResolver.ResolveClosest(snapshot.Items, desired);
            if (source is null)
            {
                issues.Add(new PlateLoadIssue(
                    PlateLoadIssueKind.ExactSourceUnavailable,
                    slot,
                    savedItem.ItemId));
                if (allowMissingItems)
                    steps.Add(new PlateLoadStep(slot, desired, null) { IsSkipped = true });
            }
            else
            {
                steps.Add(new PlateLoadStep(slot, desired, source));
            }
        }

        // Only missing items can become explicit no-op steps. Every other issue still
        // rejects the entire operation before the native editor receives any changes.
        var plan = new PlateLoadPlan(steps, issues) { AllowMissingItems = allowMissingItems };
        return plan.CanApply ? plan : plan with { Steps = [] };
    }
}
