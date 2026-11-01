using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class RichCollectionConsistencyTests
{
    private static JsonObject Snapshot()
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!.AsObject();
        AssertAccepted(root, true);
        return root;
    }

    private static void AssertAccepted(JsonObject root, bool accepted)
    {
        var json = root.ToJsonString();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(json).Success);
    }

    [Theory]
    [InlineData("ownershipKind", "Derived")]
    [InlineData("sourceStatus", "Stale")]
    [InlineData("completeness", "Partial")]
    [InlineData("provenanceType", "Restored")]
    [InlineData("observedAtUtc", "2026-09-05T12:00:00.0000000Z")]
    public void CollectionMetadataMustAgreeWithItsSource(string field, string value)
    {
        var root = Snapshot();
        root["collections"]!["glamour_dresser"]![field] = value;
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryCollectionMustAppearExactlyOnce(bool duplicate)
    {
        var root = Snapshot();
        if (duplicate) root["omittedCollections"]!["glamour_dresser"] = root["omittedCollections"]!["outfits"]!.DeepClone();
        else root["collections"]!.AsObject().Remove("glamour_dresser");
        AssertAccepted(root, false);
    }

    [Fact]
    public void CollectionItemIdsCannotRepeat()
    {
        var root = Snapshot();
        var items = root["collections"]!["glamour_dresser"]!["items"]!.AsArray();
        items.Add(items[0]!.DeepClone());
        AssertAccepted(root, false);
    }

    [Fact]
    public void UsablePresenceSourceRequiresItsItemSet()
    {
        var root = Snapshot();
        root["currentItemPresence"]!.AsObject().Remove("inventory");
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Restored")]
    [InlineData("Derived")]
    public void FreshBeastDataRequiresObservedProvenance(string provenance)
    {
        var root = Snapshot();
        root["sourceHealth"]!["beasts"]!["provenanceType"] = provenance;
        root["collections"]!["beasts"]!["provenanceType"] = provenance;
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    [InlineData(0, 100)]
    public void PresenceItemIdsMustBePositiveAndStrictlyIncreasing(int first, int second)
    {
        var root = Snapshot();
        root["currentItemPresence"]!["inventory"]!["items"] = new JsonArray(
            new JsonObject { ["itemId"] = 100, ["quantity"] = 1 }, new JsonObject { ["itemId"] = 200, ["quantity"] = 1 });
        AssertAccepted(root, true);
        root["currentItemPresence"]!["inventory"]!["items"]![0]!["itemId"] = first;
        root["currentItemPresence"]!["inventory"]!["items"]![1]!["itemId"] = second;
        AssertAccepted(root, false);
    }

    [Fact]
    public void PresenceSourcesMustBeRecognized()
    {
        var root = Snapshot();
        root["currentItemPresence"]!["unknown"] = root["currentItemPresence"]!["inventory"]!.DeepClone();
        AssertAccepted(root, false);
    }
    [Fact]
    public void UnavailableCollectionsCannotContainCapturedItems()
    {
        var root = Snapshot();
        root["sourceHealth"]!["mounts"]!["sourceStatus"] = "Unavailable";
        root["collections"]!["mounts"]!["sourceStatus"] = "Unavailable";
        AssertAccepted(root, false);
    }

    [Fact]
    public void PermanentUnlocksCannotUseCurrentItemOwnership()
    {
        var root = Snapshot();
        root["collections"]!["mounts"]!["items"]![0]!["ownershipEvidence"] =
            root["collections"]!["glamour_dresser"]!["items"]![0]!["ownershipEvidence"]!.DeepClone();
        AssertAccepted(root, false);
    }
}
