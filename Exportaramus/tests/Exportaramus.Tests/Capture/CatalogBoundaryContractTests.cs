using System.Collections;
using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Tests.Capture;

public sealed class CatalogBoundaryContractTests
{
    [Fact]
    public void ProjectionWorkDependsOnElapsedTimeAndNotTheClockEpoch()
    {
        static int AdvanceAt(double epoch)
        {
            var rows = new Rows();
            var tick = 0;
            using var projection = new BoundedCatalogProjection("English", "build", rows, () => ("English", "build"),
                () => epoch + tick++ * 0.125);
            Assert.False(projection.Advance(3));
            return rows.MoveCount;
        }
        Assert.Equal(3, AdvanceAt(0));
        Assert.Equal(3, AdvanceAt(100000));
    }

    [Fact]
    public void CompletedProjectionRetainsItsImmutableResultWithoutReenteringTheSource()
    {
        var rows = new Rows { Available = 1 };
        var identityReads = 0;
        using var projection = new BoundedCatalogProjection("English", "build", rows,
            () => { identityReads++; return ("English", "build"); }, () => 0);
        Assert.True(projection.Advance());
        var accepted = projection.Projection;
        var moveCount = rows.MoveCount;
        Assert.True(projection.Advance());
        Assert.Same(accepted, projection.Projection);
        Assert.Equal(moveCount, rows.MoveCount);
        Assert.Equal(1, identityReads);
    }

    [Fact]
    public void DisposingAnIncompleteProjectionReleasesItsEnumeratorAndPreventsMoreSourceReads()
    {
        var rows = new Rows();
        var identityReads = 0;
        var projection = new BoundedCatalogProjection("English", "build", rows,
            () => { identityReads++; return ("English", "build"); }, () => 0);
        Assert.False(projection.Advance(1));
        projection.Dispose();
        Assert.True(rows.Disposed);
        var moveCount = rows.MoveCount;
        Assert.True(projection.Advance());
        Assert.Equal(moveCount, rows.MoveCount);
        Assert.Equal(1, identityReads);
        Assert.Null(projection.Projection);
    }

    [Fact]
    public void FieldRecordIdentityUsesItsNumberEvenWhenTheSheetRowIsZero()
    {
        var row = CatalogRowPolicy.Project(new("field_records", 0, "Record", LinkId: 1));
        Assert.Equal("field_records", row.Key);
        Assert.Equal(1u, row.Item.Id);
        Assert.Equal(0u, row.Item.SourceRowId);
    }

    [Fact]
    public void CanceledFinalizationDoesNotStartMapping()
    {
        var projection = new CatalogProjection("English", "build", [], [], []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var error = Assert.Throws<OperationCanceledException>(() => CatalogPreparation.FinalizeProjection(projection, cancellation.Token,
            value => { calls++; return CollectionCatalogBuilderPolicy.Build(value); }));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CancellationDuringMappingCannotReturnThePreparedCatalog()
    {
        var projection = new CatalogProjection("English", "build", [], [], []);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var error = Assert.Throws<OperationCanceledException>(() => CatalogPreparation.FinalizeProjection(projection, cancellation.Token,
            value => { calls++; var result = CollectionCatalogBuilderPolicy.Build(value); cancellation.Cancel(); return result; }));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, calls);
    }

    private sealed class Rows : IEnumerator<(string Key, CatalogItem Item)>
    {
        public int Available { get; init; } = 100;
        public int MoveCount { get; private set; }
        public bool Disposed { get; private set; }
        public (string Key, CatalogItem Item) Current => ("mounts", new((uint)MoveCount, (uint)MoveCount, "Mount", false));
        object IEnumerator.Current => Current;
        public bool MoveNext() { ObjectDisposedException.ThrowIf(Disposed, this); return ++MoveCount <= Available; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
}
