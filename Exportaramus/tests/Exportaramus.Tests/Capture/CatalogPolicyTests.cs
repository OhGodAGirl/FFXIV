using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class CatalogPolicyTests
{
    [Fact]
    public void BuildNormalizesRowsWithinEachDefinitionAndKeepsStateOut()
    {
        var projection = new CatalogProjection("English", "test-build",
            [new("mounts", "Mounts", DataType.PermanentUnlock, true, []), new("minions", "Minions", DataType.PermanentUnlock, true, [])],
            [("mounts", new(2, 2, "Two", false)), ("mounts", new(0, 0, "Invalid", true)),
                ("mounts", new(1, 1, "One", false)), ("mounts", new(2, 2, "Duplicate", true))], [0, 3, 3, 1]);
        var result = CollectionCatalogBuilderPolicy.Build(projection);
        Assert.Equal("English", result.ClientLanguage); Assert.Equal("test-build", result.GameDataVersion);
        Assert.Equal(new uint[] { 1, 2 }, result.Collections["mounts"].Items.Select(i => i.Id));
        Assert.Equal("Two", result.Collections["mounts"].Items[1].Name); Assert.Empty(result.Collections["minions"].Items);
        Assert.True(result.KnownItemIds.SetEquals([1, 3]));
    }
}
