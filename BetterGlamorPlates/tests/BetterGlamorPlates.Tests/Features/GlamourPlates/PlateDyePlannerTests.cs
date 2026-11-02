using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Verifies the complete outfit's pending dye costs and exact native acknowledgements.</summary>
public sealed class PlateDyePlannerTests
{
    private static PlateLoadPlan Plan(byte desired0, byte desired1, byte source0 = 0, byte source1 = 0)
        => new([new(PlateSlot.Body, new SavedPlateItem { ItemId = 20, Stain0Id = desired0, Stain1Id = desired1 },
            new NativeCollectionItem(20, CollectionSource.Dresser, 5, source0, source1))], []);

    /// <summary>Matching channels cost nothing; removing stored colours allocates Terebinth.</summary>
    [Fact]
    public void MatchingDyesAreFreeAndRemovalRequiresTerebinth()
    {
        Assert.Empty(PlateDyePlanner.Create(Plan(7, 9, 7, 9), [], _ => 2).Changes);
        var missing = PlateDyePlanner.Create(Plan(0, 0, 7, 9), [], _ => 2);
        Assert.Empty(missing.Changes);
        Assert.Equal(2, missing.Failures.Count);
        var removal = PlateDyePlanner.Create(Plan(0, 0, 7, 9), [new(0, 5728, 0, 4, 2)], _ => 2);
        Assert.Empty(removal.Failures);
        Assert.Equal(2, removal.Changes.Count);
        Assert.All(removal.Changes, change => { Assert.Equal(0, change.DyeId); Assert.Equal(5728u, change.Supply.ItemId); });
    }

    /// <summary>Two channels cannot allocate the same single consumable twice.</summary>
    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    public void ReservesQuantityAcrossBothChannels(uint count, bool success)
    {
        var result = PlateDyePlanner.Create(Plan(7, 7), [new(7, 100, 0, 4, count)], _ => 2);
        Assert.Equal(success, result.Failures.Count == 0);
        Assert.Equal(success ? 2 : 0, result.Changes.Count);
        if (!success) Assert.Equal(PlateApplyFailureKind.DyeUnavailable, Assert.Single(result.Failures).Kind);
    }

    /// <summary>Multiple stacks and equivalent dye items are usable across all equipment slots.</summary>
    [Fact]
    public void AllocatesAlternativeDyeStacksAcrossOutfit()
    {
        var plan = Plan(7, 7);
        plan = plan with { Steps = [plan.Steps[0], plan.Steps[0] with { Slot = PlateSlot.Head }] };
        DyeSupply[] supplies = [new(7, 100, 0, 4, 1), new(7, 101, 1, 4, 3)];
        var result = PlateDyePlanner.Create(plan, supplies, _ => 2);
        Assert.Empty(result.Failures);
        Assert.Equal(new uint[] { 100, 101, 101, 101 }, result.Changes.Select(change => change.Supply!.ItemId));
    }

    /// <summary>Unsupported channels reject the whole plan before any dye can be dispatched.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RejectsUnsupportedSavedChannels(byte channels)
    {
        var result = PlateDyePlanner.Create(Plan(0, 7), [new(7, 100, 0, 4, 2)], _ => channels);
        Assert.Empty(result.Changes);
        Assert.Equal(PlateApplyFailureKind.UnsupportedDye, Assert.Single(result.Failures).Kind);
    }

    /// <summary>Unexpected capability values fail closed even when no saved dye is requested.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    public void RejectsInvalidCapability(byte channels)
    {
        var result = PlateDyePlanner.Create(Plan(0, 0), [], _ => channels);
        Assert.Empty(result.Changes);
        Assert.Equal(PlateApplyFailureKind.UnsupportedDye, Assert.Single(result.Failures).Kind);
    }

    /// <summary>An acknowledgement must name the allocated inventory dye and its exact location.</summary>
    [Theory]
    [InlineData(7, true, false, 100u, 1u, 4, true)]
    [InlineData(8, true, false, 100u, 1u, 4, false)]
    [InlineData(7, false, false, 100u, 1u, 4, false)]
    [InlineData(7, true, true, 100u, 1u, 4, false)]
    [InlineData(7, true, false, 101u, 1u, 4, false)]
    [InlineData(7, true, false, 100u, 2u, 4, false)]
    [InlineData(7, true, false, 100u, 1u, 5, false)]
    public void RequiresExactPendingDyeReadback(byte dye, bool inventory, bool item, uint itemId,
        uint container, ushort slot, bool matches)
    {
        var change = new PlateDyeChange(PlateSlot.Body, 1, 7, new(7, 100, 1, 4, 1));
        Assert.Equal(matches, PlateDyePlanner.Matches(change, dye, inventory, item, itemId, container, slot));
    }

