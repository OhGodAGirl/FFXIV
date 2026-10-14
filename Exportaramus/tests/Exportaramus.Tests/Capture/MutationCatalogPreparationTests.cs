using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Tests.Capture;

public sealed class MutationCatalogPreparationTests
{
    [Fact]
    public async Task UnfinishedProjectionReusesItsOwnedBuilderUntilCompletion()
    {
        var builders = new List<IncrementalBuilder>();
        using var preparation = new CatalogPreparation(() =>
        {
            var builder = new IncrementalBuilder();
            builders.Add(builder);
            return builder;
        }, (projection, _) => Task.FromResult(CollectionCatalogBuilderPolicy.Build(projection)));

        Assert.False(preparation.Prepare("English", "build"));
        var first = Assert.Single(builders);
        Assert.Equal(1, first.AdvanceCount);
        Assert.Equal(0, first.DisposeCount);

        Assert.False(preparation.Prepare("English", "build"));
        Assert.Same(first, Assert.Single(builders));
        Assert.Equal(2, first.AdvanceCount);
        Assert.Equal(0, first.DisposeCount);

        Assert.False(preparation.Prepare("English", "build"));
        Assert.Same(first, Assert.Single(builders));
        Assert.Equal(3, first.AdvanceCount);
        Assert.Equal(1, first.DisposeCount);
        Assert.True(preparation.Prepare("English", "build"));
        Assert.NotNull(preparation.Catalog);

        preparation.Dispose();
        await preparation.ShutdownTask;
        Assert.Single(builders);
        Assert.Equal(1, first.DisposeCount);
    }

    private sealed class IncrementalBuilder : ICatalogProjectionBuilder
    {
        public int AdvanceCount { get; private set; }
        public int DisposeCount { get; private set; }
        public CatalogProjection? Projection { get; } = new("English", "build", [], [], []);
        public bool Advance(int maximumRows = 256) => ++AdvanceCount == 3;
        public void Dispose() => DisposeCount++;
    }
}
