using System.Text.Json.Nodes;
using SpringCleaning.Shared.CollectionData;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Tests.CollectionData;

public sealed class CollectionDataSnapshotParserTests
{
    [Fact]
    public void ExactProviderFixturePreservesPhysicalCompositionAndSeparateInventory()
    {
        var result = Parse(ProviderFixture.Snapshot());
        Assert.True(result.Accepted);
        var value = result.Snapshot!;
        Assert.Equal("Exportaramus", value.ProviderInternalName);
        Assert.Equal(42UL, value.ContentId);
        Assert.Equal(new uint[] { 100 }, value.Inventory.ItemIds);
        Assert.Equal(CollectionSourceStatus.Fresh, value.ArmoryChest.Status);
        Assert.Equal(3, value.DresserEntries!.Value.Length);
        var set = value.DresserEntries.Value[1];
        Assert.Equal(4, set.EntryIndex);
        Assert.Equal(1000900u, set.RawItemId);
        Assert.Equal(900u, set.OutfitId);
        Assert.Equal(new[] { new CollectionDresserComponent(2, 100), new CollectionDresserComponent(3, 2642) }, set.Components);
        Assert.NotNull(value.InventoryObservation);
        Assert.Equal(64, value.ContentHash.Length);
    }

    [Fact]
    public void SourceObservationsRetainProviderTimestamps()
    {
        var result = Parse(ProviderFixture.Snapshot());
        Assert.True(result.Accepted);
        var snapshot = result.Snapshot!;
        var expected = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        foreach (var source in new[] { snapshot.Inventory, snapshot.ArmoryChest, snapshot.ChocoboSaddlebag,
            snapshot.PremiumSaddlebag, snapshot.Retainers, snapshot.Armoire, snapshot.GlamourDresser })
            Assert.Equal(expected, source.ObservedAtUtc);
    }

