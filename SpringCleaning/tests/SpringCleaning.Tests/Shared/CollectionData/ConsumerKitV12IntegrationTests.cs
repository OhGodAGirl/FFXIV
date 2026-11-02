using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;
using ProviderGates = Exportaramus.ConsumerKit.V1_2.CollectionDataGates;
using ProviderValidator = Exportaramus.ConsumerKit.V1_2.CollectionDataSemanticValidator;

namespace SpringCleaning.Tests.CollectionData;

public sealed class ConsumerKitV12IntegrationTests
{
    private const ulong FixtureCharacter = 12345678901234567;

    public static IEnumerable<object[]> CompactFixtures()
    {
        foreach (var (folder, accepted) in new[] { ("valid", true), ("invalid", false) })
        {
            foreach (var path in Directory.GetFiles(FixtureRoot(folder), "compact-*.json").Order())
                yield return [folder, Path.GetFileName(path), accepted];
        }
    }

    [Theory]
    [MemberData(nameof(CompactFixtures))]
    public void CurrentProviderFixturesPassThroughTheOwnedProjection(string folder, string name, bool accepted)
    {
        var payload = File.ReadAllText(Path.Combine(FixtureRoot(folder), name));

        var result = CollectionDataSnapshotParser.Parse(payload, FixtureCharacter, ProviderFixture.Now);

        Assert.Equal(accepted, result.Accepted);
        if (!accepted)
            Assert.Null(result.Snapshot);
    }

    [Fact]
    public void HandshakeUsesMatchingV12EndpointsAndSchema()
    {
        var capabilities = ProviderValidator.ValidateCapabilities(ProviderFixture.Capabilities());

        Assert.True(capabilities.Success);
        Assert.Equal("1.2", capabilities.Value!.SchemaVersion);
        Assert.Equal("Exportaramus.CollectionData.V1_2.GetCapabilities", ProviderGates.GetCapabilities);
        Assert.Equal("Exportaramus.CollectionData.V1_2.GetSnapshot", ProviderGates.GetSnapshot);
        Assert.Equal(ProviderGates.GetCapabilities, capabilities.Value.Gates.GetCapabilities);
        Assert.Equal(ProviderGates.GetSnapshot, capabilities.Value.Gates.GetSnapshot);
        Assert.Equal(ProviderGates.SnapshotChanged, capabilities.Value.Gates.SnapshotChanged);
        Assert.Equal(ProviderGates.SnapshotWithdrawn, capabilities.Value.Gates.SnapshotWithdrawn);
        Assert.Equal(ProviderGates.ReportOwnershipEvidence, capabilities.Value.Gates.ReportOwnershipEvidence);
        Assert.Equal(ProviderValidator.CompactSchemaHash, capabilities.Value.CollectionDataSchema.Sha256);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    [InlineData("1.3")]
    public void OtherContractVersionsCannotPublishOrFallBack(string version)
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var capabilities = JsonNode.Parse(transport.Caps)!;
        capabilities["schemaVersion"] = version;
        transport.Caps = capabilities.ToJsonString();
        var snapshot = ProviderFixture.Snapshot();
        snapshot["schemaVersion"] = version;

        var handshake = client.Refresh(42, ProviderFixture.Now);
        var parsed = CollectionDataSnapshotParser.Parse(snapshot.ToJsonString(), 42, ProviderFixture.Now);

        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, handshake.Failure);
        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, parsed.Failure);
        Assert.Null(client.Latest);
    }

    [Theory]
    [InlineData("capabilities-invalid-beasts-key-missing.json")]
    [InlineData("capabilities-invalid-beasts-missing.json")]
    [InlineData("capabilities-invalid-beasts-quantity.json")]
    [InlineData("capabilities-invalid-gate.json")]
    [InlineData("capabilities-invalid-hash.json")]
    [InlineData("capabilities-invalid-version.json")]
    public void InvalidCapabilitiesRevokeActionableState(string name)
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.Caps = File.ReadAllText(Path.Combine(FixtureRoot("invalid"), name));

        var result = client.Refresh(42, ProviderFixture.Now);

        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, result.Failure);
        Assert.Null(client.Latest);
        Assert.Equal(CollectionOwnershipFact.Unknown,
            CollectionOwnershipPolicy.GetFact(client.Latest, CollectionSourceName.GlamourDresser, 100));
    }

    [Theory]
    [InlineData("compact-valid-beasts-empty.json")]
    [InlineData("compact-valid-beasts-populated.json")]
    [InlineData("compact-valid-beasts-degraded.json")]
    [InlineData("compact-valid-beasts-stale-observed.json")]
    [InlineData("compact-valid-beasts-stale-restored.json")]
    [InlineData("compact-valid-beasts-unknown-field.json")]
    public void BeastCollectionStateDoesNotChangeGlamourStorageEvidence(string name)
    {
        var baseline = ParseWithInventory(ProviderFixture.Snapshot());
        var result = ParseWithInventory(ProviderFixture.Snapshot(name));

        Assert.Equal(baseline.Inventory.ItemIds.ToArray(), result.Inventory.ItemIds.ToArray());
        Assert.Equal(baseline.ArmoryChest.ItemIds.ToArray(), result.ArmoryChest.ItemIds.ToArray());
        Assert.Equal(baseline.Armoire.ItemIds.ToArray(), result.Armoire.ItemIds.ToArray());
        Assert.Equal(baseline.GlamourDresser.ItemIds.ToArray(), result.GlamourDresser.ItemIds.ToArray());
        Assert.Equal(baseline.GlamourDresser.Status, result.GlamourDresser.Status);
        Assert.NotNull(result.DresserEntries);
        Assert.Equal(baseline.DresserEntries!.Value.Length, result.DresserEntries!.Value.Length);
        for (var index = 0; index < baseline.DresserEntries.Value.Length; index++)
        {
            var expected = baseline.DresserEntries.Value[index];
            var actual = result.DresserEntries.Value[index];
            Assert.Equal(expected.EntryIndex, actual.EntryIndex);
            Assert.Equal(expected.RawItemId, actual.RawItemId);
            Assert.Equal(expected.BaseItemId, actual.BaseItemId);
            Assert.Equal(expected.OutfitId, actual.OutfitId);
            Assert.Equal(expected.Components.ToArray(), actual.Components.ToArray());
        }
    }

    private static string FixtureRoot(string folder) =>
        Path.Combine(ProviderFixture.Root, "tests", "fixtures", "Contracts", "1.2", "fixtures", folder);

    private static CollectionDataSnapshot ParseWithInventory(JsonObject snapshot)
    {
        var result = CollectionDataSnapshotParser.Parse(snapshot.ToJsonString(), 42, ProviderFixture.Now,
            inventory: ProviderFixture.InventoryObservation(snapshot));
        Assert.True(result.Accepted);
        return result.Snapshot!;
    }
}
