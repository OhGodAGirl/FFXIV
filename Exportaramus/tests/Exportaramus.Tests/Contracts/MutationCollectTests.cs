using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Contracts;

public sealed class MutationCollectTests
{
    private sealed class Documents
    {
        public readonly JsonObject Contract = Read("ffxiv-collect-contract.json");
        public readonly JsonObject Mapping = Read("identifier-mappings.json");
        public readonly JsonObject Recognized = Read("recognized-ids.json");
        public readonly JsonObject Derived = Read("derived-inputs.json");
        private static JsonObject Read(string name) => JsonNode.Parse(EmbeddedProductData.Read(name))!.AsObject();
        private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
        public void SealCatalog(string key)
        {
            var catalog = Recognized["catalogs"]!.AsArray().Single(c => c!["key"]!.GetValue<string>() == key)!;
            var ids = catalog["ids"]!.AsArray().Select(id => id!.GetValue<uint>()).ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', ids))));
            catalog["idsSha256"] = hash; catalog["count"] = ids.Length;
            var manifest = Contract["catalogs"]!.AsArray().Single(c => c!["key"]!.GetValue<string>() == key)!;
            manifest["idsSha256"] = hash; manifest["count"] = ids.Length;
        }
        public FFXIVCollectContract Load()
        {
            var mapping = Bytes(Mapping); var recognized = Bytes(Recognized); var derived = Bytes(Derived);
            Contract["artifacts"]!["identifierMappingsSha256"] = Convert.ToHexStringLower(SHA256.HashData(mapping));
            Contract["artifacts"]!["recognizedIdsSha256"] = Convert.ToHexStringLower(SHA256.HashData(recognized));
            Contract["artifacts"]!["derivedInputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(derived));
            return FFXIVCollectContract.Load(Bytes(Contract), mapping, recognized, derived);
        }
        public void SealRelationships(string key)
        {
            var rows = Derived[key]!.AsArray();
            var normalized = key == "outfits"
                ? string.Join('\n', rows.Select(row => row!["id"]!.GetValue<uint>() + ":" + string.Join(',', row["requiredItemIds"]!.AsArray().Select(id => id!.GetValue<uint>()))))
                : string.Join('\n', rows.Select(row => row!["id"]!.GetValue<uint>() + ":" + (row["achievementId"]?.GetValue<uint>() ?? 0u)));
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            Contract["relationshipHashes"]![key + "Sha256"] = hash;
            Derived["relationshipHashes"]![key + "Sha256"] = hash;
        }
    }

    [Fact]
    public void CoherentMetadataAcceptsExactLengthBoundaryAndRejectsOneBeyond()
    {
        foreach (var length in new[] { 128, 129 })
        {
            var documents = new Documents(); var version = new string('v', length);
            documents.Contract["validatedGameDataVersion"] = version;
            documents.Mapping["validatedGameDataVersion"] = version;
            documents.Derived["validatedGameDataVersion"] = version;
            if (length == 128) Assert.Equal(version, documents.Load().GameDataVersion);
            else Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<InvalidDataException>(() => documents.Load()).Message));
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void CoherentResealedMetadataStillRequiresANonblankGameVersion(string version)
    {
        var documents = new Documents();
        Assert.NotNull(documents.Load());
        documents.Contract["validatedGameDataVersion"] = version;
        documents.Mapping["validatedGameDataVersion"] = version;
        documents.Derived["validatedGameDataVersion"] = version;
        Assert.Equal("Embedded metadata is invalid.", Assert.Throws<InvalidDataException>(() => documents.Load()).Message);
    }

    [Theory]
    [InlineData("outfits")]
    [InlineData("relics")]
    public void ResealedRelationshipHashesCannotAuthorizeAnUnrecognizedDerivedIdentity(string key)
    {
        var documents = new Documents();
        var approved = documents.Load();
        var rows = documents.Derived[key]!.AsArray();
        var unrecognized = approved.RecognizedIds[key].Max() + 1u;
        Assert.DoesNotContain(unrecognized, approved.RecognizedIds[key]);
        rows[^1]!["id"] = unrecognized;
        documents.SealRelationships(key);
        using var derived = JsonDocument.Parse(documents.Derived.ToJsonString());
        Assert.True(Exportaramus.ConsumerKit.V1.ContractSchemaRegistry.Default.Validate(derived.RootElement, "derived-inputs.schema.json"));
        Assert.Equal("Embedded derived catalog mismatch.", Assert.Throws<InvalidDataException>(() => documents.Load()).Message);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("descending")]
    [InlineData("duplicate")]
    public void CatalogIdentityRulesRemainMandatoryEvenWhenArtifactAndCatalogHashesAgree(string defect)
    {
        var documents = new Documents();
        var catalog = documents.Recognized["catalogs"]!.AsArray().Single(c => c!["key"]!.GetValue<string>() == "bardings")!;
        var ids = catalog["ids"]!.AsArray();
        if (defect == "zero") ids[0] = 0u;
        else if (defect == "duplicate") ids[1] = ids[0]!.DeepClone();
        else { var first = ids[0]!.GetValue<uint>(); ids[0] = ids[1]!.GetValue<uint>(); ids[1] = first; }
        documents.SealCatalog("bardings");
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<InvalidDataException>(() => documents.Load()).Message));
    }

    [Fact]
    public void DerivedCatalogFallbackNamesRemainExplicitAndDoNotClaimLocalizedNames()
    {
        var derived = EmbeddedProductData.Derived();
        Assert.All(derived.Outfits, row => { Assert.Equal("Outfit", row.Name); Assert.True(row.IsFallbackName); });
        Assert.All(derived.Relics, row => { Assert.Equal("Relic", row.Name); Assert.True(row.IsFallbackName); });
    }

    [Fact]
    public void OneUnsafeStaleFactOmitsTheWholeCollectionEvenBesideAcceptableFacts()
    {
        var contract = FFXIVCollectContract.LoadEmbedded(); var data = ProductCollectTests.CompleteProjection(contract);
        var collection = data.Collections["outfits"];
        var first = collection.Items[0];
        var mixed = first with
        {
            OwnershipEvidence = first.OwnershipEvidence with
            {
                Facts = [
            new(DataType.CurrentItemPresence, "armoire", 100, "Present", SourceStatus.Stale),
            new(DataType.CurrentItemPresence, "inventory", 100, "Present", SourceStatus.Stale)]
            }
        };
        var output = FFXIVCollectImportSerializer.Serialize(data with { Collections = data.Collections.SetItem("outfits", collection with { Items = collection.Items.SetItem(0, mixed) }) }, contract);
        Assert.True(output.CanCopy); Assert.Contains("outfits", output.OmittedKeys);
        using var document = JsonDocument.Parse(output.Json!); Assert.False(document.RootElement.TryGetProperty("outfits", out _));
    }
}
