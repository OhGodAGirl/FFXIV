using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class DerivedEvidenceTests
{
    private static readonly uint[] OutfitItems = [2642, 2965, 3747];

    private static JsonObject Fact(string source, uint id, bool present) => new()
    {
        ["dataType"] = source == "achievements" ? "PermanentUnlock" : "CurrentItemPresence",
        ["sourceKey"] = source,
        ["subjectId"] = id,
        ["state"] = present ? "Present" : "Absent",
        ["sourceStatus"] = "Fresh",
    };

    private static JsonObject Snapshot(string key, uint id, bool owned, JsonArray facts)
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!.AsObject();
        var health = root["sourceHealth"]!["inventory"]!.DeepClone().AsObject();
        health["dataType"] = "Derived";
        root["sourceHealth"]![key] = health;
        root["omittedCollections"]!.AsObject().Remove(key);
        var collection = root["collections"]!["glamour_dresser"]!.DeepClone().AsObject();
        collection["label"] = key;
        collection["ownershipKind"] = "Derived";
        collection["items"] = new JsonArray(new JsonObject
        {
            ["id"] = id,
            ["name"] = "Derived fixture",
            ["owned"] = owned,
            ["ownershipEvidence"] = new JsonObject
            {
                ["ruleCode"] = key == "outfits" ? "AllRequiredConstituents" : "PermanentUnlockOrCurrentItemPresence",
                ["facts"] = facts,
            },
        });
        root["collections"]![key] = collection;
        foreach (var fact in facts)
        {
            var source = fact!["sourceKey"]!.GetValue<string>();
            if (source == "achievements" || fact["state"]!.GetValue<string>() != "Present") continue;
            var items = root["currentItemPresence"]![source]!["items"]!.AsArray();
            items.Add(new JsonObject { ["itemId"] = fact["subjectId"]!.DeepClone(), ["quantity"] = 1 });
        }
        return root;
    }

    private static JsonObject Item(JsonObject root, string key) => root["collections"]![key]!["items"]![0]!.AsObject();
    private static JsonArray Facts(JsonObject root, string key) => Item(root, key)["ownershipEvidence"]!["facts"]!.AsArray();

    private static JsonObject Outfit(bool owned)
    {
        var facts = new JsonArray();
        foreach (var id in OutfitItems)
        {
            if (owned) facts.Add(Fact("inventory", id, true));
            else foreach (var source in CollectionDataSemanticValidator.PresenceKeys) facts.Add(Fact(source, id, false));
        }
        return Snapshot("outfits", 45094, owned, facts);
    }

    private static JsonObject Relic(bool owned, bool achievement)
    {
        var facts = new JsonArray();
        if (owned) facts.Add(Fact(achievement ? "achievements" : "inventory", achievement ? 129u : 1675u, true));
        else
        {
            foreach (var source in CollectionDataSemanticValidator.PresenceKeys) facts.Add(Fact(source, 1675, false));
            facts.Add(Fact("achievements", 129, false));
        }
        return Snapshot("relics", 1675, owned, facts);
    }

    private static void AssertValid(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.True(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.True(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }

    private static void AssertInvalid(JsonObject root)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        Assert.False(CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.False(CollectionDataSemanticValidator.ValidateRich(root.ToJsonString()).Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutfitRequiresEveryConstituentOrCompleteAbsenceEvidence(bool owned) => AssertValid(Outfit(owned));

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void RelicAcceptsAchievementPresenceOrCompleteAbsence(bool owned, bool achievement) => AssertValid(Relic(owned, achievement));

    [Theory]
    [InlineData("missing-constituent")]
    [InlineData("extra-constituent")]
    [InlineData("unknown-outfit")]
    [InlineData("wrong-owned")]
    [InlineData("wrong-rule")]
    [InlineData("wrong-type")]
    [InlineData("unknown-state")]
    [InlineData("unavailable-source")]
    [InlineData("stale-fact")]
    [InlineData("contradictory-presence")]
    public void OutfitRejectsInconsistentEvidence(string change)
    {
        var root = Outfit(true);
        AssertValid(root);
        var facts = Facts(root, "outfits");
        switch (change)
        {
            case "missing-constituent": facts.RemoveAt(0); break;
            case "extra-constituent": facts.Add(Fact("inventory", 999999, false)); break;
            case "unknown-outfit": Item(root, "outfits")["id"] = 999999; break;
            case "wrong-owned": Item(root, "outfits")["owned"] = false; break;
            case "wrong-rule": Item(root, "outfits")["ownershipEvidence"]!["ruleCode"] = "DirectPermanentUnlock"; break;
            case "wrong-type": facts[0]!["dataType"] = "PermanentUnlock"; break;
            case "unknown-state": facts[0]!["state"] = "Unknown"; break;
            case "unavailable-source": facts[0]!["sourceStatus"] = "Unavailable"; break;
            case "stale-fact": facts[0]!["sourceStatus"] = "Stale"; break;
            case "contradictory-presence": facts[0]!["state"] = "Absent"; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        AssertInvalid(root);
    }

    [Theory]
    [InlineData("outfits")]
    [InlineData("relics")]
    public void AbsenceRequiresEveryPresenceSource(string key)
    {
        var root = key == "outfits" ? Outfit(false) : Relic(false, false);
        AssertValid(root);
        Facts(root, key).RemoveAt(0);
        AssertInvalid(root);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelicEvidenceMustMatchActualItemPresence(bool owned)
    {
        var root = Relic(owned, false);
        AssertValid(root);
        var items = root["currentItemPresence"]!["inventory"]!["items"]!.AsArray();
        if (owned) items.Clear();
        else items.Add(new JsonObject { ["itemId"] = 1675, ["quantity"] = 1 });
        AssertInvalid(root);
    }

    [Fact]
    public void RelicAbsenceRequiresItsAchievementEvidence()
    {
        var root = Relic(false, false);
        AssertValid(root);
        var facts = Facts(root, "relics");
        facts.RemoveAt(facts.Count - 1);
        AssertInvalid(root);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelicRejectsEvidenceForAnotherSubject(bool achievement)
    {
        var root = Relic(true, achievement);
        AssertValid(root);
        Facts(root, "relics")[0]!["subjectId"] = 100;
        AssertInvalid(root);
    }

    [Fact]
    public void RelicRejectsAnUnknownCatalogEntry()
    {
        var root = Relic(true, true);
        AssertValid(root);
        Item(root, "relics")["id"] = 999999;
        AssertInvalid(root);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelicWithoutAchievementUsesOnlyItemPresence(bool owned)
    {
        var facts = new JsonArray();
        if (owned) facts.Add(Fact("inventory", 2327, true));
        else foreach (var source in CollectionDataSemanticValidator.PresenceKeys) facts.Add(Fact(source, 2327, false));
        var root = Snapshot("relics", 2327, owned, facts);
        AssertValid(root);
        facts.Add(Fact("achievements", 129, owned));
        AssertInvalid(root);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutfitCanCombineEvidenceFromDifferentSources(bool lastPresent)
    {
        var facts = new JsonArray
        {
            Fact("inventory", 2642, true),
            Fact("armoryChest", 2965, true),
        };
        if (lastPresent) facts.Add(Fact("chocoboSaddlebag", 3747, true));
        else foreach (var source in CollectionDataSemanticValidator.PresenceKeys) facts.Add(Fact(source, 3747, false));
        var root = Snapshot("outfits", 45094, lastPresent, facts);
        AssertValid(root);
        Item(root, "outfits")["owned"] = !lastPresent;
        AssertInvalid(root);
    }

    [Theory]
    [InlineData("outfits", "PermanentUnlockOrCurrentItemPresence")]
    [InlineData("relics", "AllRequiredConstituents")]
    public void DerivedRulesCannotBeSwappedBetweenCollections(string key, string rule)
    {
        var root = key == "outfits" ? Outfit(true) : Relic(true, true);
        AssertValid(root);
        Item(root, key)["ownershipEvidence"]!["ruleCode"] = rule;
        AssertInvalid(root);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutfitPresenceCanCoexistWithAbsenceInAnotherSource(bool reverseOrder)
    {
        var facts = new JsonArray();
        foreach (var id in OutfitItems)
        {
            facts.Add(Fact(reverseOrder ? "armoryChest" : "inventory", id, !reverseOrder));
            facts.Add(Fact(reverseOrder ? "inventory" : "armoryChest", id, reverseOrder));
        }
        var root = Snapshot("outfits", 45094, true, facts);
        AssertValid(root);
        Item(root, "outfits")["owned"] = false;
        AssertInvalid(root);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelicOwnershipNeedsOnlyOnePositiveSource(bool achievementPresent)
    {
        var facts = new JsonArray
        {
            Fact("achievements", 129, achievementPresent),
            Fact("inventory", 1675, !achievementPresent),
        };
        var root = Snapshot("relics", 1675, true, facts);
        AssertValid(root);
        Item(root, "relics")["owned"] = false;
        AssertInvalid(root);
    }

    [Fact]
    public void OutfitRejectsAchievementEvidenceAlongsideItemPresence()
    {
        var root = Outfit(true);
        AssertValid(root);
        Facts(root, "outfits").Add(Fact("achievements", OutfitItems[0], true));
        AssertInvalid(root);
    }
}