    /// <summary>Removal must acknowledge the exact Terebinth allocation, even though the colour is zero.</summary>
    [Fact]
    public void RemovalAcknowledgementRequiresTerebinth()
    {
        var change = new PlateDyeChange(PlateSlot.Head, 0, 0, new(0, 5728, 0, 4, 2));
        Assert.True(PlateDyePlanner.Matches(change, 0, true, false, 5728, 0, 4));
        Assert.False(PlateDyePlanner.Matches(change, 0, false, false, 0, 0, 0));
        Assert.False(PlateDyePlanner.Matches(change, 7, true, false, 5728, 0, 4));
    }

    /// <summary>Spectrum dyes shared by different colours still share one physical stack quantity.</summary>
    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    public void SpectrumColoursShareStackBudget(uint quantity, bool success)
    {
        var result = PlateDyePlanner.Create(Plan(7, 9),
            [new(7, 52254, 0, 4, quantity), new(9, 52254, 0, 4, quantity)], _ => 2);
        Assert.Equal(success, result.Failures.Count == 0);
        Assert.Equal(success ? 2 : 0, result.Changes.Count);
    }

    /// <summary>Removing dyes changes the plan only, and chooses an undyed source when available.</summary>
    [Fact]
    public void UndyedLoadKeepsLocalSavedDyes()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        var source = new NativeCollectionSnapshot([new(20, CollectionSource.Dresser, 4, 7, 0),
            new(20, CollectionSource.Armoire, 5, 0, 0)], true, true, TestDomainValues.Timestamp);
        var plan = PlateLoadPlanner.Create(plate, source, (_, _) => true, PlateDyeMode.None);
        Assert.True(plan.CanApply);
        var step = plan.Steps[(int)PlateSlot.Body];
        Assert.Equal(0, step.SavedItem!.Stain0Id);
        Assert.Equal(CollectionSource.Armoire, step.Source!.Source);
        Assert.Equal(7, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
    }
    /// <summary>Skipped and incomplete steps cannot allocate dye or consult item capability.</summary>
    [Fact]
    public void IgnoresSkippedAndIncompleteSteps()
    {
        var step = Plan(7, 9).Steps[0];
        var plan = new PlateLoadPlan([step with { IsSkipped = true }, step with { SavedItem = null }, step with { Source = null }], []);
        var result = PlateDyePlanner.Create(plan, [], _ => throw new InvalidOperationException("Unexpected capability read"));
        Assert.Empty(result.Changes);
        Assert.Empty(result.Failures);
    }

    /// <summary>Stored colours are compared against their matching channel, without charging unchanged dyes.</summary>
    [Fact]
    public void ComparesStoredDyesPerChannel()
    {
        var result = PlateDyePlanner.Create(Plan(7, 9, 7, 9), [new(7, 100, 0, 1, 2), new(9, 101, 0, 2, 2)], _ => 2);
        Assert.Empty(result.Changes);
        Assert.Empty(result.Failures);
    }

    /// <summary>Invalid capability reports one item failure; unsupported channels do not also report missing supply.</summary>
    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 1)]
    public void UnsupportedCapabilityDoesNotAttemptAllocation(byte channels, int expected)
    {
        var result = PlateDyePlanner.Create(Plan(0, 9), [], _ => channels);
        Assert.Empty(result.Changes);
        Assert.Equal(expected, result.Failures.Count);
        Assert.All(result.Failures, failure => Assert.Equal(PlateApplyFailureKind.UnsupportedDye, failure.Kind));
    }

    /// <summary>Wrong colours and records without an inventory item cannot satisfy a dye request.</summary>
    [Fact]
    public void RejectsWrongColourAndMissingItemSupplies()
    {
        var result = PlateDyePlanner.Create(Plan(7, 0), [new(9, 101, 0, 1, 9), new(7, 0, 0, 2, 9)], _ => 2);
        Assert.Empty(result.Changes);
        Assert.Equal(PlateApplyFailureKind.DyeUnavailable, Assert.Single(result.Failures).Kind);
    }
}
