using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;
using SpringCleaning.Shared.Inventory;
using SpringCleaning.Tests.CollectionData;

namespace SpringCleaning.Tests;

public sealed class MigrationFinalBoundaryTests
{
    [Fact]
    public void EverySelectedOutfitPieceMustMatchItsPlannedPhysicalSource()
    {
        var outfit = new InventoryDresserOutfit(900, null, "Set",
            [new(3202, 7, 3, 10, false, "First", 0), new(3203, 8, 4, 11, false, "Second", 0)]);

        Assert.False(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 3202, 7), new(11, 0, 8)]));
        Assert.True(OutfitPieceSelectionPolicy.Matches(outfit, [new(10, 3202, 7), new(11, 3203, 8)]));
    }

    [Fact]
    public void AcceptedWorkerCompletionPublishesTheExactPreparedSnapshot()
    {
        using var controller = new CollectionDataRefreshController();
        var request = controller.Observe(42, false).RefreshRequest!.Value;
        var result = CollectionDataSnapshotParserTests.Parse(ProviderFixture.Snapshot());

        Assert.Null(controller.Complete(request.ContentId, result, null));
        Assert.Same(result.Snapshot, controller.LatestSnapshot);
        Assert.True(controller.Snapshot.HasSnapshot);
    }

    [Fact]
    public void ClearingTheCharacterRequestsAnImmediateReadBeforeDiscoveryDeadline()
    {
        using var client = new CollectionDataClient(new CollectionDataClientTests.Transport());
        Assert.True(client.TryTakeRefreshRequest(ProviderFixture.Now));
        Assert.False(client.TryTakeRefreshRequest(ProviderFixture.Now));

        client.ClearCharacter();

        Assert.True(client.TryTakeRefreshRequest(ProviderFixture.Now));
        Assert.False(client.TryTakeRefreshRequest(ProviderFixture.Now));
    }

    [Fact]
    public void DisposalImmediatelyDetachesBothEventsAndClearsPublishedAuthority()
    {
        var transport = new CollectionDataClientTests.Transport();
        var client = new CollectionDataClient(transport);
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);

        client.Dispose();

        Assert.Equal(1, transport.Disposals);
        Assert.Null(client.Latest);
        Assert.Null(transport.CaptureChanged());
        Assert.Null(transport.CaptureWithdrawn());
        client.Dispose();
        Assert.Equal(1, transport.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithdrawalResetsOrderingButAnOrdinaryNoticePreservesIt(bool withdrawn)
    {
        var transport = new CollectionDataClientTests.Transport();
        using var client = new CollectionDataClient(transport);
        var snapshot = ProviderFixture.Snapshot();
        var revision = snapshot["revision"]!.GetValue<ulong>();
        snapshot["revision"] = revision + 1;
        transport.Payload = snapshot.ToJsonString();
        Assert.True(client.Refresh(42, ProviderFixture.Now).Accepted);

        if (withdrawn) transport.Withdrawn();
        else transport.Changed();
        snapshot["revision"] = revision;
        transport.Payload = snapshot.ToJsonString();

        var result = client.Refresh(42, ProviderFixture.Now);
        Assert.Equal(withdrawn, result.Accepted);
        Assert.Equal(withdrawn, client.Latest is not null);
    }

    [Fact]
    public void UnknownInventoryLocationRemainsExplicitlyUnobserved()
    {
        var payload = InventoryFixture.Inventory(state: "Unknown", observed: "null");

        var location = Assert.Single(InventoryContract.ReadSnapshot(payload, "12").Locations);

        Assert.Equal("Unknown", location.State);
        Assert.Null(location.ObservedAt);
        Assert.Empty(location.Items);
    }

    [Fact]
    public void RetainedInventoryPreservesItsLastObservationWithoutBecomingCurrent()
    {
        var payload = InventoryFixture.Inventory(state: "Retained");

        var location = Assert.Single(InventoryContract.ReadSnapshot(payload, "12").Locations);

        Assert.Equal("Retained", location.State);
        Assert.NotNull(location.ObservedAt);
    }

    [Fact]
    public void EvenAnEmptyObservedInventoryRequiresAnObservationTimestamp()
    {
        var payload = InventoryFixture.Inventory(state: "Observed", observed: "null");

        var error = Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(payload, "12"));

        Assert.Equal("Inconsistent observation.", error.Message);
    }

    [Theory]
    [InlineData("Armoire")]
    [InlineData("GlamourDresser")]
    public void MembershipOnlyLocationsAcceptItemsWithoutInventingQualityQuantities(string kind)
    {
        var payload = InventoryFixture.Inventory(kind: kind, quantities: false,
            items: """[{"itemId":2642,"normalQuantity":null,"highQualityQuantity":null}]""");

        var location = Assert.Single(InventoryContract.ReadSnapshot(payload, "12").Locations);
        var item = Assert.Single(location.Items);

        Assert.False(location.SupportsQuantity);
        Assert.Equal(2642u, item.ItemId);
        Assert.Null(item.Normal);
        Assert.Null(item.HighQuality);
    }

    [Fact]
    public void InvalidUtf16CannotBeSilentlyReplacedBeforeContractValidation()
    {
        Assert.Throws<EncoderFallbackException>(() => InventoryContract.Parse("{\"name\":\"\uD800\"}"));
    }

    [Fact]
    public void UnicodePayloadAndMaximumDepthRemainAccepted()
    {
        const string payload = "{\"name\":\"é\"}";
        using var unicode = InventoryContract.Parse(payload);
        Assert.Equal("é", unicode.RootElement.GetProperty("name").GetString());
        var atLimit = new string('[', 64) + "0" + new string(']', 64);
        using var accepted = InventoryContract.Parse(atLimit);
        Assert.Equal(JsonValueKind.Array, accepted.RootElement.ValueKind);
        Assert.ThrowsAny<JsonException>(() => InventoryContract.Parse("[" + atLimit + "]"));
    }

    [Fact]
    public void NullRequiredLocationNameHasTheSpecificContractDiagnostic()
    {
        var root = JsonNode.Parse(InventoryFixture.Inventory())!;
        root["locations"]![0]!["name"] = null;

        var error = Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(root.ToJsonString(), "12"));

        Assert.Equal("Required string.", error.Message);
    }
}
