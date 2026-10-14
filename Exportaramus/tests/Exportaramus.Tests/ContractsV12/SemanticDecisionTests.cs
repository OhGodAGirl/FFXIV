// Regression parity with Contracts/SemanticDecisionTests.cs, targeting the independently pinned 1.2 assembly.
using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.ContractsV12;

public sealed class SemanticDecisionTests
{
    private static JsonObject Rich() => JsonNode.Parse(V12FixtureAdapter.Rich(SnapshotFixture.Create(), "Fixture", SnapshotFixture.Now))!.AsObject();
    private static JsonNode Item(JsonObject root, string key) => root["collections"]![key]!["items"]![0]!;
    private static JsonArray Facts(JsonObject root, string key) => Item(root, key)["ownershipEvidence"]!["facts"]!.AsArray();
    private static JsonObject Absent(string source, uint id) => new() { ["dataType"] = source == "achievements" ? "PermanentUnlock" : "CurrentItemPresence", ["sourceKey"] = source, ["subjectId"] = id, ["state"] = "Absent", ["sourceStatus"] = "Fresh" };
    private static JsonObject NegativeDerived()
    {
        var root = Rich();
        foreach (var source in root["currentItemPresence"]!.AsObject()) source.Value!["items"] = new JsonArray();
        foreach (var key in new[] { "armoires", "glamour_dresser" })
        { Item(root, key)["owned"] = false; Facts(root, key)[0]!["state"] = "Absent"; }
        Item(root, "outfits")["owned"] = false;
        Item(root, "outfits")["ownershipEvidence"]!["facts"] = new JsonArray(new uint[] { 2642, 2965, 3747 }.SelectMany(id => CollectionDataSemanticValidator.PresenceKeys.Select(source => (JsonNode?)Absent(source, id))).ToArray());
        Item(root, "relics")["owned"] = false;
        Item(root, "relics")["ownershipEvidence"]!["facts"] = new JsonArray(CollectionDataSemanticValidator.PresenceKeys.Select(source => (JsonNode?)Absent(source, 1675)).Append(Absent("achievements", 129)).ToArray());
        Item(root, "achievements")["owned"] = false; Facts(root, "achievements")[0]!["state"] = "Absent";
        return root;
    }

    [Fact]
    public void NegativeDerivedEvidenceRequiresEveryConstituentAndEveryAbsenceSource()
    {
        var negative = NegativeDerived();
        var accepted = CollectionDataSemanticValidator.ValidateRich(negative.ToJsonString());
        Assert.True(accepted.Success, accepted.Code.ToString());
        foreach (var edit in new Action<JsonObject>[]
        {
            n => Facts(n, "outfits").RemoveAt(0),
            n => Facts(n, "relics").RemoveAt(0),
            n => Facts(n, "relics").RemoveAt(7),
            n => Facts(n, "relics")[7]!["subjectId"] = 130,
            n => Facts(n, "relics")[0]!["subjectId"] = 1676,
            n => Facts(n, "outfits")[0]!["state"] = "Present",
        })
        {
            var root = negative.DeepClone().AsObject(); edit(root);
            Assert.False(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        }
    }

    [Fact]
    public void RichSemanticBoundaryRejectsInconsistentEvidenceEvenWithWellFormedFields()
    {
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["sourceHealth"]!["mounts"]!["dataType"] = "Derived",
            n => n["sourceHealth"]!["mounts"]!["observedAtUtc"] = "2026-09-08T00:00:00Z",
            n => n["collections"]!["mounts"]!["ownershipKind"] = "Derived",
            n => n["collections"]!["mounts"]!["observedAtUtc"] = "2026-09-06T00:00:00Z",
            n => Facts(n, "mounts")[0]!["dataType"] = "Derived",
            n => Facts(n, "mounts")[0]!["state"] = "Unknown",
            n => Facts(n, "mounts")[0]!["sourceStatus"] = "Unavailable",
            n => Facts(n, "mounts")[0]!["sourceStatus"] = "Stale",
            n => Facts(n, "mounts")[0]!["subjectId"] = 2,
            n => Facts(n, "mounts")[0]!["sourceKey"] = "minions",
            n => { Facts(n, "mounts")[0]!["sourceKey"] = "nonexistent"; Facts(n, "mounts")[0]!["dataType"] = "CurrentItemPresence"; },
            n => Facts(n, "armoires")[0]!["sourceKey"] = "armoires",
            n => Facts(n, "mounts").Add(Facts(n, "mounts")[0]!.DeepClone()),
            n => n["collections"]!["mounts"]!["items"]!.AsArray().Add(Item(n, "mounts").DeepClone()),
            n => Item(n, "mounts")["ownershipEvidence"]!["ruleCode"] = "UnknownRule",
            n => { n["sourceHealth"]!["mounts"]!["completeness"] = "Partial"; n["collections"]!["mounts"]!["completeness"] = "Partial"; },
            n => Facts(n, "glamour_dresser")[0]!["subjectId"] = 1675,
            n => Item(n, "mounts")["ownershipEvidence"]!["ruleCode"] = "AllRequiredConstituents",
            n => Item(n, "mounts")["ownershipEvidence"]!["ruleCode"] = "PermanentUnlockOrCurrentItemPresence",
            n => Item(n, "mounts")["ownershipEvidence"]!["ruleCode"] = "DirectCurrentItemPresence",
            n => Item(n, "outfits")["ownershipEvidence"]!["ruleCode"] = "DirectPermanentUnlock",
            n => Facts(n, "outfits")[0] = Absent("achievements", 129),
            n => Facts(n, "relics")[0]!["sourceKey"] = "mounts",
            n => Item(n, "quests").AsObject().Remove("status"),
            n => Item(n, "achievements").AsObject().Remove("rewardCollected"),
            n => Item(n, "mounts")["reportedEvidence"] = new JsonArray(new JsonObject { ["itemId"] = 1, ["reporter"] = " invalid ", ["reportedAtUtc"] = "2026-09-07T00:00:00Z" }),
        })
        {
            var root = Rich(); edit(root);
            using var document = JsonDocument.Parse(root.ToJsonString());
            Assert.False(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        }
    }

