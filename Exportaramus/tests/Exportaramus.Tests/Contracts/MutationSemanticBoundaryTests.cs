using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Contracts;

public sealed class MutationSemanticBoundaryTests
{
    private static JsonObject Rich() => JsonNode.Parse(CollectionDataSerializer.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now))!.AsObject();
    private static void AssertSemanticFailure(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.True(ContractSchemaRegistry.Default.Validate(document.RootElement, "exportaramus-snapshot.schema.json"));
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Code);
    }

    [Fact]
    public void ChangedNoticeRequiresEverySourceToMatchItsDeclaredDataTypes()
    {
        var payload = ConsumerContractTests.Edit(ConsumerContractTests.Fixture("snapshot-changed-valid.json"), root =>
        {
            root["changedDataTypes"] = new JsonArray("Unlocks");
            root["changedSources"] = new JsonArray("achievements", "inventory");
        });
        Assert.Equal(CollectionDataValidationCode.SemanticValidationFailed, CollectionDataSemanticValidator.ValidateChanged(payload).Code);
    }

    [Fact]
    public void UnknownUnlockEvidenceCannotBePresentedAsObservedAbsence()
    {
        var root = Rich();
        var item = root["collections"]!["achievements"]!["items"]![0]!;
        item["owned"] = false;
        item["ownershipEvidence"]!["facts"]![0]!["state"] = "Unknown";
        AssertSemanticFailure(root);
    }

    [Fact]
    public void OneUnknownConstituentWitnessCannotHideBesidePositiveWitnesses()
    {
        var root = Rich();
        var facts = root["collections"]!["outfits"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        var witness = facts[0]!.DeepClone();
        witness["sourceKey"] = "armoire";
        witness["state"] = "Unknown";
        var id = witness["subjectId"]!.GetValue<uint>();
        var presence = root["currentItemPresence"]!["armoire"]!["items"]!.AsArray();
        for (var index = presence.Count - 1; index >= 0; index--) if (presence[index]!["itemId"]!.GetValue<uint>() == id) presence.RemoveAt(index);
        facts.Add(witness);
        AssertSemanticFailure(root);
    }

    [Fact]
    public void RelicAchievementEvidenceMustComeFromAchievementsEvenWhenIdMatches()
    {
        var root = Rich();
        var facts = root["collections"]!["relics"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        facts.Single(fact => fact!["sourceKey"]!.GetValue<string>() == "achievements")!["sourceKey"] = "quests";
        AssertSemanticFailure(root);
    }

    [Theory]
    [InlineData("DirectCurrentItemPresence")]
    [InlineData("AllRequiredConstituents")]
    public void PermanentCollectionCannotBorrowAPresenceOrOutfitRule(string rule)
    {
        var root = Rich();
        var item = rule == "AllRequiredConstituents"
            ? root["collections"]!["outfits"]!["items"]![0]!.DeepClone()
            : root["collections"]!["glamour_dresser"]!["items"]![0]!.DeepClone();
        root["collections"]!["mounts"]!["items"] = new JsonArray(item);
        Assert.Equal(rule, item["ownershipEvidence"]!["ruleCode"]!.GetValue<string>());
        AssertSemanticFailure(root);
    }

    [Fact]
    public void OutfitConstituentCannotUsePermanentEvidenceBesideValidPhysicalWitnesses()
    {
        var root = Rich();
        var facts = root["collections"]!["outfits"]!["items"]![0]!["ownershipEvidence"]!["facts"]!.AsArray();
        var witness = facts[0]!.DeepClone();
        witness["dataType"] = "PermanentUnlock";
        witness["sourceKey"] = "achievements";
        facts.Add(witness);
        AssertSemanticFailure(root);
    }

    [Theory]
    [InlineData("1.9", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData("4294967296.0", CollectionDataValidationCode.MalformedSchemaVersion)]
    public void VersionClassificationPreservesDigitsAndUnsignedComponentBounds(string version, CollectionDataValidationCode expected)
    {
        var result = CollectionDataSemanticValidator.ParseDocument("{\"schemaVersion\":\"" + version + "\"}", 4096, true);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Code);
    }

    [Fact]
    public void HealthCannotClaimObservationAfterSnapshotCapture()
    {
        var root = Rich();
        root["sourceHealth"]!["inventory"]!["observedAtUtc"] = "2027-01-01T00:00:00Z";
        AssertSemanticFailure(root);
    }

    [Theory]
    [InlineData(false, CollectionDataValidationCode.InvalidJson)]
    [InlineData(true, CollectionDataValidationCode.ContractBindingFailed)]
    public void InvalidUnicodeValuesRemainTerminalValidationFailures(bool escaped, CollectionDataValidationCode expected)
    {
        var root = Rich();
        root["character"]!["displayName"] = "SURROGATE_MARKER";
        var payload = root.ToJsonString().Replace("SURROGATE_MARKER", escaped ? "\\uD800" : "\ud800", StringComparison.Ordinal);
        var result = CollectionDataSemanticValidator.ValidateRich(payload);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Code);
    }

    [Theory]
    [InlineData("outfits")]
    [InlineData("relics")]
    public void UnknownDerivedIdentityIsASemanticFailureRatherThanABindingException(string key)
    {
        var root = Rich();
        root["collections"]![key]!["items"]![0]!["id"] = uint.MaxValue;
        AssertSemanticFailure(root);
    }

    [Theory]
    [InlineData("{}", CollectionDataValidationCode.MissingSchemaVersion)]
    [InlineData("{\"schemaVersion\":\"2.0\"}", CollectionDataValidationCode.UnsupportedSchemaVersion)]
    [InlineData("{\"schemaVersion\":\"1\"}", CollectionDataValidationCode.MalformedSchemaVersion)]
    public void TypedConsumerPreservesVersionFailureClassificationBeforeSchemaBinding(string payload, CollectionDataValidationCode expected)
    {
        var result = CollectionDataSemanticValidator.ValidateSnapshot(payload, "123");
        Assert.False(result.Success);
        Assert.Equal(expected, result.Code);
    }
}
