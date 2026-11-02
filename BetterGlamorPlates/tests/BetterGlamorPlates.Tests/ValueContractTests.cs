using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests;

/// <summary>Executable model helpers retain their validation and identity contracts.</summary>
public sealed class ValueContractTests
{
    /// <summary>Every projected source is mapped to its exact named field.</summary>
    [Fact]
    public void SourceMapPreservesEachIdentity()
    {
        var snapshot = TestDomainValues.SharedSnapshot();
        CollectionSourceSnapshot[] sources = [snapshot.Inventory, snapshot.ArmoryChest, snapshot.ChocoboSaddlebag,
            snapshot.PremiumSaddlebag, snapshot.Retainers, snapshot.Armoire, snapshot.GlamourDresser];
        foreach (var source in Enum.GetValues<CollectionSourceName>()) Assert.Same(sources[(int)source], snapshot.GetSource(source));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetSource((CollectionSourceName)999));
    }

    /// <summary>Acceptance requires both a usable snapshot and no failure reason.</summary>
    [Fact]
    public void ParseResultCannotAcceptFailureOrMissingSnapshot()
    {
        var snapshot = TestDomainValues.SharedSnapshot();
        Assert.True(CollectionDataParseResult.Accept(snapshot).Accepted);
        Assert.False(new CollectionDataParseResult(null, CollectionDataFailure.None).Accepted);
        Assert.False(new CollectionDataParseResult(snapshot, CollectionDataFailure.NoCharacter).Accepted);
        Assert.False(CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable).Accepted);
        Assert.Throws<ArgumentNullException>(() => CollectionDataParseResult.Accept(null!));
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => CollectionDataParseResult.Reject(CollectionDataFailure.None));
        Assert.Contains("A rejected result requires a failure reason.", failure.Message);
    }

    /// <summary>Collection identity includes source and native completion requires zero failures.</summary>
    [Fact]
    public void CollectionKeysAndCompletionRetainMeaning()
    {
        Assert.Equal("Dresser:20", TestDomainValues.Item(20, "A").StableKey);
        Assert.Equal("Armoire:20", TestDomainValues.Item(20, "A", CollectionSource.Armoire).StableKey);
        Assert.True(new PlateApplyResult(0, 0, []).IsComplete);
        Assert.False(new PlateApplyResult(1, 0, [new(PlateApplyFailureKind.EditorClosed, null, 0)]).IsComplete);
    }
}
