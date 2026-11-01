using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class OwnershipEvidenceValidationTests
{
    private static JsonObject Snapshot()
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!.AsObject();
        AssertRejected(root, false);
        return root;
    }

    private static JsonObject Item(JsonObject root, string key) => root["collections"]![key]!["items"]![0]!.AsObject();

    private static void AssertRejected(JsonObject root, bool rejected)
    {
        var json = root.ToJsonString();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(!rejected, CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.Equal(!rejected, CollectionDataSemanticValidator.ValidateRich(json).Success);
    }

    [Theory]
    [InlineData("missing-status")]
    [InlineData("incomplete")]
    [InlineData("wrong-rule")]
    public void OwnedQuestsRequireCompletionEvidence(string change)
    {
        var root = Snapshot();
        var item = Item(root, "quests");
        if (change == "missing-status") item.Remove("status");
        else if (change == "incomplete") item["status"] = "Incomplete";
        else item["ownershipEvidence"]!["ruleCode"] = "DirectPermanentUnlock";
        AssertRejected(root, true);
    }

    [Theory]
    [InlineData("description")]
    [InlineData("points")]
    public void QuestsRejectUnsupportedDetailFields(string field)
    {
        var root = Snapshot();
        Item(root, "quests")[field] = "Unsupported detail";
        AssertRejected(root, true);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("true")]
    [InlineData("false")]
    public void AchievementRewardCollectionMustRemainUnknown(string change)
    {
        var root = Snapshot();
        var item = Item(root, "achievements");
        if (change == "missing") item.Remove("rewardCollected");
        else item["rewardCollected"] = change == "true";
        AssertRejected(root, true);
    }

    [Theory]
    [InlineData("unknown-rule")]
    [InlineData("extra-fact")]
    [InlineData("wrong-source")]
    [InlineData("wrong-subject")]
    public void DresserOwnershipRequiresOneMatchingSourceFact(string change)
    {
        var root = Snapshot();
        var evidence = Item(root, "glamour_dresser")["ownershipEvidence"]!;
        var facts = evidence["facts"]!.AsArray();
        if (change == "unknown-rule") evidence["ruleCode"] = "Unknown";
        else if (change == "extra-fact") facts.Add(facts[0]!.DeepClone());
        else if (change == "wrong-source") facts[0]!["sourceKey"] = "armoire";
        else
        {
            facts[0]!["subjectId"] = 101;
            root["currentItemPresence"]!["glamourDresser"]!["items"]!.AsArray().Add(new JsonObject { ["itemId"] = 101 });
        }
        AssertRejected(root, true);
    }
}
