using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationBoundaryReviewTests
{
    [Fact]
    public void InvalidUnicodeReporterDoesNotReturnAReplacementIdentity()
    {
        Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, " Reporter ", out var character, out var normalized));
        Assert.Equal(123ul, character);
        Assert.Equal("Reporter", normalized);
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, "\ud800", out _, out normalized));
        Assert.Empty(normalized);
    }

    [Fact]
    public void InvalidItemDoesNotReturnAReporterIdentity()
    {
        Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, "Reporter", out _, out var normalized));
        Assert.Equal("Reporter", normalized);
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 0, "Reporter", out _, out normalized));
        Assert.Empty(normalized);
    }

    [Fact]
    public void DresserPresenceUsesMappedItemIdentity()
    {
        var data = SnapshotFixture.Create();
        Assert.Single(data.Collections["glamour_dresser"].Items);
        var original = data.Catalog.Collections["glamour_dresser"];
        var mapped = original with { Items = [new CatalogItem(900, 900, "Mapped item", false, 100)] };
        var catalog = data.Catalog with { Collections = data.Catalog.Collections.SetItem("glamour_dresser", mapped) };
        var assembled = CollectionDataAssembler.Assemble(catalog, data.State);
        var item = Assert.Single(assembled.Collections["glamour_dresser"].Items);
        Assert.Equal(900u, item.Id);
        Assert.True(item.Owned);
    }
}
