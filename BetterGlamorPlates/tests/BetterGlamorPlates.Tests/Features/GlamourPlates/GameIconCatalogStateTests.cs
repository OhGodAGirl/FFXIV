using BetterGlamorPlates.Adapters;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Exercises production decisions with controlled inputs and observable assertions.</summary>
public sealed class GameIconCatalogStateTests
{
    private static readonly uint[] ExpectedIds = [2, 3, 1, 8];
    private static readonly string[] ExpectedCategories = ["Accessories", "Armour", "Other", "Weapons"];
    /// <summary>Disposal releases cached strings even while the disposed catalog remains referenced.</summary>
    [Fact]
    public void DisposalReleasesCatalogOwnedNames()
    {
        var (catalog, name) = CreateDisposedCatalog();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(name.IsAlive);
        GC.KeepAlive(catalog);
    }

    private static IEnumerable<GameIconFact> Drain(Queue<GameIconFact> input)
    {
        while (input.TryDequeue(out var item)) yield return item;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (GameIconCatalogState Catalog, WeakReference Name) CreateDisposedCatalog()
    {
        var name = new string('x', 100);
        var weak = new WeakReference(name);
        var input = new Queue<GameIconFact>();
        input.Enqueue(new GameIconFact(1, name, false, false, false));
        var catalog = new GameIconCatalogState(() => Drain(input).GetEnumerator());
        catalog.Advance();
        catalog.Dispose();
        return (catalog, weak);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public void EnumerationIsLazyAndPublishesOnlyTheCompletedSortedDeduplicatedCatalog()
    {
        var opened = 0;
        var facts = new[] { new GameIconFact(8, "z", true, true, true), new GameIconFact(3, "A", false, true, true),
            new GameIconFact(2, "a", false, false, true), new GameIconFact(1, "other", false, false, false),
            new GameIconFact(8, "duplicate", false, false, false), new GameIconFact(0, "zero", false, false, false),
            new GameIconFact(9, "", false, false, false) };
        using var catalog = new GameIconCatalogState(() => { opened++; return ((IEnumerable<GameIconFact>)facts).GetEnumerator(); });
        Assert.Equal(0, opened);
        Assert.False(catalog.Complete);
        Assert.Empty(catalog.Icons);

        catalog.Advance();
        catalog.Advance();

        Assert.Equal(1, opened);
        Assert.True(catalog.Complete);
        Assert.Equal(ExpectedIds, catalog.Icons.Select(x => x.Id));
        Assert.Equal(ExpectedCategories, catalog.Icons.Select(x => x.Category));
        Assert.Equal("z", catalog.Icons[3].Name);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Theory]
    [InlineData(false, 100000, 1)]
    [InlineData(true, 65536, 65536)]
    public void WorkAndStorageAreBounded(bool unique, int expectedRead, int expectedIcons)
    {
        var read = 0;
        var released = 0;
        IEnumerable<GameIconFact> Rows()
        {
            try { while (true) { read++; yield return new GameIconFact(unique ? (uint)read : 1, "Item", false, false, false); } }
            finally { released++; }
        }
        using var catalog = new GameIconCatalogState(() => Rows().GetEnumerator());

        catalog.Advance();
        Assert.Equal(256, read);
        Assert.False(catalog.Complete);
        Assert.Empty(catalog.Icons);
        for (var update = 0; update < 400 && !catalog.Complete; update++) catalog.Advance();
        Assert.True(catalog.Complete);

        Assert.Equal(expectedRead, read);
        Assert.Equal(expectedIcons, catalog.Icons.Count);
        Assert.Equal(1, released);
        catalog.Dispose();
        Assert.Equal(1, released);
        Assert.Empty(catalog.Icons);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public void DisposeBeforeOrDuringEnumerationNeverReopensOrLeaksTheEnumerator()
    {
        var opened = 0;
        var released = 0;
        IEnumerable<GameIconFact> Rows()
        {
            try { while (true) yield return new GameIconFact(1, "A", false, false, false); }
            finally { released++; }
        }
        var unopened = new GameIconCatalogState(() => { opened++; return Rows().GetEnumerator(); });
        unopened.Dispose();
        unopened.Advance();
        Assert.Equal(0, opened);
        Assert.True(unopened.Complete);
        var active = new GameIconCatalogState(() => Rows().GetEnumerator());
        active.Advance();

        active.Dispose();
        active.Dispose();
        active.Advance();

        Assert.Equal(1, released);
        Assert.True(active.Complete);
        Assert.Empty(active.Icons);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Theory]
    [InlineData(0u, false, false, null)]
    [InlineData(1000000u, true, false, null)]
    [InlineData(1u, true, true, "ui/icon/000000/000001.tex")]
    [InlineData(999u, false, false, "ui/icon/000000/000999.tex")]
    [InlineData(1000u, true, true, "ui/icon/001000/001000.tex")]
    [InlineData(999999u, true, true, "ui/icon/999000/999999.tex")]
    public void ValidationBoundsIdsAndMapsExactTexturePaths(uint id, bool exists, bool expected, string? expectedPath)
    {
        string? path = null;

        var valid = GameIconCatalogState.IsValid(id, candidate => { path = candidate; return exists; });

        Assert.Equal(expected, valid);
        Assert.Equal(expectedPath, path);
    }

    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    [InlineData(257, false)]
    public void ExactBatchBoundaryPublishesOnlyAfterEndOfInput(int count, bool completedAfterFirst)
    {
        using var catalog = new GameIconCatalogState(() => Enumerable.Range(1, count)
            .Select(id => new GameIconFact((uint)id, "Item", false, false, false)).GetEnumerator());

        catalog.Advance();

        Assert.Equal(completedAfterFirst, catalog.Complete);
        Assert.Equal(completedAfterFirst ? count : 0, catalog.Icons.Count);
        catalog.Advance();
        Assert.True(catalog.Complete);
        Assert.Equal(count, catalog.Icons.Count);
    }
    /// <summary>Verifies the behavior named by this regression with deterministic inputs.</summary>
    [Fact]
    public void EmptyCatalogCompletesAndDoesNotReopen()
    {
        var opened = 0;
        using var catalog = new GameIconCatalogState(() => { opened++; return Enumerable.Empty<GameIconFact>().GetEnumerator(); });
        catalog.Advance();
        catalog.Advance();
        Assert.True(catalog.Complete);
        Assert.Empty(catalog.Icons);
        Assert.Equal(1, opened);
    }
}






