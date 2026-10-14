using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;
namespace Exportaramus.Tests;
public sealed class CatalogRefreshTests
{
    [Fact]
    public void ReceiptBindsAllAdditionsAndRejectsModifiedBytes()
    {
        var bytes = EmbeddedProductData.Read("catalog-refresh.json");
        var refresh = CatalogRefreshContract.Load(bytes);
        Assert.Equal(52, refresh.Additions.Sum(x => x.Value.Count));
        Assert.Equal(new uint[] { 52428, 52429, 52430, 52431, 52432 }, refresh.Outfits.Single(x => x.Id == 52601).RequiredItemIds);
        var changed = bytes.ToArray(); changed[0] ^= 1;
        Assert.Throws<InvalidDataException>(() => CatalogRefreshContract.Load(changed));
    }
    [Fact]
    public void EmbeddedRefreshIncludesAll52WhileOriginalResourcesRemainBaseline()
    {
        var contract = FFXIVCollectContract.LoadEmbedded();
        Assert.Equal(CatalogRefreshContract.GameDataVersion, contract.GameDataVersion);
        foreach (var pair in CatalogRefreshContract.LoadEmbedded().Additions)
            Assert.True(pair.Value.IsSubsetOf(contract.RecognizedIds[pair.Key]));
        Assert.Equal(1088, EmbeddedProductData.Derived().Outfits.Length);
        using var original = JsonDocument.Parse(EmbeddedProductData.Read("derived-inputs.json"));
        Assert.Equal(1086, original.RootElement.GetProperty("outfits").GetArrayLength());
    }
    [Fact]
    public void NewOutfitIsInternalButExistingRichSchemasPreservePinnedDomain()
    {
        var data = SnapshotFixture.Create();
        var old = data.Collections["outfits"];
        var item = new CollectionItem(52602, "Beastmaster's Attire", true, new("AllRequiredConstituents",
            [.. new uint[] { 50744, 50745, 50746, 50747, 50748 }.Select(id => new OwnershipFact(DataType.CurrentItemPresence, "armoire", id, "Present", SourceStatus.Fresh))]));
        data = data with { Collections = data.Collections.SetItem("outfits", old with { Items = [.. old.Items, item] }) };
        var legacy = CollectionDataLegacyProjection.Data(data);
        Assert.DoesNotContain(legacy.Collections["outfits"].Items, x => x.Id == 52602);
        Assert.Contains(data.Collections["outfits"].Items, x => x.Id == 52602);
        Assert.DoesNotContain(CollectionDataLegacyProjection.DataV11(data).Collections["outfits"].Items, x => x.Id == 52602);
        var armoire = data.State.CurrentItemPresence["armoire"];
        data = data with { State = data.State with { CurrentItemPresence = data.State.CurrentItemPresence.SetItem("armoire", armoire with { Items = [.. armoire.Items, .. new uint[] { 50744, 50745, 50746, 50747, 50748 }.Select(id => new PresenceItem(id, null))] }) } };
        var rich = CollectionDataSerializer.RichV12(data, "Test", SnapshotFixture.Now);
        Assert.True(Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator.ValidateRich(rich).Success);
        using var projected = JsonDocument.Parse(rich);
        Assert.DoesNotContain(projected.RootElement.GetProperty("collections").GetProperty("outfits").GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetUInt32() == 52602);
    }
    [Fact]
    public void AllNewIdsExportEvenThoughExistingRichOutfitDomainsRemainFrozen()
    {
        var contract = FFXIVCollectContract.LoadEmbedded();
        var data = Exportaramus.Tests.Contracts.ProductCollectTests.CompleteProjection(contract);
        data = data with { Collections = data.Collections.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value with { Items = [.. pair.Value.Items.Select(item => item with { Owned = true })] }) };
        var export = FFXIVCollectImportSerializer.Serialize(data, contract);
        Assert.True(export.CanCopy);
        using var json = JsonDocument.Parse(export.Json!);
        foreach (var pair in CatalogRefreshContract.LoadEmbedded().Additions)
            Assert.True(pair.Value.IsSubsetOf(json.RootElement.GetProperty(pair.Key).EnumerateArray().Select(x => x.GetUInt32())));
        Assert.DoesNotContain(CollectionDataLegacyProjection.RichV12(data).Collections["outfits"].Items, x => x.Id is 52601 or 52602);
        Assert.Contains(data.Collections["outfits"].Items, x => x.Id == 52602);
    }}