    [Fact]
    public void MissingInventoryObservationExplainsUnavailableCarriedSources()
    {
        var root = ProviderFixture.Snapshot();
        var result = CollectionDataSnapshotParser.Parse(root.ToJsonString(), 42, ProviderFixture.Now);
        Assert.True(result.Accepted);
        foreach (var source in new[] { result.Snapshot!.Inventory, result.Snapshot.ArmoryChest })
        {
            Assert.Equal(CollectionSourceStatus.Unavailable, source.Status);
            Assert.Equal("Waiting for matching Exportaramus Inventory observations.", source.Reason);
            Assert.Empty(source.ItemIds);
            Assert.Equal(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero), source.ObservedAtUtc);
        }
    }

    [Fact]
    public void MissingCompositionDiffersFromConfirmedEmpty()
    {
        var unknown = Parse(ProviderFixture.Snapshot("compact-valid-legacy-restored-composition-unknown.json"));
        Assert.True(unknown.Accepted);
        Assert.Null(unknown.Snapshot!.DresserEntries);
        var empty = Parse(ProviderFixture.Snapshot("compact-valid-empty-dresser.json"));
        Assert.True(empty.Accepted);
        Assert.Empty(empty.Snapshot!.DresserEntries!.Value);
        var unavailable = ProviderFixture.Snapshot();
        unavailable["consistency"] = "MixedObservationTimes";
        unavailable["currentItemPresence"]!.AsObject().Remove("glamourDresser");
        var health = ProviderFixture.Health(unavailable, "glamourDresser");
        health["sourceStatus"] = "Unavailable";
        health["capabilityState"] = "UnavailablePendingValidation";
        unavailable["sourceCapabilities"]!["glamourDresser"]!["capabilityState"] = "UnavailablePendingValidation";
        health["provenanceType"] = "None";
        health.Remove("observedAtUtc");
        var result = Parse(unavailable);
        Assert.True(result.Accepted);
        Assert.Null(result.Snapshot!.DresserEntries);
        Assert.Equal(CollectionSourceStatus.Unavailable, result.Snapshot.GlamourDresser.Status);
    }

    [Theory]
    [InlineData(null, (int)CollectionDataFailure.EmptyPayload)]
    [InlineData("", (int)CollectionDataFailure.EmptyPayload)]
    [InlineData("{", (int)CollectionDataFailure.MalformedJson)]
    [InlineData("[]", (int)CollectionDataFailure.MalformedJson)]
    public void InvalidPayloadsNeverPublish(string? json, int expected) =>
        Assert.Equal((CollectionDataFailure)expected, CollectionDataSnapshotParser.Parse(json, 42, ProviderFixture.Now).Failure);

    [Fact]
    public void WireLimitsAndIdentityRemainProviderBound()
    {
        var snapshot = ProviderFixture.Snapshot();
        Assert.Equal(CollectionDataFailure.NoCharacter, CollectionDataSnapshotParser.Parse(null, 0, ProviderFixture.Now).Failure);
        Assert.Equal(CollectionDataFailure.WrongCharacter, CollectionDataSnapshotParser.Parse(snapshot.ToJsonString(), 43, ProviderFixture.Now).Failure);
        Assert.Equal(CollectionDataFailure.PayloadTooLarge, CollectionDataSnapshotParser.Parse(new string('x', 4 * 1024 * 1024 + 1), 42, ProviderFixture.Now).Failure);
        snapshot["schemaVersion"] = "2.0";
        Assert.Equal(CollectionDataFailure.UnsupportedProtocol, Parse(snapshot).Failure);
    }

    [Fact]
    public void SchemaAndSemanticRejectionsAreRetained()
    {
        foreach (var field in new[] { "providerInternalName", "providerSessionId", "revision", "sourceHealth", "currentItemPresence" })
        {
            var root = ProviderFixture.Snapshot(); root.Remove(field);
            Assert.False(Parse(root, inventory: ProviderFixture.InventoryObservation()).Accepted);
        }
        var duplicate = ProviderFixture.Snapshot().ToJsonString().Replace("\"schemaVersion\":\"1.2\"", "\"schemaVersion\":\"1.2\",\"schemaVersion\":\"1.2\"", StringComparison.Ordinal);
        Assert.Equal(CollectionDataFailure.MalformedJson, CollectionDataSnapshotParser.Parse(duplicate, 42, ProviderFixture.Now).Failure);
    }

    [Fact]
    public void CanonicalFreshObservationHasNoShortAgeExpiryButFutureClockIsBounded()
    {
        var snapshot = ProviderFixture.Snapshot();
        Assert.True(Parse(snapshot).Accepted);
        snapshot["capturedAtUtc"] = ProviderFixture.Now.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Parse(snapshot).Accepted);
        snapshot["capturedAtUtc"] = ProviderFixture.Now.AddMinutes(5).AddTicks(1).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(CollectionDataFailure.InvalidTimestamp, Parse(snapshot).Failure);
    }

    [Fact]
    public void RevisionOrderingIsScopedToProviderSessionAndExactCharacter()
    {
        var root = ProviderFixture.Snapshot(); root["revision"] = 7;
        var old = Parse(root).Snapshot!;
        Assert.True(Parse(root, old).Accepted);
        root["revision"] = 6; Assert.Equal(CollectionDataFailure.OutOfOrder, Parse(root, old).Failure);
        root["revision"] = 7; root["providerVersion"] = "99.0.0.0";
        Assert.Equal(CollectionDataFailure.InvalidRevision, Parse(root, old).Failure);
        root["providerSessionId"] = Guid.NewGuid().ToString(); root["revision"] = 1;
        Assert.True(Parse(root, old).Accepted);
        Assert.True(Parse(root, old with { ContentId = 77 }).Accepted);
    }

    [Fact]
    public void InventoryRevisionAndEqualRevisionContentsAreIndependentlyChecked()
    {
        var root = ProviderFixture.Snapshot();
        var old = Parse(root, inventory: ProviderFixture.InventoryObservation(root, 7)).Snapshot!;
        Assert.Equal(CollectionDataFailure.OutOfOrder, Parse(root, old, ProviderFixture.InventoryObservation(root, 6)).Failure);
        var changed = ProviderFixture.InventoryObservation(root, 7) with { Hash = "conflict" };
        Assert.Equal(CollectionDataFailure.OutOfOrder, Parse(root, old, changed).Failure);
        Assert.True(Parse(root, old, ProviderFixture.InventoryObservation(root, 8)).Accepted);
        var withoutInventory = CollectionDataSnapshotParser.Parse(root.ToJsonString(), 42, ProviderFixture.Now, old);
        Assert.True(withoutInventory.Accepted);
        Assert.Equal(0UL, withoutInventory.Snapshot!.InventoryRevision);
        Assert.Equal(string.Empty, withoutInventory.Snapshot.InventoryHash);
    }

    [Theory]
    [InlineData("Stale", "Complete", (int)CollectionSourceStatus.Stale)]
    [InlineData("Fresh", "Partial", (int)CollectionSourceStatus.Unavailable)]
    [InlineData("Unavailable", "Complete", (int)CollectionSourceStatus.Unavailable)]
    [InlineData("NeverObserved", "Complete", (int)CollectionSourceStatus.NeverObserved)]
    public void SourceHealthNeverTurnsUnknownIntoMissing(string status, string completeness, int expected)
    {
        var root = ProviderFixture.Snapshot(); root["consistency"] = "MixedObservationTimes";
        var health = ProviderFixture.Health(root, "armoire"); health["sourceStatus"] = status; health["completeness"] = completeness;
        if (status is "Unavailable" or "NeverObserved") { health.Remove("observedAtUtc"); health["provenanceType"] = "None"; }
        if (status is "Unavailable" or "NeverObserved" || completeness != "Complete") root["currentItemPresence"]!.AsObject().Remove("armoire");
        var parsed = Parse(root);
        Assert.True(parsed.Accepted);
        Assert.Equal((CollectionSourceStatus)expected, parsed.Snapshot!.Armoire.Status);
    }

    [Fact]
    public void InventoryLossMismatchOrRetainedReadCannotAuthorizeCarriedOperations()
    {
        var root = ProviderFixture.Snapshot();
        var parsed = CollectionDataSnapshotParser.Parse(root.ToJsonString(), 42, ProviderFixture.Now);
        Assert.True(parsed.Accepted);
        Assert.Equal(CollectionSourceStatus.Unavailable, parsed.Snapshot!.Inventory.Status);
        Assert.Equal(CollectionSourceStatus.Fresh, parsed.Snapshot.GlamourDresser.Status);
        var observation = ProviderFixture.InventoryObservation(root);
        foreach (var invalid in new[] { observation with { Character = "99" }, observation with { Locations = [] },
            observation with { Locations = observation.Locations.Select(location => location with { State = "Retained" }).ToArray() } })
            Assert.Equal(CollectionSourceStatus.Unavailable, Parse(root, inventory: invalid).Snapshot!.Inventory.Status);
        var empty = Parse(root, inventory: observation with
        { Locations = observation.Locations.Select(location => location with { Items = [] }).ToArray() }).Snapshot!;
        Assert.Equal(CollectionSourceStatus.Fresh, empty.Inventory.Status);
        Assert.Empty(empty.Inventory.ItemIds);
    }

    internal static CollectionDataParseResult Parse(JsonObject root, CollectionDataSnapshot? previous = null, InventorySnapshot? inventory = null) =>
        CollectionDataSnapshotParser.Parse(root.ToJsonString(), 42, ProviderFixture.Now, previous, inventory ?? ProviderFixture.InventoryObservation(root));
}
