using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.Tests;

public sealed class ConsumerKitV12Tests
{
    private const string Identity = "12345678901234567";
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static string FixtureRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "project", "Exportaramus.csproj"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException(), "tests", "fixtures", "Contracts", "1.2");
        }
    }

    private static string ContractData => Path.Combine(Exportaramus.Tests.Contracts.ConsumerContractTests.Root, "data", "Contracts", "1.2");

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(FixtureRoot, "fixtures", name.Contains("-invalid-", StringComparison.Ordinal) ? "invalid" : "valid", name));
    private static string Edit(string payload, Action<JsonObject> edit)
    {
        var node = JsonNode.Parse(payload)!.AsObject(); edit(node); return node.ToJsonString();
    }

    public static IEnumerable<object[]> Fixtures() => Directory.EnumerateFiles(Path.Combine(FixtureRoot, "fixtures"), "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => new object[] { Path.GetFileName(path), Path.GetFileName(Path.GetDirectoryName(path)) == "valid" });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CompleteVersionedFixturesHaveExpectedAcceptance(string file, bool expected)
    {
        var payload = Fixture(file);
        var actual = file.StartsWith("ffxiv-collect-", StringComparison.Ordinal) ? CollectionDataSemanticValidator.ValidateFFXIVCollect(payload).Success
            : file.StartsWith("capabilities-", StringComparison.Ordinal) ? CollectionDataSemanticValidator.ValidateCapabilities(payload).Success
            : file.StartsWith("snapshot-changed-", StringComparison.Ordinal) ? CollectionDataSemanticValidator.ValidateChanged(payload).Success
            : file.StartsWith("snapshot-withdrawn-", StringComparison.Ordinal) ? CollectionDataSemanticValidator.ValidateWithdrawn(payload).Success
            : file.StartsWith("compendium-", StringComparison.Ordinal) ? CollectionDataSemanticValidator.ValidateRich(payload).Success
            : CollectionDataSemanticValidator.ValidateSnapshot(payload, Identity).Success;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CapabilitiesPinCompactSchema()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(ContractData, "schemas", "collection-data.schema.json"))));
        Assert.Equal(hash, CollectionDataSemanticValidator.CompactSchemaHash);
        Assert.Equal(hash, CollectionDataSemanticValidator.ValidateCapabilities(Fixture("capabilities-valid.json")).Value!.CollectionDataSchema.Sha256);
    }

    [Fact]
    public void BeastMembershipUsesPinnedCatalogAndOmissionIsDistinctFromCompleteEmpty()
    {
        var populated = CollectionDataSemanticValidator.ValidateSnapshot(Fixture("compact-valid-beasts-populated.json"), Identity);
        Assert.True(populated.Success); Assert.Equal(new uint[] { 1, 2, 50 }, populated.Value!.Unlocks["beasts"].UnlockedIds);
        var empty = CollectionDataSemanticValidator.ValidateSnapshot(Fixture("compact-valid-beasts-empty.json"), Identity);
        Assert.True(empty.Success); Assert.Empty(empty.Value!.Unlocks["beasts"].UnlockedIds);
        var unavailable = CollectionDataSemanticValidator.ValidateSnapshot(Fixture("compact-valid-full.json"), Identity);
        Assert.True(unavailable.Success); Assert.False(unavailable.Value!.Unlocks.ContainsKey("beasts"));
        Assert.Equal("UnavailablePendingValidation", unavailable.Value.SourceHealth["beasts"].CapabilityState);
        Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(Fixture("compact-valid-beasts-populated.json"), "1").Success);
        using var resource = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContractData, "resources", "recognized-ids.json")));
        var ids = resource.RootElement.GetProperty("catalogs").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "beasts").GetProperty("ids");
        Assert.Equal(Enumerable.Range(1, 50), ids.EnumerateArray().Select(id => id.GetInt32()));
    }

    [Fact]
    public void VersionOnePointTwoDoesNotAcceptOlderPayloadsOrExposeBeastWriteGates()
    {
        Assert.Equal("Exportaramus.CollectionData.V1_2", CollectionDataGates.Prefix);
        foreach (var version in new[] { "1.0", "1.1", "1.3", "2.0" })
            Assert.Equal(CollectionDataValidationCode.UnsupportedSchemaVersion, CollectionDataSemanticValidator.ValidateSnapshot(Edit(Fixture("compact-valid-full.json"), n => n["schemaVersion"] = version), Identity).Code);
        Assert.Equal(5, typeof(GateNames).GetProperties().Length);
        Assert.Contains("beasts", CollectionDataSemanticValidator.UnlockKeys);
        Assert.DoesNotContain("beasts", CollectionDataSemanticValidator.PresenceKeys);
        Assert.True(CollectionDataSemanticValidator.ValidateFFXIVCollect("{\"beasts\":[1]}").Success);
        Assert.False(Exportaramus.ConsumerKit.V1_1.CollectionDataSemanticValidator.ValidateFFXIVCollect("{\"beasts\":[1]}").Success);
        Assert.False(Exportaramus.ConsumerKit.V1.CollectionDataSemanticValidator.ValidateFFXIVCollect("{\"beasts\":[1]}").Success);
    }

}
