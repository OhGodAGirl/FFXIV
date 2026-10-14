using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Exportaramus.Bootstrap;

namespace Exportaramus.Tests.Contracts;

public sealed class EmbeddedProductDataTests
{
    [Fact]
    public void DirectDiagnosticParserExplainsIncompatibleMetadata()
    {
        var contract = JsonNode.Parse(EmbeddedProductData.Read("ffxiv-collect-contract.json"))!;
        contract["schemaVersion"] = "2.0";
        var error = Assert.Throws<InvalidDataException>(() => EmbeddedProductData.ParseDiagnosticContract(
            Encoding.UTF8.GetBytes(contract.ToJsonString()), EmbeddedProductData.Read("identifier-mappings.json")));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void EmbeddedDerivedRelationshipsMatchPinnedPublicRowsAndNullableAchievements()
    {
        var derived = EmbeddedProductData.Derived();
        Assert.Equal(1088, derived.Outfits.Length); Assert.Equal(2251, derived.Relics.Length);
        Assert.Equal(new uint[] { 2642, 2965, 3747 }, derived.Outfits.Single(i => i.Id == 45094).RequiredItemIds.ToArray());
        Assert.Equal(129u, derived.Relics.Single(i => i.Id == 1675).AchievementId);
        Assert.Equal(864, derived.Relics.Count(i => i.AchievementId is not null));
        Assert.Contains(derived.Relics, i => i.AchievementId is null);
        Assert.All(derived.Outfits, i => { Assert.Equal(i.Id, i.SourceRowId); Assert.True(i.IsFallbackName); Assert.NotEmpty(i.RequiredItemIds); });
    }

    [Fact]
    public void DiagnosticContractIdentifiesExactEmbeddedBytesWithoutInventingRuntimeEvidence()
    {
        var metadata = EmbeddedProductData.DiagnosticContract();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(EmbeddedProductData.Read("ffxiv-collect-contract.json"))), metadata.ContractSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(EmbeddedProductData.Read("identifier-mappings.json"))), metadata.MappingSha256);
        Assert.Equal("7eda07857e89b7dba81453135b44bc33bf3457b1", metadata.UpstreamCommit);
        Assert.NotEqual(default, metadata.VerifiedAtUtc);
        Assert.Throws<InvalidOperationException>(() => EmbeddedProductData.Read("nonexistent.json"));
    }

    [Fact]
    public void OptionalResourceLoadersReturnVerifiedDataOrExplicitFailureWithoutEmptyOwnership()
    {
        Assert.True(EmbeddedProductData.TryDerived(out var derived, out var derivedError));
        Assert.True(EmbeddedProductData.TryDerived(out _, out _));
        Assert.NotNull(derived); Assert.Null(derivedError);
        Assert.True(EmbeddedProductData.TryDiagnosticContract(out var metadata, out var metadataError));
        Assert.True(EmbeddedProductData.TryDiagnosticContract(out _, out _));
        Assert.NotNull(metadata); Assert.Null(metadataError);
        var contract = EmbeddedProductData.Read("ffxiv-collect-contract.json");
        var mapping = EmbeddedProductData.Read("identifier-mappings.json");
        var recognized = EmbeddedProductData.Read("recognized-ids.json");
        Assert.False(EmbeddedProductData.TryDerived(contract, mapping, recognized, [], out derived, out derivedError));
        Assert.Null(derived); Assert.Equal("Derived collection mappings are invalid.", derivedError);
        Assert.False(EmbeddedProductData.TryDiagnosticContract([], mapping, out metadata, out metadataError));
        Assert.Null(metadata); Assert.Equal("FFXIV Collect diagnostic metadata is invalid.", metadataError);
    }

    [Theory]
    [InlineData("verifiedAtUtc", "2026-09-06T00:00:00")]
    [InlineData("upstreamCommit", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("mappingHash", "wrong")]
    public void DiagnosticMetadataRejectsUnverifiableOrIncompatibleFields(string property, string replacement)
    {
        var root = JsonNode.Parse(EmbeddedProductData.Read("ffxiv-collect-contract.json"))!;
        if (property == "mappingHash") root["artifacts"]!["identifierMappingsSha256"] = replacement;
        else root[property] = replacement;
        Assert.False(EmbeddedProductData.TryDiagnosticContract(Encoding.UTF8.GetBytes(root.ToJsonString()), EmbeddedProductData.Read("identifier-mappings.json"), out var metadata, out var error));
        Assert.Null(metadata); Assert.Equal("FFXIV Collect diagnostic metadata is invalid.", error);
    }
}

