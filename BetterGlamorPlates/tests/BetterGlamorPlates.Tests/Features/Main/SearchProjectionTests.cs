using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Queued searches and cached icon projections remain consistent across frames.</summary>
public sealed class SearchProjectionTests
{
    /// <summary>Old acknowledgements must not replace newer typing; a library reset may do so.</summary>
    [Fact]
    public void SearchReconcilesOnlyAcknowledgedTypingOrLibraryChanges()
    {
        var state = new LibrarySearchState();
        Assert.Null(state.Reconcile("", 0));
        Assert.Equal("old", state.Reconcile("old", 1));
        state.Pending = "newer";

        var stale = state.Reconcile("new", 1);

        Assert.Null(stale);
        Assert.Equal("newer", state.Pending);
        Assert.Null(state.Reconcile("new", 1));
        Assert.Equal("newer", state.Reconcile("newer", 1));
        Assert.Null(state.Pending);
        state.Pending = "unacknowledged";
        Assert.Equal("", state.Reconcile("", 2));
        Assert.Null(state.Pending);
    }

    /// <summary>Source identity, category and query each invalidate the projection exactly once.</summary>
    [Fact]
    public void IconFilterInvalidatesEachInputAndPreservesCatalogOrder()
    {
        GameIconOption[] source = [new(3, "Ruby", "Weapons"), new(1, "Red", "Weapons"), new(2, "Ruby", "Armour")];
        var filter = new IconChoiceFilter();
        Assert.Empty(filter.Items);

        Assert.True(filter.Update(source, "Weapons", "R"));

        Assert.Equal(new uint[] { 3, 1 }, filter.Items.Select(item => item.Id));
        var same = filter.Items;
        Assert.False(filter.Update(source, "Weapons", "R"));
        Assert.Same(same, filter.Items);
        Assert.True(filter.Update(source, "Weapons", "RUBY"));
        Assert.Equal((uint)3, Assert.Single(filter.Items).Id);
        Assert.True(filter.Update(source, "Armour", "RUBY"));
        Assert.Equal((uint)2, Assert.Single(filter.Items).Id);
        Assert.True(filter.Update([], "Armour", "RUBY"));
        Assert.Empty(filter.Items);
    }
}
