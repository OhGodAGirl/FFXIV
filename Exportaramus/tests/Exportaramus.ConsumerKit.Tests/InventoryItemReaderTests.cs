using System.Text.Json.Nodes;
using CollectionExample;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class InventoryItemReaderTests
{
    private const string Capabilities = """
        {"schemaVersion":"1.0","scope":"CurrentCharacter","quantityQuality":["Normal","HighQuality"],"retainedIsCurrent":false}
        """;
    private const string Available = """
        {"schemaVersion":"1.0","available":true,"itemId":100,"contentId":"42","revision":2,
         "normalQuantity":2,"highQualityQuantity":1,"quantityMeaning":"LastObservedQuantifiedLocationsOnly",
         "hasUnknownLocations":true,"hasRetainedLocations":false,"locations":[
          {"kind":"Inventory","retainerId":"0","name":"Inventory","state":"Observed",
           "observedAtUtc":"2026-10-30T12:00:00+00:00","normalQuantity":2,"highQualityQuantity":1}]}
        """;
    private const string Unavailable = """
        {"schemaVersion":"1.0","available":false,"itemId":100,"contentId":null,"revision":0,
         "normalQuantity":0,"highQualityQuantity":0,"quantityMeaning":"LastObservedQuantifiedLocationsOnly",
         "hasUnknownLocations":true,"hasRetainedLocations":false,"locations":[]}
        """;

    private static InventoryItemReader.Result? Read(string payload) => InventoryItemReader.Read(Capabilities, payload, 100, "42");
    private static JsonObject Row(JsonObject root) => root["locations"]!.AsArray()[0]!.AsObject();

    [Theory]
    [InlineData(0, "42")]
    [InlineData(1000000, "42")]
    [InlineData(100, "0")]
    [InlineData(100, "01")]
    public void InvalidRequestIsRejectedEvenWhenResponseIsUnavailable(int itemId, string character)
    {
        var payload = ContractFixture.Edit(Unavailable, root => root["itemId"] = itemId);
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(Capabilities, payload, (uint)itemId, character));
    }

    [Fact]
    public void CapabilitiesAndLocationsRejectExtraFields()
    {
        var caps = ContractFixture.Edit(Capabilities, root => root["extra"] = true);
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(caps, Available, 100, "42"));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => Row(root)["extra"] = true)));
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void LocationNameLengthHasAnInclusiveLimit(int length, bool accepted)
    {
        var payload = ContractFixture.Edit(Available, root => Row(root)["name"] = new string('a', length));
        if (accepted) Assert.Equal(length, Assert.Single(Read(payload)!.Locations).Name.Length);
        else Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Fact]
    public void ControlCharactersAndUnknownRetainedStatesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => Row(root)["name"] = "a\nb")));
        var payload = ContractFixture.Edit(Available, root =>
        {
            root["hasRetainedLocations"] = true;
            Row(root)["state"] = "Unknown";
        });
        Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Fact]
    public void MaximumLocationQuantityIsAccepted()
    {
        var payload = ContractFixture.Edit(Available, root =>
        {
            root["normalQuantity"] = uint.MaxValue;
            root["highQualityQuantity"] = 0;
            Row(root)["normalQuantity"] = uint.MaxValue;
            Row(root)["highQualityQuantity"] = 0;
        });
        Assert.Equal((ulong)uint.MaxValue, Read(payload)!.Normal);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void RetainerLimitIsIndependentOfTheTotalLocationLimit(int count, bool accepted)
    {
        var payload = ContractFixture.Edit(Available, root =>
        {
            var template = Row(root).DeepClone();
            var rows = root["locations"]!.AsArray();
            rows.Clear();
            for (var i = 1; i <= count; i++)
            {
                var row = template.DeepClone();
                row["kind"] = "Retainer";
                row["retainerId"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(row);
            }
            root["normalQuantity"] = count * 2;
            root["highQualityQuantity"] = count;
        });
        if (accepted) Assert.Equal(count, Read(payload)!.Locations.Count);
        else Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Fact]
    public void NonObjectRepliesProduceAValidationError()
    {
        Assert.Throws<InvalidDataException>(() => Read("[]"));
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read("[]", Available, 100, "42"));
    }

    [Fact]
    public void ValidationErrorsIncludeAnExplanation()
    {
        var invalid = ContractFixture.Edit(Available, root => root["schemaVersion"] = "2.0");
        var missing = ContractFixture.Edit(Available, root => root["quantityMeaning"] = null);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<InvalidDataException>(() => Read(invalid)).Message));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<InvalidDataException>(() => Read(missing)).Message));
    }

    [Fact]
    public void AllSeventeenLocationsAreAccepted()
    {
        var payload = ContractFixture.Edit(Available, root =>
        {
            var template = Row(root).DeepClone();
            var rows = root["locations"]!.AsArray();
            rows.Clear();
            foreach (var kind in new[] { "Inventory", "Equipped", "ArmoryChest", "ChocoboSaddlebag", "PremiumSaddlebag", "Armoire", "GlamourDresser" })
            {
                var row = template.DeepClone();
                row["kind"] = kind;
                if (kind is "Armoire" or "GlamourDresser")
                {
                    row["normalQuantity"] = null;
                    row["highQualityQuantity"] = null;
                }
                rows.Add(row);
            }
            for (var i = 1; i <= 10; i++)
            {
                var row = template.DeepClone();
                row["kind"] = "Retainer";
                row["retainerId"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rows.Add(row);
            }
            root["normalQuantity"] = 30;
            root["highQualityQuantity"] = 15;
        });
        Assert.Equal(17, Read(payload)!.Locations.Count);
    }

    [Fact]
    public void ExactByteLimitIsAcceptedAndMultibyteOverflowIsRejected()
    {
        var padded = Unavailable.PadRight(InventoryItemReader.MaximumJsonBytes);
        Assert.Null(Read(padded));
        var caps = Capabilities.PadRight(InventoryItemReader.MaximumJsonBytes);
        Assert.Null(InventoryItemReader.Read(caps, Unavailable, 100, "42"));
        var oversized = "{\"extra\":\"" + new string('é', InventoryItemReader.MaximumJsonBytes / 2) + "\"}";
        Assert.Throws<InvalidDataException>(() => Read(oversized));
        Assert.Throws<System.Text.EncoderFallbackException>(() => Read("\ud800"));
    }

    [Fact]
    public void ExcessiveJsonNestingIsRejectedBeforeFieldValidation()
    {
        var payload = new string('[', 17) + "0" + new string(']', 17);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Read(payload));
    }

    [Theory]
    [InlineData(0, "42")]
    [InlineData(1000000, "42")]
    [InlineData(100, "0")]
    [InlineData(100, "01")]
    [InlineData(100, "invalid")]
    [InlineData(100, "18446744073709551616")]
    public void InvalidRequestIdentityOrItemIsRejectedBeforeReadingTheResponse(int itemId, string character)
    {
        // Arrange: pair a valid reply with an invalid caller scope.
        var payload = Available;

        // Act and Assert.
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(Capabilities, payload, (uint)itemId, character));
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("scope", "OtherCharacter")]
    public void IncompatibleCapabilityIdentityIsRejected(string key, string value)
    {
        // Arrange.
        var capabilities = ContractFixture.Edit(Capabilities, root => root[key] = value);

        // Act and Assert.
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(capabilities, Available, 100, "42"));
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("short-array")]
    [InlineData("wrong-normal")]
    [InlineData("wrong-high")]
    public void QuantityQualityContractMustHaveBothOrderedQualityNames(string fault)
    {
        // Arrange: use the wrong quantity order.
        var capabilities = ContractFixture.Edit(Capabilities, root => root["quantityQuality"] = fault switch
        {
            "wrong-type" => JsonValue.Create("Normal"),
            "short-array" => new JsonArray("Normal"),
            "wrong-normal" => new JsonArray("HighQuality", "HighQuality"),
            _ => new JsonArray("Normal", "Normal"),
        });

        // Act and Assert.
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(capabilities, Available, 100, "42"));
    }

    [Theory]
    [InlineData("Equipped")]
    [InlineData("ArmoryChest")]
    [InlineData("ChocoboSaddlebag")]
    [InlineData("PremiumSaddlebag")]
    public void EachQuantifiedStandardStorePreservesItsLocationAndQualityCounts(string kind)
    {
        // Arrange.
        var payload = ContractFixture.Edit(Available, root => Row(root)["kind"] = kind);

        // Act.
        var result = Assert.IsType<InventoryItemReader.Result>(Read(payload));

        // Assert.
        var location = Assert.Single(result.Locations);
        Assert.Equal(kind, location.Kind);
        Assert.Equal(2u, location.Normal);
        Assert.Equal(1u, location.HighQuality);
        Assert.Equal(2ul, result.Normal);
        Assert.Equal(1ul, result.HighQuality);
    }

    [Fact]
    public void NullRequiredStringAndNonArrayLocationsAreRejected()
    {
        // Arrange.
        var nullMeaning = ContractFixture.Edit(Available, root => root["quantityMeaning"] = null);
        var objectLocations = ContractFixture.Edit(Available, root => root["locations"] = new JsonObject());

        // Act and Assert: reject both malformed values instead of returning zero.
        Assert.Throws<InvalidDataException>(() => Read(nullMeaning));
        Assert.Throws<InvalidDataException>(() => Read(objectLocations));
    }

    [Fact]
    public void DefaultObservationTimeCannotBecomeARecordedObservation()
    {
        // Arrange.
        var payload = ContractFixture.Edit(Available, root => Row(root)["observedAtUtc"] = "0001-01-01T00:00:00+00:00");

        // Act and Assert.
        Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(4294967295L, 1L)]
    public void AQuantifiedLocationCannotHaveZeroOrOverflowingCombinedQuantity(long normal, long high)
    {
        // Arrange: exceed UInt32 at a single location.
        var payload = ContractFixture.Edit(Available, root =>
        {
            root["normalQuantity"] = normal;
            root["highQualityQuantity"] = high;
            Row(root)["normalQuantity"] = normal;
            Row(root)["highQualityQuantity"] = high;
        });

        // Act and Assert.
        Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Fact]
    public void CompleteResponsePreservesQualityCountsIdentityAgeAndCoverage()
    {
        var result = Assert.IsType<InventoryItemReader.Result>(Read(Available));
        Assert.Equal("42", result.ContentId);
        Assert.Equal(100u, result.ItemId);
        Assert.Equal(2, result.Revision);
        Assert.Equal(2ul, result.Normal);
        Assert.Equal(1ul, result.HighQuality);
        Assert.True(result.UnknownLocations);
        Assert.False(result.RetainedLocations);
        var location = Assert.Single(result.Locations);
        Assert.Equal("Inventory", location.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero), location.ObservedAtUtc);
    }

    [Fact]
    public void UnavailableResponseIsNotAnEmptyInventoryResult() => Assert.Null(Read(Unavailable));

    [Theory]
    [InlineData("contentId", "43")]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("quantityMeaning", "Current")]
    public void IncorrectScopeOrMeaningIsRejected(string property, string value) =>
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => root[property] = value)));

    [Theory]
    [InlineData("itemId", 101)]
    [InlineData("revision", 0)]
    [InlineData("normalQuantity", 3)]
    [InlineData("highQualityQuantity", 2)]
    public void MismatchedItemRevisionOrTotalsAreRejected(string property, int value) =>
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => root[property] = value)));

    [Theory]
    [InlineData("Retained")]
    [InlineData("ReadFailed")]
    public void EarlierObservationsRequireAndPreserveRetainedFlag(string state)
    {
        var payload = ContractFixture.Edit(Available, root => Row(root)["state"] = state);
        Assert.Throws<InvalidDataException>(() => Read(payload));
        payload = ContractFixture.Edit(payload, root => root["hasRetainedLocations"] = true);
        var result = Assert.IsType<InventoryItemReader.Result>(Read(payload));
        Assert.True(result.RetainedLocations);
        Assert.Equal(state, Assert.Single(result.Locations).State);
    }

    [Theory]
    [InlineData("Armoire")]
    [InlineData("GlamourDresser")]
    public void MembershipOnlyStorageHasNoInventedCopyCount(string kind)
    {
        var payload = ContractFixture.Edit(Available, root =>
        {
            root["normalQuantity"] = 0;
            root["highQualityQuantity"] = 0;
            var row = Row(root);
            row["kind"] = kind;
            row["normalQuantity"] = null;
            row["highQualityQuantity"] = null;
        });
        var result = Assert.IsType<InventoryItemReader.Result>(Read(payload));
        Assert.Equal(0ul, result.Normal);
        Assert.Null(Assert.Single(result.Locations).Normal);
        Assert.Null(Assert.Single(result.Locations).HighQuality);
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(payload, root => Row(root)["normalQuantity"] = 1)));
    }

    [Fact]
    public void DuplicateAndUnknownFieldsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Read(Available.Replace("\"itemId\":100", "\"itemId\":100,\"itemId\":100", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => root["unexpected"] = true)));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => root.Remove("hasUnknownLocations"))));
    }

    [Theory]
    [InlineData("kind", "UnknownStorage")]
    [InlineData("state", "Unknown")]
    [InlineData("retainerId", "123")]
    [InlineData("name", "")]
    [InlineData("observedAtUtc", "2026-10-30T12:00:00+01:00")]
    public void InvalidLocationFieldsAreRejected(string property, string value) =>
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Available, root => Row(root)[property] = value)));

    [Fact]
    public void DuplicateLocationCannotInflateTotals()
    {
        var payload = ContractFixture.Edit(Available, root =>
        {
            root["normalQuantity"] = 4;
            root["highQualityQuantity"] = 2;
            root["locations"]!.AsArray().Add(Row(root).DeepClone());
        });
        Assert.Throws<InvalidDataException>(() => Read(payload));
    }

    [Fact]
    public void RetainerIdsMustBeNonzeroAndCanonical()
    {
        var payload = ContractFixture.Edit(Available, root => Row(root)["kind"] = "Retainer");
        Assert.Throws<InvalidDataException>(() => Read(payload));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(payload, root => Row(root)["retainerId"] = "01")));
        var result = Read(ContractFixture.Edit(payload, root => Row(root)["retainerId"] = "123"));
        Assert.Equal("123", Assert.Single(result!.Locations).RetainerId);
    }

    [Fact]
    public void UnavailableResponseCannotCarryQuantitiesOrCharacterData()
    {
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Unavailable, root => root["normalQuantity"] = 1)));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Unavailable, root => root["contentId"] = "42")));
        Assert.Throws<InvalidDataException>(() => Read(ContractFixture.Edit(Unavailable, root => root["hasUnknownLocations"] = false)));
    }

    [Fact]
    public void InvalidCapabilitiesAndOversizedPayloadAreRejected()
    {
        var caps = ContractFixture.Edit(Capabilities, root => root["retainedIsCurrent"] = true);
        Assert.Throws<InvalidDataException>(() => InventoryItemReader.Read(caps, Available, 100, "42"));
        Assert.Throws<InvalidDataException>(() => Read(new string(' ', InventoryItemReader.MaximumJsonBytes + 1)));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Read("{"));
    }
}
