using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Partial loading distinguishes missing occupied slots from deliberate empty-slot clears.</summary>
public sealed class PartialPlateLoadTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Available items load, missing items skip and deliberately empty slots keep their clear meaning.</summary>
    [Fact]
    public void PartialPlanKeepsAllTwelveIndependentDecisions()
    {
        var plate = Plate();
        var plan = Partial(plate, Snapshot());
        Assert.True(plan.CanApply);
        Assert.True(PlateResultPolicy.IsExecutable(plan));
        Assert.Equal(12, plan.Steps.Count);
        Assert.False(plan.Steps[0].IsSkipped);
        Assert.Equal(100u, plan.Steps[0].Source!.ItemId);
        Assert.True(plan.Steps[3].IsSkipped);
        Assert.Equal(200u, plan.Steps[3].SavedItem!.ItemId);
        Assert.Null(plan.Steps[3].Source);
        Assert.False(plan.Steps[11].IsSkipped);
        Assert.Null(plan.Steps[11].SavedItem);
        Assert.Null(plan.Steps[11].Source);
        Assert.Equal(new PlateLoadIssue(PlateLoadIssueKind.ExactSourceUnavailable, PlateSlot.Body, 200), Assert.Single(plan.Issues));
    }

    /// <summary>Strict callers retain complete rejection until partial loading is explicitly requested.</summary>
    [Fact]
    public void MissingItemsDoNotImplicitlyEnablePartialLoading()
    {
        var plan = PlateLoadPlanner.Create(Plate(), Snapshot(), (_, _) => true);
        Assert.False(plan.CanApply);
        Assert.False(PlateResultPolicy.IsExecutable(plan));
        Assert.Empty(plan.Steps);
        Assert.Equal(PlateLoadIssueKind.ExactSourceUnavailable, Assert.Single(plan.Issues).Kind);
    }

    /// <summary>Neither unread source can be represented as a list of missing items.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void PartialLoadStillRequiresBothCompleteNativeSources(bool dresser, bool armoire)
    {
        var plan = PlateLoadPlanner.Create(Plate(), Snapshot() with { IsDresserLoaded = dresser, IsArmoireLoaded = armoire },
            (_, _) => throw new InvalidOperationException("Incomplete sources must stop before per-item planning."),
            allowMissingItems: true, validationTimeUtc: Now);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
        Assert.Equal(PlateLoadIssueKind.CollectionNotReady, Assert.Single(plan.Issues).Kind);
    }

    /// <summary>Only the snapshot captured for this action can establish that an item is missing.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void RejectsRetainedAndFutureSnapshots(int offsetSeconds)
    {
        var plan = Partial(Plate(), Snapshot() with { CapturedAtUtc = Now.AddSeconds(offsetSeconds) });
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
        Assert.Equal(PlateLoadIssueKind.CollectionNotReady, Assert.Single(plan.Issues).Kind);
    }

    /// <summary>A partial caller must explicitly bind action time rather than silently trusting any snapshot.</summary>
    [Fact]
    public void MissingActionTimeCannotAuthorizeSkips()
    {
        var plan = PlateLoadPlanner.Create(Plate(), Snapshot(), (_, _) => true, allowMissingItems: true);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
    }

    /// <summary>Invalid slot facts reject every action even when missing items would otherwise be skipped.</summary>
    [Fact]
    public void InvalidSlotRemainsAWholeOperationGate()
    {
        var plan = PlateLoadPlanner.Create(Plate(), Snapshot(), (itemId, _) => itemId != 100,
            allowMissingItems: true, validationTimeUtc: Now);
        Assert.False(plan.CanApply);
        Assert.False(PlateResultPolicy.IsExecutable(plan));
        Assert.Empty(plan.Steps);
        Assert.Contains(plan.Issues, issue => issue.Kind == PlateLoadIssueKind.InvalidSlot && issue.Slot == PlateSlot.MainHand);
        Assert.Contains(plan.Issues, issue => issue.Kind == PlateLoadIssueKind.ExactSourceUnavailable && issue.Slot == PlateSlot.Body);
    }

    /// <summary>An entirely unavailable outfit contains only no-op steps, not accidental clears.</summary>
    [Fact]
    public void EveryMissingItemLeavesItsNativeSlotUntouched()
    {
        var plate = Plate();
        plate.Items = Enumerable.Range(0, 12).Select(index => (SavedPlateItem?)new SavedPlateItem { ItemId = (uint)(300 + index) }).ToArray();
        var plan = Partial(plate, Snapshot());
        Assert.True(PlateResultPolicy.IsExecutable(plan));
        Assert.Equal(12, plan.Issues.Count);
        Assert.All(plan.Steps, step => { Assert.True(step.IsSkipped); Assert.NotNull(step.SavedItem); Assert.Null(step.Source); });
        var dyes = PlateDyePlanner.Create(plan, [], _ => throw new InvalidOperationException("Skipped items must not resolve dye capability or supplies."));
        Assert.Empty(dyes.Changes);
        Assert.Empty(dyes.Failures);
    }

    /// <summary>Saved and undyed partial plans retain both channels in local data and snapshot skipped intent independently.</summary>
    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 0)]
    public void PartialModePreservesSavedLocalColours(int mode, byte desired)
    {
        var plate = Plate();
        plate.Items[3]!.Stain0Id = 7;
        plate.Items[3]!.Stain1Id = 9;
        var plan = PlateLoadPlanner.Create(plate, Snapshot(), (_, _) => true, (PlateDyeMode)mode, true, Now);
        Assert.Equal(desired, plan.Steps[3].SavedItem!.Stain0Id);
        Assert.Equal(7, plate.Items[3]!.Stain0Id);
        Assert.Equal(9, plate.Items[3]!.Stain1Id);
        Assert.NotSame(plate.Items[3], plan.Steps[3].SavedItem);
    }

    /// <summary>Skipped dye intent cannot consume shared material needed by an available item.</summary>
    [Fact]
    public void MaterialsAreAllocatedOnlyForAvailableItems()
    {
        var plate = Plate();
        plate.Items[0]!.Stain0Id = 7;
        plate.Items[3]!.Stain0Id = 7;
        var plan = Partial(plate, Snapshot());
        var dyes = PlateDyePlanner.Create(plan, [new(7, 555, 0, 0, 1)], itemId =>
        {
            Assert.Equal(100u, itemId);
            return 1;
        });
        Assert.Empty(dyes.Failures);
        Assert.Equal(PlateSlot.MainHand, Assert.Single(dyes.Changes).Slot);
        var shortage = PlateDyePlanner.Create(plan, [], _ => 1);
        Assert.Empty(shortage.Changes);
        Assert.Equal(PlateApplyFailureKind.DyeUnavailable, Assert.Single(shortage.Failures).Kind);
    }

    /// <summary>Unsupported channels on an available item remain a complete material-preflight failure.</summary>
    [Fact]
    public void UnsupportedAvailableDyesStillRejectAllChanges()
    {
        var plate = Plate();
        plate.Items[0]!.Stain1Id = 7;
        var dyes = PlateDyePlanner.Create(Partial(plate, Snapshot()), [new(7, 555, 0, 0, 5)], _ => 1);
        Assert.Empty(dyes.Changes);
        Assert.Equal(PlateApplyFailureKind.UnsupportedDye, Assert.Single(dyes.Failures).Kind);
    }

    /// <summary>Skip intent is executable only with a one-to-one matching missing-item issue.</summary>
    [Fact]
    public void RejectsForgedOrAmbiguousSkipSteps()
    {
        var valid = Partial(Plate(), Snapshot());
        Assert.True(PlateResultPolicy.IsExecutable(valid));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { AllowMissingItems = false }));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Issues = [] }));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Issues = [new(PlateLoadIssueKind.ExactSourceUnavailable, PlateSlot.Body, 999)] }));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Issues = [new(PlateLoadIssueKind.ExactSourceUnavailable, PlateSlot.Head, 200)] }));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Issues = [new(PlateLoadIssueKind.ExactSourceUnavailable)] }));
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Issues = [valid.Issues[0], valid.Issues[0]] }));
        var steps = valid.Steps.ToArray();
        steps[3] = steps[3] with { SavedItem = null };
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Steps = steps }));
        steps[3] = valid.Steps[3] with { Source = new(200, CollectionSource.Dresser, 1, 0, 0) };
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Steps = steps }));
        steps[3] = valid.Steps[3] with { IsSkipped = false };
        Assert.False(PlateResultPolicy.IsExecutable(valid with { Steps = steps }));
    }

    /// <summary>No invalid source enum may fall through to an Armoire native write.</summary>
    [Fact]
    public void ExecutabilityRejectsUnsupportedSourceKinds()
    {
        var plan = Partial(Plate(), Snapshot());
        var steps = plan.Steps.ToArray();
        steps[0] = steps[0] with { Source = steps[0].Source! with { Source = (CollectionSource)99 } };
        Assert.False(PlateResultPolicy.IsExecutable(plan with { Steps = steps }));
    }

    private static SavedPlate Plate()
    {
        var plate = PlateLibrary.Create("Partial fixture", Now, Guid.NewGuid());
        plate.Items[0] = new() { ItemId = 100 };
        plate.Items[3] = new() { ItemId = 200 };
        return plate;
    }

    private static NativeCollectionSnapshot Snapshot() => new([new(100, CollectionSource.Dresser, 7, 0, 0)], true, true, Now);
    private static PlateLoadPlan Partial(SavedPlate plate, NativeCollectionSnapshot snapshot)
        => PlateLoadPlanner.Create(plate, snapshot, (_, _) => true, allowMissingItems: true, validationTimeUtc: Now);
}