    [Theory]
    [InlineData("SourceCapabilities", "mounts", true)]
    [InlineData("SourceHealth", "inventory", true)]
    [InlineData("ReportedOwnership", "reportedOwnership", true)]
    [InlineData("Unlocks", "reportedOwnership", false)]
    [InlineData("CurrentItemPresence", "mounts", false)]
    public void ChangedSourcesMustMatchAnnouncedDataType(string type, string source, bool expected)
    {
        var json = ConsumerContractTests.Edit(ConsumerContractTests.Fixture("snapshot-changed-valid.json"), n => { n["changedDataTypes"] = new JsonArray(type); n["changedSources"] = new JsonArray(source); });
        Assert.Equal(expected, CollectionDataSemanticValidator.ValidateChanged(json).Success);
    }

    [Fact]
    public void MathematicalIntegerSpellingsCannotEscapeTypedBindingBoundary()
    {
        var rich = Rich().ToJsonString();
        Assert.False(CollectionDataSemanticValidator.ValidateRich(rich.Replace("\"subjectId\":1,", "\"subjectId\":1.0,", StringComparison.Ordinal)).Success);
        var compact = ConsumerContractTests.Fixture("compact-valid-full.json").Replace("\"revision\": 1,", "\"revision\": 1.0,", StringComparison.Ordinal);
        Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(compact, "12345678901234567").Success);
        Assert.Equal(CollectionDataValidationCode.InvalidJson, CollectionDataSemanticValidator.ParseDocument("{\"schemaVersion\":\"\\uD800\"}", 4096, true).Code);
        Assert.Equal(CollectionDataValidationCode.PayloadTooLarge, CollectionDataSemanticValidator.ParseDocument(new string('é', 2049), 4096, true).Code);
    }

    [Fact]
    public void ReportOrderingRejectsDescendingIdsAndDuplicateReporterPairs()
    {
        var payload = ConsumerContractTests.Fixture("compact-valid-full.json");
        foreach (var ids in new[] { new[] { 2, 1 }, new[] { 1, 1 } })
        {
            var changed = ConsumerContractTests.Edit(payload, root => root["reportedOwnership"]!["items"] = new JsonArray(ids.Select(id => (JsonNode?)new JsonObject { ["itemId"] = id, ["reporter"] = "Fixture", ["reportedAtUtc"] = "2026-09-06T12:00:00Z" }).ToArray()));
            Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(changed, "12345678901234567").Success);
        }
        foreach (var reporters in new[] { new[] { "Fixture", "Fixture" }, new[] { "Z", "A" }, new[] { "A", "B" }, new[] { "A", "B\0C" } })
        {
            var changed = ConsumerContractTests.Edit(payload, root => root["reportedOwnership"]!["items"] = new JsonArray(reporters.Select((reporter, i) => (JsonNode?)new JsonObject { ["itemId"] = 1, ["reporter"] = reporter, ["reportedAtUtc"] = $"2026-09-06T12:00:0{i}Z" }).ToArray()));
            Assert.Equal(reporters[0] == "A" && reporters[1] == "B", CollectionDataSemanticValidator.ValidateSnapshot(changed, "12345678901234567").Success);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPayloadHasExplicitUnavailableCode(string? payload)
        => Assert.Equal(CollectionDataValidationCode.NoPayload, CollectionDataSemanticValidator.ParseDocument(payload, 4096, true).Code);

    [Fact]
    public void CapabilityCoverageRateAndTypeContractsAreIndependentlyEnforced()
    {
        var payload = ConsumerContractTests.Fixture("capabilities-valid.json");
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["sourceCapabilities"]!.AsObject().Remove("mounts"),
            n => n["unlockKeys"]!.AsArray().RemoveAt(0),
            n => n["currentPresenceSources"]!.AsArray().RemoveAt(0),
            n => n["sourceCapabilities"]!["mounts"]!["dataType"] = "Derived",
            n => n["limits"]!["globalReportRate"]!["acceptedPerMinute"] = 59,
            n => n["limits"]!["reporterReportRate"]!["acceptedPerMinute"] = 29,
            n => n["limits"]!["reporterReportRate"]!["burst"] = 9,
        }) Assert.False(CollectionDataSemanticValidator.ValidateCapabilities(ConsumerContractTests.Edit(payload, edit)).Success);
    }

    [Fact]
    public void CompactMapsRejectUnknownKeysMissingUsableSetsAndUnusableIncludedSets()
    {
        var payload = ConsumerContractTests.Fixture("compact-valid-full.json");
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["sourceCapabilities"]!.AsObject().Remove("mounts"),
            n => n["sourceHealth"]!.AsObject().Remove("mounts"),
            n => n["unlocks"]!.AsObject().Remove("mounts"),
            n => n["unlocks"]!["extra"] = new JsonObject { ["unlockedIds"] = new JsonArray() },
            n => n["currentItemPresence"]!["extra"] = new JsonObject { ["items"] = new JsonArray() },
            n => n["sourceHealth"]!["mounts"]!["captureGeneration"] = 0,
            n => n["sourceHealth"]!["mounts"]!["sourceStatus"] = "Stale",
            n => n["sourceCapabilities"]!["mounts"]!["capabilityState"] = "UnavailablePendingValidation",
            n => { n["sourceHealth"]!["mounts"]!["capabilityState"] = "UnavailablePendingValidation"; n["sourceCapabilities"]!["mounts"]!["capabilityState"] = "UnavailablePendingValidation"; },
            n => n["unlocks"]!["mounts"]!["unlockedIds"] = new JsonArray(2, 1),
        }) Assert.False(CollectionDataSemanticValidator.ValidateSnapshot(ConsumerContractTests.Edit(payload, edit), "12345678901234567").Success);
        var missing = ConsumerContractTests.Edit(payload, n =>
        {
            n["unlocks"]!.AsObject().Remove("mounts");
            n["sourceHealth"]!["mounts"]!["sourceStatus"] = "Unavailable";
            n["sourceHealth"]!["mounts"]!["completeness"] = "Unknown";
            n["sourceHealth"]!["mounts"]!["provenanceType"] = "None";
            n["sourceHealth"]!["mounts"]!.AsObject().Remove("observedAtUtc");
        });
        Assert.True(CollectionDataSemanticValidator.ValidateSnapshot(missing, "12345678901234567").Success);
    }

    [Fact]
    public void NoticeOrderingAndIdentityOverflowAreRejectedBeforeDispatch()
    {
        var valid = ConsumerContractTests.Fixture("snapshot-changed-valid.json");
        foreach (var edit in new Action<JsonObject>[]
        {
            n => n["contentId"] = "18446744073709551616",
            n => n["changedDataTypes"] = new JsonArray("Unlocks", "CurrentItemPresence"),
            n => n["changedSources"] = new JsonArray("mounts", "inventory"),
        }) Assert.False(CollectionDataSemanticValidator.ValidateChanged(ConsumerContractTests.Edit(valid, edit)).Success);
    }

    [Fact]
    public void RelicWithoutAchievementUsesPhysicalAbsenceOnlyAndRejectsInventedAchievement()
    {
        var root = NegativeDerived(); Item(root, "relics")["id"] = 2327;
        Facts(root, "relics").RemoveAt(7);
        foreach (var fact in Facts(root, "relics")) fact!["subjectId"] = 2327;
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
        Facts(root, "relics").Add(Absent("achievements", 129));
        Assert.False(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }
}


