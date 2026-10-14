using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Tests.Capture;

public sealed class CatalogPreparationTests
{
    private static CatalogProjection Projection(string language = "English", string version = "build") => new(language, version, [], [], []);

    [Fact]
    public void PreparationPassesLargerRowAllowanceToTheTimeBoundedBuilder()
    {
        var builder = new FakeBuilder();
        using var preparation = new CatalogPreparation(() => builder, (value, _) => Task.FromResult(CollectionCatalogBuilderPolicy.Build(value)));
        preparation.Prepare("English", "build");
        Assert.Equal(2048, builder.LastMaximumRows);
    }

    [Fact]
    public async Task PreparationKeepsWorkIncrementalAndAcceptsOnlyMatchingCompletedCatalog()
    {
        var builder = new FakeBuilder { Remaining = 1 };
        var completion = new TaskCompletionSource<CollectionCatalog>();
        using var preparation = new CatalogPreparation(() => builder, (_, _) => completion.Task);
        Assert.Null(preparation.WorkerStatus);
        Assert.False(preparation.Prepare("English", "build")); Assert.False(builder.Disposed);
        Assert.False(preparation.Prepare("English", "build")); Assert.True(builder.Disposed);
        Assert.Equal(TaskStatus.WaitingForActivation, preparation.WorkerStatus);
        Assert.False(preparation.Prepare("English", "build")); Assert.Null(preparation.Catalog);
        completion.SetResult(CollectionCatalogBuilderPolicy.Build(Projection()));
        Assert.True(preparation.Prepare("English", "build"));
        var catalog = preparation.Catalog; Assert.True(preparation.Prepare("English", "build")); Assert.Same(catalog, preparation.Catalog);
        preparation.Dispose(); preparation.Dispose(); await preparation.ShutdownTask;
        Assert.Null(preparation.Catalog); Assert.False(preparation.Prepare("English", "build"));
    }

    [Fact]
    public async Task IdentityChangeRejectsStaleWorkerAndInvalidatesCachedCatalog()
    {
        var projections = new Queue<CatalogProjection>([Projection(), Projection("Japanese"), Projection("Japanese", "next")]);
        var completion = new TaskCompletionSource<CollectionCatalog>(); var calls = 0;
        using var preparation = new CatalogPreparation(() => new FakeBuilder { Projection = projections.Dequeue() },
            (value, _) => ++calls == 1 ? completion.Task : Task.FromResult(CollectionCatalogBuilderPolicy.Build(value)));
        Assert.False(preparation.Prepare("English", "build"));
        completion.SetResult(CollectionCatalogBuilderPolicy.Build(Projection()));
        Assert.False(preparation.Prepare("Japanese", "build")); Assert.Null(preparation.Catalog);
        Assert.True(preparation.Prepare("Japanese", "build"));
        Assert.False(preparation.Prepare("Japanese", "next")); Assert.Null(preparation.Catalog);
        Assert.True(preparation.Prepare("Japanese", "next"));
        preparation.Dispose(); await preparation.ShutdownTask;
    }

    [Fact]
    public async Task SameLanguageWithChangedBuildRejectsOldCompletedWork()
    {
        var first = true;
        using var preparation = new CatalogPreparation(() => new FakeBuilder(), (value, _) =>
            Task.FromResult(CollectionCatalogBuilderPolicy.Build(first ? value : Projection(version: "next"))));
        Assert.False(preparation.Prepare("English", "build")); first = false;
        Assert.False(preparation.Prepare("English", "next")); Assert.True(preparation.Prepare("English", "next"));
        preparation.Dispose(); await preparation.ShutdownTask;
    }

    [Fact]
    public async Task ProjectionFailureDisposesBuilderAndAllowsRetryAndIdleDisposal()
    {
        var bad = new FakeBuilder { Failure = new InvalidDataException() }; var current = bad;
        using var preparation = new CatalogPreparation(() => current, (value, _) => Task.FromResult(CollectionCatalogBuilderPolicy.Build(value)));
        Assert.Throws<InvalidDataException>(() => preparation.Prepare("English", "build")); Assert.True(bad.Disposed);
        current = new FakeBuilder { Remaining = 3 }; Assert.False(preparation.Prepare("English", "build"));
        preparation.Dispose(); Assert.True(current.Disposed); await preparation.ShutdownTask;
    }

    [Fact]
    public async Task FinalizerFailureIsObservedAndAllowsRetry()
    {
        var fail = true;
        using var preparation = new CatalogPreparation(() => new FakeBuilder(), (value, _) => fail
            ? Task.FromException<CollectionCatalog>(new InvalidDataException()) : Task.FromResult(CollectionCatalogBuilderPolicy.Build(value)));
        Assert.False(preparation.Prepare("English", "build"));
        Assert.Throws<InvalidDataException>(() => preparation.Prepare("English", "build")); fail = false;
        Assert.False(preparation.Prepare("English", "build")); Assert.True(preparation.Prepare("English", "build"));
        preparation.Dispose(); await preparation.ShutdownTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsAndObservesOwnedFinalizer(bool canceled)
    {
        var completion = new TaskCompletionSource<CollectionCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        using var preparation = new CatalogPreparation(() => new FakeBuilder(), (_, owned) => { token = owned; return completion.Task; });
        preparation.Prepare("English", "build"); preparation.Dispose();
        Assert.True(token.IsCancellationRequested); Assert.False(preparation.ShutdownTask.IsCompleted);
        if (canceled) completion.SetCanceled(); else completion.SetException(new IOException());
        await preparation.ShutdownTask;
    }

    [Fact]
    public async Task BackgroundFinalizationUsesOnlyImmutableProjectionAndHonorsCancellation()
    {
        var result = await CatalogPreparation.FinalizeInBackground(Projection(), default);
        Assert.Equal("build", result.GameDataVersion); Assert.Empty(result.Collections);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CatalogPreparation.FinalizeInBackground(Projection(), new CancellationToken(true)));
        using var empty = new CatalogPreparation(() => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException());
        empty.Dispose(); await empty.ShutdownTask;
    }

    private sealed class FakeBuilder : ICatalogProjectionBuilder
    {
        public CatalogProjection? Projection { get; init; } = CatalogPreparationTests.Projection();
        public int LastMaximumRows; public int Remaining; public Exception? Failure; public bool Disposed;
        public bool Advance(int maximumRows = 256) { LastMaximumRows = maximumRows; if (Failure is not null) throw Failure; return Remaining-- <= 0; }
        public void Dispose() => Disposed = true;
    }
}
