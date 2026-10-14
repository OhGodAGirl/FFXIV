using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Contracts;

public sealed class EmbeddedCollectContractTests
{
    private static void AssertRejected(Action action)
    {
        var failure = Assert.Throws<InvalidDataException>(action);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
    }
    private static (JsonObject Contract, JsonObject Mapping, JsonObject Recognized, JsonObject Derived) Inputs() =>
        (JsonNode.Parse(EmbeddedProductData.Read("ffxiv-collect-contract.json"))!.AsObject(), JsonNode.Parse(EmbeddedProductData.Read("identifier-mappings.json"))!.AsObject(), JsonNode.Parse(EmbeddedProductData.Read("recognized-ids.json"))!.AsObject(), JsonNode.Parse(EmbeddedProductData.Read("derived-inputs.json"))!.AsObject());

    private static FFXIVCollectContract Load((JsonObject Contract, JsonObject Mapping, JsonObject Recognized, JsonObject Derived) input, bool reseal = true)
    {
        var mapping = Encoding.UTF8.GetBytes(input.Mapping.ToJsonString()); var recognized = Encoding.UTF8.GetBytes(input.Recognized.ToJsonString()); var derived = Encoding.UTF8.GetBytes(input.Derived.ToJsonString());
        if (reseal)
        {
            input.Contract["artifacts"]!["identifierMappingsSha256"] = Convert.ToHexStringLower(SHA256.HashData(mapping));
            input.Contract["artifacts"]!["recognizedIdsSha256"] = Convert.ToHexStringLower(SHA256.HashData(recognized));
            input.Contract["artifacts"]!["derivedInputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(derived));
        }
        return FFXIVCollectContract.Load(Encoding.UTF8.GetBytes(input.Contract.ToJsonString()), mapping, recognized, derived);
    }

    [Fact]
    public void ExactEmbeddedContractAndNonthrowingBootstrapReadSucceed()
    {
        Assert.True(FFXIVCollectContract.TryLoadEmbedded(out var contract, out var error)); Assert.Null(error); Assert.Equal(15, contract!.RecognizedIds.Count);
        Assert.True(FFXIVCollectContract.TryLoadEmbedded(out _, out _));
        Assert.Equal(15, Load(Inputs()).RecognizedIds.Count);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => FFXIVCollectContract.Load([0], [], [], []));
        Assert.False(FFXIVCollectContract.TryLoad([0], [], [], [], out var invalid, out var failure)); Assert.Null(invalid); Assert.Equal("FFXIV Collect mapping contract is invalid.", failure);
        AssertRejected(() => Load(Inputs(), false));
    }

    [Fact]
    public void RootContractFieldsAndArtifactSourceMetadataAreMandatory()
    {
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["schemaVersion"] = "2.0", n => n["importUtf8ByteLimit"] = 1048577,
            n => n["orderedKeys"]!.AsArray().RemoveAt(0), n => n["validatedGameDataVersion"] = "",
            n => n["validatedGameDataVersion"] = new string('a', 129), n => n["format"] = "invalid",
            n => n["upstreamRepository"] = "https://invalid.example/", n => n["upstreamCommit"] = new string('0', 40),
        }) { var input = Inputs(); edit(input.Contract); AssertRejected(() => Load(input)); }
        foreach (var target in new[] { "mapping", "recognized", "derived" })
        {
            var input = Inputs(); var document = target == "mapping" ? input.Mapping : target == "recognized" ? input.Recognized : input.Derived;
            document["schemaVersion"] = "2.0"; AssertRejected(() => Load(input));
        }
        var wrongMap = Inputs(); wrongMap.Mapping["validatedGameDataVersion"] = "other"; AssertRejected(() => Load(wrongMap));
        var wrongDerived = Inputs(); wrongDerived.Derived["validatedGameDataVersion"] = "other"; AssertRejected(() => Load(wrongDerived));
        var omitted = Inputs(); omitted.Mapping["entries"]!.AsArray().RemoveAt(0); AssertRejected(() => Load(omitted));
    }

    [Fact]
    public void RecognizedCatalogCoverageCountsOrderBoundsAndHashAreVerified()
    {
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["catalogs"]!.AsArray().RemoveAt(0),
            n => n["catalogs"]![0]!["count"] = 1,
            n => n["catalogs"]![0]!["ids"]![0] = 0,
            n => n["catalogs"]![0]!["ids"]![0] = 2,
            n => n["catalogs"]![0]!["idsSha256"] = new string('0', 64),
        }) { var input = Inputs(); edit(input.Recognized); AssertRejected(() => Load(input)); }
        var count = Inputs(); count.Contract["catalogs"]![0]!["count"] = 1; AssertRejected(() => Load(count));
        var hash = Inputs(); hash.Contract["catalogs"]![0]!["idsSha256"] = new string('0', 64); AssertRejected(() => Load(hash));
    }

    [Fact]
    public void DerivedIdentifiersAndBothNormalizedRelationshipHashesAreVerified()
    {
        var id = Inputs(); id.Derived["outfits"]![0]!["id"] = 1; AssertRejected(() => Load(id));
        var relation = Inputs(); relation.Derived["outfits"]![0]!["requiredItemIds"]![0] = 1; AssertRejected(() => Load(relation));
        var rootHash = Inputs(); rootHash.Contract["relationshipHashes"]!["outfitsSha256"] = new string('0', 64); AssertRejected(() => Load(rootHash));
        var derivedHash = Inputs(); derivedHash.Derived["relationshipHashes"]!["relicsSha256"] = new string('0', 64); AssertRejected(() => Load(derivedHash));
        var structure = Inputs(); structure.Derived["outfits"]![0]!["unexpected"] = true; AssertRejected(() => Load(structure));
    }
}
