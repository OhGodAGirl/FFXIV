using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Prevents an item-only acknowledgement from accepting the wrong appearance or source.</summary>
public sealed class PlateResultPolicyTests
{
    /// <summary>Only an exact source, item and two-dye result without pending dyes succeeds.</summary>
    [Theory]
    [InlineData(100u, 7u, 1, 2, false, true)]
    [InlineData(101u, 7u, 1, 2, false, false)]
    [InlineData(100u, 8u, 1, 2, false, false)]
    [InlineData(100u, 7u, 0, 2, false, false)]
    [InlineData(100u, 7u, 1, 0, false, false)]
    [InlineData(100u, 7u, 1, 2, true, false)]
    public void MatchesRequiresExactReadback(uint item, uint locator, byte first, byte second, bool pending, bool expected)
    {
        var source = new NativeCollectionItem(100, CollectionSource.Dresser, 7, 1, 2);
        Assert.Equal(expected, PlateResultPolicy.Matches(source, item, locator, CollectionSource.Dresser, first, second, pending));
        Assert.False(PlateResultPolicy.Matches(source, 100, 7, CollectionSource.Armoire, 1, 2, false));
        Assert.False(PlateResultPolicy.Matches(source, 100, 7, null, 1, 2, false));
    }

    /// <summary>Duplicate, missing, invalid or issue-bearing plans cannot reach native writes.</summary>
    [Fact]
    public void ExecutableRequiresOneStepPerSlot()
    {
        var steps = Enumerable.Range(0, 12).Select(index => new PlateLoadStep((PlateSlot)index, null, null)).ToArray();
        Assert.True(PlateResultPolicy.IsExecutable(new(steps, [])));
        Assert.False(PlateResultPolicy.IsExecutable(new(steps[..11], [])));
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [new(PlateLoadIssueKind.CollectionNotReady)])));
        steps[11] = steps[0];
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
        steps[11] = new((PlateSlot)99, null, null);
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
        steps[11] = new((PlateSlot)12, null, null);
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
    }

    /// <summary>The owned item must match; requested colour changes are checked by dye preflight.</summary>
    [Fact]
    public void ExecutableRejectsMismatchedItemsAndHalfEmptySteps()
    {
        var steps = Enumerable.Range(0, 12).Select(index => new PlateLoadStep((PlateSlot)index, null, null)).ToArray();
        var saved = new SavedPlateItem { ItemId = 100, Stain0Id = 1, Stain1Id = 2 };
        var source = new NativeCollectionItem(100, CollectionSource.Dresser, 7, 1, 2);
        steps[0] = new(PlateSlot.MainHand, saved, source);
        Assert.True(PlateResultPolicy.IsExecutable(new(steps, [])));
        foreach (var mismatch in new[] { source with { ItemId = 101 }, source with { Stain0Id = 0 }, source with { Stain1Id = 0 } })
        {
            steps[0] = new(PlateSlot.MainHand, saved, mismatch);
            Assert.Equal(mismatch.ItemId == saved.ItemId, PlateResultPolicy.IsExecutable(new(steps, [])));
        }
        steps[0] = new(PlateSlot.MainHand, null, source);
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
        steps[0] = new(PlateSlot.MainHand, saved, null);
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
        steps[0] = new((PlateSlot)(-1), null, null);
        Assert.False(PlateResultPolicy.IsExecutable(new(steps, [])));
    }
}
