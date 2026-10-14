using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Exportaramus.Bootstrap;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Publication;
namespace Exportaramus.Tests;

public sealed class BeastsPolicyRegressionTests
{
    [Fact]
    public void FailedReadMustNotConsumeTheFirstCompleteEmptyObservation()
    {
        var policy = new BeastReadinessPolicy();
        Assert.False(policy.Poll(true, true, () => throw new InvalidOperationException(), 0));
        Assert.True(policy.Poll(true, true, () => (true, 0), 1));
        Assert.False(policy.Poll(true, true, () => (true, 0), 2));
    }

    [Fact]
    public void VersionBoundaryAllowsExactly128Characters()
    {
        var json = Contract();
        json["validatedGameDataVersion"] = new string('v', 128);
        Assert.Equal(new string('v', 128), BeastsExportContract.Load(Encoding.UTF8.GetBytes(json.ToJsonString())).GameDataVersion);
    }

    [Fact]
    public void CorrectChecksumCannotLegitimizeZeroAlongsideRealBeastIds()
    {
        var json = Contract();
        json["ids"] = new JsonArray(0, 1);
        json["idsSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("0,1")));
        var error = Assert.Throws<InvalidDataException>(() => BeastsExportContract.Load(Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Equal("Beasts export contract is invalid.", error.Message);
    }

    [Fact]
    public void CorruptedRefreshReceiptProducesAnActionableDiagnostic()
    {
        var changed = EmbeddedProductData.Read("catalog-refresh.json").Concat(new byte[] { 32 }).ToArray();
        var error = Assert.Throws<InvalidDataException>(() => CatalogRefreshContract.Load(changed));
        Assert.Equal("Catalog refresh receipt mismatch.", error.Message);
    }

    private static JsonObject Contract() => new()
    {
        ["schemaVersion"] = "1.2", ["key"] = "beasts",
        ["upstreamCommit"] = "30d92ee4ea0bbbd678fff5c4279a41f7724b357d",
        ["importerPath"] = "app/controllers/import_controller.rb",
        ["validatedGameDataVersion"] = "2026.09.01.0000.0000",
        ["ids"] = new JsonArray(1, 2),
        ["idsSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("1,2"))),
        ["mappingValidated"] = true,
    };
}
