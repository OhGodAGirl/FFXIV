using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpringCleaning.Shared.Inventory;

namespace SpringCleaning.Tests;

public sealed class InventoryContractBoundaryTests
{
    private static readonly string[] LocationKinds = ["Inventory", "Equipped", "ArmoryChest", "ChocoboSaddlebag", "PremiumSaddlebag", "Armoire", "GlamourDresser"];
    private static JsonNode Snapshot() => JsonNode.Parse(InventoryFixture.Inventory())!;
    private static JsonNode Location(JsonNode root) => root["locations"]![0]!;
    private static InventorySnapshot Read(JsonNode root, string character = "12") => InventoryContract.ReadSnapshot(root.ToJsonString(), character);

    [Fact]
    public void AcceptsActualProviderDateTimeOffsetSerialization()
    {
        var utc = new DateTimeOffset(2026, 9, 12, 12, 30, 0, TimeSpan.Zero);
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            available = true,
            contentId = "12",
            revision = 1,
            retainerRosterKnown = false,
            locations = new[] { new { kind = "Inventory", retainerId = "0", name = "Inventory", state = "Observed", observedAtUtc = utc, attemptedAtUtc = utc, supportsQuantity = true, items = Array.Empty<object>() } },
        });
        using var document = JsonDocument.Parse(payload);
        Assert.EndsWith("+00:00", document.RootElement.GetProperty("locations")[0].GetProperty("observedAtUtc").GetString()!, StringComparison.Ordinal);
        Assert.Equal("Observed", InventoryContract.ReadSnapshot(payload, "12").Locations[0].State);
    }

    [Fact]
    public void TransportBoundsAreInclusiveAndCountUtf8Bytes()
    {
        Assert.Throws<JsonException>(() => InventoryContract.Parse("{\"a\":{},\"a\":1}"));
        using var document = InventoryContract.Parse("{\"x\":\"é\"}", 10);
        Assert.Equal("é", document.RootElement.GetProperty("x").GetString());
        Assert.Throws<JsonException>(() => InventoryContract.Parse("{\"x\":\"é\"}", 9));
        var payload = InventoryFixture.Inventory().PadRight(InventoryContract.ByteLimit);
        Assert.Equal(1UL, InventoryContract.ReadSnapshot(payload, "12").Revision);
        Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(payload + " ", "12"));
        InventoryContract.ValidateCapabilities(InventoryFixture.Capabilities.PadRight(16 * 1024));
        Assert.Throws<JsonException>(() => InventoryContract.ValidateCapabilities(InventoryFixture.Capabilities.PadRight(16 * 1024 + 1)));
        Assert.Throws<JsonException>(() => InventoryContract.Parse("{\"outer\":[{\"x\":1,\"x\":2}]}"));
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("scope", "AnotherCharacter")]
    public void CapabilityIdentityCannotDrift(string field, string value)
    {
        var root = JsonNode.Parse(InventoryFixture.Capabilities)!; root[field] = value;
        Assert.Throws<JsonException>(() => InventoryContract.ValidateCapabilities(root.ToJsonString()));
    }

    [Fact]
    public void UnavailableEnvelopeCannotCarryFacts()
    {
        var root = JsonNode.Parse("{\"schemaVersion\":\"1.0\",\"available\":false,\"contentId\":null,\"revision\":0,\"retainerRosterKnown\":false,\"locations\":null}")!;
        foreach (var field in new[] { "contentId", "locations", "retainerRosterKnown" })
        {
            var invalid = root.DeepClone();
            if (field == "contentId") invalid[field] = "12";
            else if (field == "locations") invalid[field] = new JsonArray();
            else invalid[field] = true;
            Assert.Throws<JsonException>(() => Read(invalid));
        }
        var snapshot = Snapshot(); snapshot["schemaVersion"] = "2.0";
        Assert.Throws<JsonException>(() => Read(snapshot));
        snapshot = Snapshot(); snapshot["revision"] = 0;
        Assert.Throws<JsonException>(() => Read(snapshot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData(" 12")]
    [InlineData("12a")]
    [InlineData("18446744073709551616")]
    [InlineData("123456789012345678901")]
    public void CharacterIdentifiersMustBeCanonicalNonzeroUlong(string id)
    {
        var root = Snapshot(); root["contentId"] = id;
        Assert.Throws<JsonException>(() => Read(root, id));
    }

    [Fact]
    public void MaximumCanonicalCharacterAndRevisionAreNotRounded()
    {
        var root = Snapshot(); root["contentId"] = ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture); root["revision"] = ulong.MaxValue;
        var value = Read(root, "18446744073709551615");
        Assert.Equal(ulong.MaxValue, value.Revision); Assert.Equal("18446744073709551615", value.Character);
        root["contentId"] = "1"; Assert.Equal("1", Read(root, "1").Character);
    }

    [Fact]
    public void LocationRosterLimitsAreIndependentOfDuplicateKeys()
    {
        var root = Snapshot(); var basis = Location(root).DeepClone();
        var locations = new JsonArray();
        foreach (var kind in LocationKinds)
        {
            var row = basis.DeepClone(); row["kind"] = kind; row["supportsQuantity"] = kind is not ("Armoire" or "GlamourDresser"); locations.Add(row);
        }
        for (var id = 1; id <= 10; id++) { var row = basis.DeepClone(); row["kind"] = "Retainer"; row["retainerId"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture); locations.Add(row); }
        root["locations"] = locations; root["retainerRosterKnown"] = true;
        Assert.Equal(17, Read(root).Locations.Length); Assert.True(Read(root).RosterKnown);
        var eleventh = basis.DeepClone(); eleventh["kind"] = "Retainer"; eleventh["retainerId"] = "11"; locations.Add(eleventh);
        Assert.Throws<JsonException>(() => Read(root));
        for (var i = 0; i < 7; i++) locations.RemoveAt(0);
        Assert.Throws<JsonException>(() => Read(root));
    }

    [Theory]
    [InlineData("kind", "Other")]
    [InlineData("state", "Fresh")]
    [InlineData("retainerId", "1")]
    public void LocationVocabularyAndIdentityAreExact(string field, string value)
    {
        var root = Snapshot(); Location(root)[field] = value;
        Assert.Throws<JsonException>(() => Read(root));
    }

    [Fact]
    public void DuplicateLocationsInvalidRetainersAndNamesAreRejected()
    {
        var root = Snapshot(); root["locations"]!.AsArray().Add(Location(root).DeepClone()); Assert.Throws<JsonException>(() => Read(root));
        root = Snapshot(); Location(root)["kind"] = "Retainer"; Assert.Throws<JsonException>(() => Read(root));
        root = Snapshot(); Location(root)["name"] = new string('n', 256); Assert.Single(Read(root).Locations);
        Location(root)["name"] = new string('n', 257); Assert.Throws<JsonException>(() => Read(root));
        root = Snapshot(); Location(root)["name"] = null; Assert.Throws<JsonException>(() => Read(root));
    }

    [Theory]
    [InlineData("not-a-dateZ")]
    [InlineData("2026-09-12T12:00:00")]
    [InlineData("2026-09-12T12:00:00+25:00")]
    public void ObservationAndAttemptTimesMustBeUtc(string value)
    {
        foreach (var field in new[] { "observedAtUtc", "attemptedAtUtc" })
        {
            var root = Snapshot(); Location(root)[field] = value;
            Assert.Throws<JsonException>(() => Read(root));
        }
    }

    [Fact]
    public void FailedWithoutObservationCannotSmuggleItems()
    {
        var root = Snapshot(); var row = Location(root); row["state"] = "ReadFailed"; row["observedAtUtc"] = null; row["attemptedAtUtc"] = null;
        Assert.Empty(Read(root).Locations[0].Items);
        row["items"] = JsonNode.Parse("[{\"itemId\":1,\"normalQuantity\":1,\"highQualityQuantity\":0}]");
        Assert.Throws<JsonException>(() => Read(root));
        row["state"] = "Unknown"; Assert.Throws<JsonException>(() => Read(root));
        row["items"] = new JsonArray(); row["state"] = "Retained"; Assert.Throws<JsonException>(() => Read(root));
    }

    [Theory]
    [InlineData("0", "1", "0")]
    [InlineData("1000000", "1", "0")]
    [InlineData("4294967296", "1", "0")]
    [InlineData("4294967297", "1", "0")]
    [InlineData("1", "0", "0")]
    [InlineData("1", "1", "null")]
    [InlineData("1", "4294967296", "0")]
    [InlineData("1", "1", "4294967296")]
    [InlineData("1", "18446744073709551616", "1")]
    [InlineData("1", "\"1\"", "0")]
    public void PerQualityCountsAndBaseIdsAreBounded(string id, string normal, string high)
    {
        var payload = InventoryFixture.Inventory(items: $"[{{\"itemId\":{id},\"normalQuantity\":{normal},\"highQualityQuantity\":{high}}}]");
        Assert.ThrowsAny<Exception>(() => InventoryContract.ReadSnapshot(payload, "12"));
    }

    [Fact]
    public void MembershipLocationsNeverContainQuantities()
    {
        foreach (var quantities in new[] { "\"normalQuantity\":1,\"highQualityQuantity\":null", "\"normalQuantity\":null,\"highQualityQuantity\":1" })
            Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(InventoryFixture.Inventory(kind: "Armoire", quantities: false, items: "[{\"itemId\":1," + quantities + "}]"), "12"));
        Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(InventoryFixture.Inventory(kind: "Inventory", quantities: false), "12"));
        Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(InventoryFixture.Inventory(kind: "GlamourDresser", quantities: true), "12"));
        const string item = "{\"itemId\":999999,\"normalQuantity\":0,\"highQualityQuantity\":4294967295}";
        var accepted = InventoryContract.ReadSnapshot(InventoryFixture.Inventory(items: "[" + item + "]"), "12");
        Assert.Equal(uint.MaxValue, accepted.Locations[0].Items[0].HighQuality);
        Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(InventoryFixture.Inventory(items: "[" + item + "," + item + "]"), "12"));
    }

    [Fact]
    public void ItemCountLimitAcceptsFiftyThousandDistinctItems()
    {
        var rows = string.Join(',', Enumerable.Range(1, 50_000).Select(id => $"{{\"itemId\":{id},\"normalQuantity\":1,\"highQualityQuantity\":0}}"));
        Assert.Equal(50_000, InventoryContract.ReadSnapshot(InventoryFixture.Inventory(items: "[" + rows + "]"), "12").Locations[0].Items.Length);
        Assert.Throws<JsonException>(() => InventoryContract.ReadSnapshot(InventoryFixture.Inventory(items: "[" + rows + ",{\"itemId\":50001,\"normalQuantity\":1,\"highQualityQuantity\":0}]"), "12"));
    }

    [Fact]
    public void DuplicateDetectionScopesNamesToTheirOwnObject()
    {
        using var document = InventoryContract.Parse("{\"x\":1,\"child\":{\"x\":2},\"siblings\":[{\"x\":3},{\"x\":4}]}");
        Assert.Equal(4, document.RootElement.GetProperty("siblings")[1].GetProperty("x").GetInt32());
        Assert.Throws<JsonException>(() => InventoryContract.Parse("{\"x\":1,\"child\":{\"x\":2},\"x\":3}"));
    }

    [Theory]
    [InlineData("2026-09-12T12:00:00Z")]
    [InlineData("2026-09-12T12:00:00.1234567Z")]
    [InlineData("2026-09-12T12:00:00+00:00")]
    [InlineData("2026-09-12T12:00:00.1234567+00:00")]
    [InlineData("2026-09-12T05:00:00-07:00")]
    [InlineData("2026-09-12T13:00:00+01:00")]
    public void SupportedUtcRepresentationsPreserveObservationText(string timestamp)
    {
        var root = Snapshot(); Location(root)["observedAtUtc"] = timestamp;
        Assert.Equal(timestamp, Read(root).Locations[0].ObservedAt);
    }

    [Fact]
    public void RejectedInventoryDataRetainsItsDiagnosticReason()
    {
        void Invalid(Action<JsonNode> change, string reason)
        {
            var root = Snapshot(); change(root); Assert.Equal(reason, Assert.Throws<JsonException>(() => Read(root)).Message);
        }
        Assert.Equal("Payload exceeds the consumer bound.", Assert.Throws<JsonException>(() => InventoryContract.Parse("{}", 1)).Message);
        Assert.Equal("Duplicate property.", Assert.Throws<JsonException>(() => InventoryContract.Parse("{\"x\":1,\"x\":2}")).Message);
        Assert.Equal("Unsupported Inventory capability.", Assert.Throws<JsonException>(() => InventoryContract.ValidateCapabilities(InventoryFixture.Capabilities.Replace("CurrentCharacter", "Other"))).Message);
        Assert.Equal("Unsupported quantities.", Assert.Throws<JsonException>(() => InventoryContract.ValidateCapabilities(InventoryFixture.Capabilities.Replace("Normal", "NQ"))).Message);
        Invalid(root => root["schemaVersion"] = "2.0", "Unsupported Inventory schema.");
        Invalid(root => root["available"] = false, "Malformed unavailable snapshot.");
        var unavailable = "{\"schemaVersion\":\"1.0\",\"available\":false,\"contentId\":null,\"revision\":0,\"locations\":null,\"retainerRosterKnown\":false}";
        Assert.Equal("Inventory is unavailable.", Assert.Throws<InvalidOperationException>(() => InventoryContract.ReadSnapshot(unavailable, "12")).Message);
        Invalid(root => root["contentId"] = "13", "Wrong character.");
        Invalid(root => root["revision"] = 0, "Invalid revision.");
        Invalid(root => Location(root)["kind"] = "Other", "Invalid location.");
        Invalid(root => Location(root)["name"] = new string('x', 257), "Name exceeds bounds.");
        Invalid(root => Location(root)["name"] = null, "Required string.");
        Invalid(root => Location(root)["state"] = "Unknown", "Inconsistent observation.");
        Invalid(root => Location(root)["items"] = JsonNode.Parse("[{\"itemId\":0,\"normalQuantity\":1,\"highQualityQuantity\":0}]"), "Invalid item id.");
        Invalid(root => Location(root)["items"] = JsonNode.Parse("[{\"itemId\":1,\"normalQuantity\":0,\"highQualityQuantity\":0}]"), "Invalid quantity meaning.");
        Invalid(root => { var row = Location(root); row["state"] = "ReadFailed"; row["observedAtUtc"] = null; row["items"] = JsonNode.Parse("[{\"itemId\":1,\"normalQuantity\":1,\"highQualityQuantity\":0}]"); }, "Unobserved items.");
        Invalid(root => root["revision"] = 1.5, "Exact integer required.");
        Invalid(root => root["revision"] = JsonNode.Parse("18446744073709551616"), "Exact integer required.");
        Invalid(root => Location(root)["observedAtUtc"] = "invalid", "Invalid timestamp.");
        Invalid(root =>
        {
            var basis = Location(root).DeepClone(); var rows = new JsonArray();
            foreach (var kind in LocationKinds)
            { var row = basis.DeepClone(); row["kind"] = kind; row["supportsQuantity"] = kind is not ("Armoire" or "GlamourDresser"); rows.Add(row); }
            for (var index = 1; index <= 11; index++) { var row = basis.DeepClone(); row["kind"] = "Retainer"; row["retainerId"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture); rows.Add(row); }
            root["locations"] = rows;
        }, "Too many locations.");
    }
}
