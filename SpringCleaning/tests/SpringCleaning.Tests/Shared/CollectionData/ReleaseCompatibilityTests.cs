using System.Text.Json;
using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class ReleaseCompatibilityTests
{
    [Fact]
    public void MissingProviderCanRecoverThroughDiscoveryWithoutRecreatingTheConsumer()
    {
        var transport = new CollectionDataClientTests.Transport
        {
            BeforeRead = () => throw new IOException("Provider disabled"),
        };
        using var client = new CollectionDataClient(transport);
        Assert.True(client.TryTakeRefreshRequest(ProviderFixture.Now));

        var unavailable = client.Refresh(42, ProviderFixture.Now);

        Assert.Equal(CollectionDataFailure.ProviderUnavailable, unavailable.Failure);
        Assert.Null(client.Latest);
        Assert.Equal(CollectionOwnershipFact.Unknown,
            CollectionOwnershipPolicy.GetFact(client.Latest, CollectionSourceName.GlamourDresser, 100));
        Assert.False(client.TryTakeRefreshRequest(ProviderFixture.Now.AddSeconds(4)));
        transport.BeforeRead = null;
        Assert.True(client.TryTakeRefreshRequest(ProviderFixture.Now.AddSeconds(5)));
        Assert.True(client.Refresh(42, ProviderFixture.Now.AddSeconds(5)).Accepted);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest!.Inventory.Status);
    }

    [Fact]
    public void ProviderReleaseAndSessionChangeAllowBothRevisionCountersToRestart()
    {
        var transport = new CollectionDataClientTests.Transport();
        var original = JsonNode.Parse(transport.Payload)!;
        original["revision"] = 73;
        transport.Payload = original.ToJsonString();
        transport.Inventory = ProviderFixture.Inventory(original, 91);
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        var originalEpoch = client.PresentationEpoch;
        var nextSession = Guid.NewGuid().ToString();
        var capabilities = JsonNode.Parse(transport.Caps)!;
        capabilities["providerSessionId"] = nextSession;
        capabilities["providerVersion"] = "1.0.0.1";
        original["providerSessionId"] = nextSession;
        original["providerVersion"] = "1.0.0.1";
        original["revision"] = 1;
        transport.Caps = capabilities.ToJsonString();
        transport.Payload = original.ToJsonString();
        transport.Inventory = ProviderFixture.Inventory(original, 1);

        var restarted = client.Refresh(42, ProviderFixture.Now);

        Assert.True(restarted.Accepted);
        Assert.Equal("1.0.0.1", client.Latest!.ProviderVersion);
        Assert.Equal(nextSession, client.Latest.ProviderSessionId);
        Assert.Equal(1UL, client.Latest.Revision);
        Assert.Equal(1UL, client.Latest.InventoryRevision);
        Assert.NotEqual(originalEpoch, client.PresentationEpoch);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest.Inventory.Status);
    }

    [Fact]
    public void InventoryWithdrawalIsUnknownAndRecoveryPreservesStorageEvidence()
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        transport.Inventory = """{"schemaVersion":"1.0","available":false,"contentId":null,"revision":0,"retainerRosterKnown":false,"locations":null}""";

        var unavailable = client.Refresh(42, ProviderFixture.Now);

        Assert.True(unavailable.Accepted);
        Assert.Equal(CollectionSourceStatus.Unavailable, client.Latest!.Inventory.Status);
        Assert.Equal(CollectionSourceStatus.Unavailable, client.Latest.ArmoryChest.Status);
        Assert.Equal(CollectionOwnershipFact.Unknown,
            CollectionOwnershipPolicy.GetFact(client.Latest, CollectionSourceName.Inventory, 100));
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest.GlamourDresser.Status);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest.Armoire.Status);
        transport.Inventory = ProviderFixture.Inventory(revision: 2);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest!.Inventory.Status);
        Assert.Equal(CollectionSourceStatus.Fresh, client.Latest.ArmoryChest.Status);
        Assert.Equal(CollectionOwnershipFact.Owned,
            CollectionOwnershipPolicy.GetFact(client.Latest, CollectionSourceName.Inventory, 100));
    }

    [Fact]
    public void ExistingConfigurationKeepsEveryPersistedPreferenceThroughSerialization()
    {
        const string saved = """
            {
              "Version": 1,
              "TabStyle": 4,
              "ActionDelayMilliseconds": 900,
              "ConfirmationTimeoutMilliseconds": 9500,
              "InteractionDistance": 2.5,
              "ProtectMateriaAndFullySpiritboundItems": false,
              "ProtectItemsWithMateria": true,
              "ProtectItemsWithSpiritbond": false,
              "IgnoredDuplicateItemIds": [100, 2642],
              "ReservedInventorySlots": 7,
              "OnlyMoveCompleteOutfits": true,
              "SkipExistingDresserOutfits": true,
              "ReviewOnlyItemsOutsideOutfits": true,
              "DiagnosticMode": false,
              "RememberWindowPlacement": true,
              "DresserArmoireOverlayX": 123.5,
              "DresserArmoireOverlayY": 456.75
            }
            """;

        var restored = JsonSerializer.Deserialize<Configuration>(saved)!;
        var roundTrip = JsonSerializer.SerializeToNode(restored)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved), roundTrip));
        Assert.Equal(1, restored.Version);
        Assert.True(restored.ProtectItemsWithMateria);
        Assert.False(restored.ProtectItemsWithSpiritbond);
        Assert.True(restored.IgnoredDuplicateItemIds.SetEquals([100u, 2642u]));
    }
}
