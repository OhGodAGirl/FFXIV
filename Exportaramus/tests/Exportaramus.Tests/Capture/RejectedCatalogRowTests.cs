using Exportaramus.Features.CollectionData.Catalog;

namespace Exportaramus.Tests.Capture;

public sealed class RejectedCatalogRowTests
{
    [Fact]
    public void RejectedRowHasNoPublishableIdentityOrDisplayName()
    {
        var valid = CatalogRowPolicy.Project(new("mounts", 1, "Mount", Icon: 1));
        Assert.Equal("mounts", valid.Key);
        Assert.Equal(1u, valid.Item.Id);
        Assert.Equal("Mount", valid.Item.Name);

        var rejected = CatalogRowPolicy.Project(new("mounts", 0, "Mount", Icon: 1));
        Assert.Empty(rejected.Key);
        Assert.Equal(0u, rejected.Item.Id);
        Assert.Equal(0u, rejected.Item.SourceRowId);
        Assert.Empty(rejected.Item.Name);
        Assert.False(rejected.Item.IsFallbackName);
    }
}

