namespace CollectionShopper.Tests;

public sealed class CollectionOwnershipRefreshPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void OwnershipRefreshDependsOnlyOnLifecycleState(
        bool disposed,
        bool refreshInProgress,
        bool expected)
    {
        Assert.Equal(
            expected,
            CollectionOwnershipRefreshPolicy.CanStart(disposed, refreshInProgress));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void CatalogRefreshIsRequiredOnlyForTheAutomaticFirstLoad(
        bool collectionLoaded,
        bool automaticallyRefresh,
        bool expected)
    {
        Assert.Equal(
            expected,
            CollectionOwnershipRefreshPolicy.RequiresCatalogRefresh(
                collectionLoaded,
                automaticallyRefresh));
    }
}
