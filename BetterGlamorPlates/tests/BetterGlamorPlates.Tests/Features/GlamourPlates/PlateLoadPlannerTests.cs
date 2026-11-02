namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Verifies native editor changes are planned only from a complete, exact
/// collection snapshot.
/// </summary>
public sealed class PlateLoadPlannerTests
{
    /// <summary>
    /// Verifies invalid caller dependencies fail before policy evaluation.
    /// </summary>
    [Fact]
    public void CreateRejectsNullDependencies()
    {
        // Arrange
        var plate = CreatePlate("Null guard");
        var snapshot = ReadySnapshot();

        // Act
        var nullPlate = () => PlateLoadPlanner.Create(null!, snapshot, (_, _) => true);
        var nullSnapshot = () => PlateLoadPlanner.Create(plate, null!, (_, _) => true);
        var nullMatcher = () => PlateLoadPlanner.Create(plate, snapshot, null!);

        // Assert
        Assert.Throws<ArgumentNullException>(nullPlate);
        Assert.Throws<ArgumentNullException>(nullSnapshot);
        Assert.Throws<ArgumentNullException>(nullMatcher);
    }

    /// <summary>
    /// Verifies neither collection may be treated as optional during a load.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void CreateRejectsIncompleteCollectionBeforePlanningActions(
        bool dresserLoaded,
        bool armoireLoaded)
    {
        // Arrange
        var plate = CreatePlate("Incomplete collection");
        var snapshot = new NativeCollectionSnapshot(
            [],
            dresserLoaded,
            armoireLoaded,
            DateTime.UtcNow);

        // Act
        var plan = PlateLoadPlanner.Create(plate, snapshot, (_, _) => true);

        // Assert
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
        Assert.Collection(
            plan.Issues,
            issue => Assert.Equal(PlateLoadIssueKind.CollectionNotReady, issue.Kind));
    }

    /// <summary>
    /// Verifies every invalid or unavailable slot is reported without exposing
    /// a partial native action list.
    /// </summary>
    [Fact]
    public void CreateRejectsWholePlateWhenAnySavedSlotIsInvalid()
    {
        // Arrange
        var plate = CreatePlate("Invalid slots");
        plate.Items[0] = new SavedPlateItem { ItemId = 100 };
        plate.Items[1] = new SavedPlateItem { ItemId = 200, Stain0Id = 7 };
        var snapshot = ReadySnapshot(
            new NativeCollectionItem(100, CollectionSource.Dresser, 4, 0, 0),
            new NativeCollectionItem(200, CollectionSource.Dresser, 5, 0, 0));

        // Act
        var plan = PlateLoadPlanner.Create(
            plate,
            snapshot,
            (itemId, _) => itemId != 100);

        // Assert
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
        Assert.Collection(
            plan.Issues,
            issue => Assert.Equal(PlateLoadIssueKind.InvalidSlot, issue.Kind));
    }

    /// <summary>
    /// Verifies slot incompatibility is authoritative and does not also report
    /// source availability for an item that can never occupy that slot.
    /// </summary>
    [Fact]
    public void CreateReportsOnlyInvalidSlotWhenSourceIsAlsoUnavailable()
    {
        var plate = CreatePlate("Invalid and unavailable");
        plate.Items[0] = new SavedPlateItem { ItemId = 500 };

        var plan = PlateLoadPlanner.Create(
            plate,
            ReadySnapshot(),
            (_, _) => false);

        var issue = Assert.Single(plan.Issues);
        Assert.Equal(PlateLoadIssueKind.InvalidSlot, issue.Kind);
        Assert.Equal(PlateSlot.MainHand, issue.Slot);
        Assert.Equal(500u, issue.ItemId);
        Assert.Empty(plan.Steps);
    }

    /// <summary>
    /// Verifies a valid exact source and all empty native slots form one complete
    /// twelve-slot plan.
    /// </summary>
    [Fact]
    public void CreateBuildsCompletePlanForExactSourcesAndClears()
    {
        // Arrange
        var plate = CreatePlate("Ready");
        plate.Items[3] = new SavedPlateItem { ItemId = 300, Stain0Id = 8 };
        NativeCollectionItem source = new(
            300,
            CollectionSource.Dresser,
            6,
            8,
            0);
        var snapshot = ReadySnapshot(source);

        // Act
        var plan = PlateLoadPlanner.Create(plate, snapshot, (_, _) => true);

        // Assert
        Assert.True(plan.CanApply);
        Assert.Empty(plan.Issues);
        Assert.Equal(12, plan.Steps.Count);
        Assert.Same(source, plan.Steps[3].Source);
        Assert.Null(plan.Steps[0].SavedItem);
        Assert.Null(plan.Steps[0].Source);
    }

    /// <summary>
    /// Verifies malformed short legacy arrays are interpreted without mutating
    /// the saved configuration during planning.
    /// </summary>
    [Fact]
    public void CreateDoesNotMutateMalformedLegacyShape()
    {
        // Arrange
        var plate = CreatePlate("Legacy");
        plate.Items = [new SavedPlateItem { ItemId = 400 }];
        var snapshot = ReadySnapshot(
            new NativeCollectionItem(400, CollectionSource.Armoire, 7, 0, 0));

        // Act
        var plan = PlateLoadPlanner.Create(plate, snapshot, (_, _) => true);

        // Assert
        Assert.True(plan.CanApply);
        Assert.Single(plate.Items);
        Assert.Equal(12, plan.Steps.Count);
    }

    /// <summary>
    /// Verifies null legacy arrays and zero-ID entries both mean empty slots and
    /// never invoke slot validation.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateTreatsLegacyEmptyRepresentationsAsClears(bool nullArray)
    {
        // Arrange
        var plate = CreatePlate("Legacy empty");
        plate.Items = nullArray
            ? null!
            : [new SavedPlateItem { ItemId = 0 }];
        var matcherCalled = false;

        // Act
        var plan = PlateLoadPlanner.Create(
            plate,
            ReadySnapshot(),
            (_, _) =>
            {
                matcherCalled = true;
                return true;
            });

        // Assert
        Assert.True(plan.CanApply);
        Assert.Equal(12, plan.Steps.Count);
        Assert.All(plan.Steps, step => Assert.Null(step.SavedItem));
        Assert.False(matcherCalled);
    }

    private static NativeCollectionSnapshot ReadySnapshot(params NativeCollectionItem[] items)
        => new(items, true, true, DateTime.UtcNow);

    private static SavedPlate CreatePlate(string name)
        => PlateLibrary.Create(
            name,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
}

