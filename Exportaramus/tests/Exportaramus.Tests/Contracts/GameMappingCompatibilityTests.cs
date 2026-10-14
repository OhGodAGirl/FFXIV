using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Contracts;

public sealed class GameMappingCompatibilityTests
{
    [Theory]
    [InlineData("2026.08.11.0000.0000", "2026.09.01.0000.0000", true)]
    [InlineData("2026.08.11.0000.0000", "2026.08.11.0000.0000", true)]
    [InlineData("2026.08.11.0000.0000", "2026.09.01.0000.0001", false)]
    [InlineData("2026.08.11.0000.0000", "2026.09.01.0000.0000 ", false)]
    [InlineData("unreviewed", "2026.09.01.0000.0000", false)]
    [InlineData("2026.08.11.0000.0000", "2026.10.06.0000.0000", true)]
    [InlineData("2026.09.01.0000.0000", "2026.10.06.0000.0000", true)]
    [InlineData("2026.09.01.0000.0000", "2026.10.06.0000.0001", false)]
    [InlineData("2026.09.01.0000.0000", "2026.10.06.0000.0000 ", false)]
    [InlineData("2026.09.01.0000.0001", "2026.10.06.0000.0000", false)]
    [InlineData("unreviewed", "2026.10.06.0000.0000", false)]
    public void OnlyTheReviewedMappingBaselineCanBeReused(string baseline, string current, bool expected)
    {
        var contract = new FFXIVCollectContract(baseline, ImmutableDictionary<string, ImmutableHashSet<uint>>.Empty);
        Assert.Equal(expected, contract.SupportsGameVersion(current));
    }

    [Theory]
    [InlineData("2026.10.06.0000.0000", true)]
    [InlineData("2026.10.06.0000.0001", false)]
    [InlineData("2026.10.06.0000.0000 ", false)]
    public void EmbeddedRefreshedAndBeastMappingsRemainIndependentlyVersionGuarded(string version, bool expected)
    {
        var contract = FFXIVCollectContract.LoadEmbedded();
        Assert.Equal("2026.09.01.0000.0000", contract.GameDataVersion);
        Assert.Equal(expected, contract.SupportsGameVersion(version));
        Assert.Equal(expected, contract.Beasts!.SupportsGameVersion(version));
        Assert.False((contract.Beasts with { MappingValidated = false }).SupportsGameVersion(version));
        Assert.False((contract.Beasts with { GameDataVersion = "unreviewed" }).SupportsGameVersion(version));
    }
}
