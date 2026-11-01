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
}
