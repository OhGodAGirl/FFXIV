using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Copied native source facts retain exact identity, dye and unlock meaning.</summary>
public sealed class NativeCollectionProjectionTests
{
    /// <summary>Ordinary high-quality identifiers normalize while both stored dyes survive.</summary>
    [Fact]
    public void DresserNormalizesQualityAndPreservesSourceAndDyes()
    {
        var result = new List<NativeCollectionItem>();
        NativeCollectionProjection.AddDresser(result, 1_000_020, 8, 3, 4, null);
        Assert.Equal(new NativeCollectionItem(20, CollectionSource.Dresser, 8, 3, 4), Assert.Single(result));
        NativeCollectionProjection.AddDresser(result, 0, 9, 5, 6, null);
        Assert.Single(result);
    }

    /// <summary>Only unlocked, nonempty set components are projected and set dyes are zero.</summary>
    [Fact]
    public void SetsExpandOnlyUnlockedComponentsWithoutInventingStoredDyes()
    {
        var result = new List<NativeCollectionItem>();
        NativeCollectionProjection.AddDresser(result, 99, 7, 8, 9, [(20, true), (21, false), (0, true), (22, true)]);
        Assert.Equal(new[] { new NativeCollectionItem(20, CollectionSource.Dresser, 7, 0, 0),
            new NativeCollectionItem(22, CollectionSource.Dresser, 7, 0, 0) }, result);
        NativeCollectionProjection.AddDresser(result, 100, 8, 1, 2, []);
        Assert.Equal(2, result.Count);
    }

    /// <summary>Missing cabinet identity or ownership never becomes an available source.</summary>
    [Theory]
    [InlineData(0u, 20u, true)]
    [InlineData(1u, 0u, true)]
    [InlineData(1u, 20u, false)]
    public void ArmoireRejectsMissingFacts(uint row, uint item, bool owned)
    {
        var result = new List<NativeCollectionItem>();
        NativeCollectionProjection.AddArmoire(result, row, item, owned);
        Assert.Empty(result);
    }

    /// <summary>Snapshots have stable source/index/item ordering and preserve readiness independently.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SnapshotOrdersCopiedValuesAndRetainsReadiness(bool dresser, bool armoire)
    {
        var input = new List<NativeCollectionItem>();
        NativeCollectionProjection.AddArmoire(input, 2, 40, true);
        input.Add(new(30, CollectionSource.Dresser, 2, 1, 2));
        input.Add(new(20, CollectionSource.Dresser, 1, 3, 4));
        input.Add(new(10, CollectionSource.Dresser, 1, 5, 6));

        var snapshot = NativeCollectionProjection.Complete(input, dresser, armoire, TestDomainValues.Timestamp);

        Assert.Equal(new uint[] { 10, 20, 30, 40 }, snapshot.Items.Select(item => item.ItemId));
        Assert.Equal(dresser, snapshot.IsDresserLoaded);
        Assert.Equal(armoire, snapshot.IsArmoireLoaded);
        Assert.Equal(TestDomainValues.Timestamp, snapshot.CapturedAtUtc);
        input.Clear();
        Assert.Equal(4, snapshot.Items.Count);
    }
}

