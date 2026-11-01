using System.Text.Json;
using System.Text.Json.Nodes;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class DresserEntryValidationTests
{
    private static JsonObject Snapshot(bool outfit = false)
    {
        var root = JsonNode.Parse(ContractFixture.Read("expanded-valid-beasts-populated.json"))!.AsObject();
        root["currentItemPresence"]!["glamourDresser"]!["dresserEntries"] = new JsonArray(new JsonObject
        {
            ["entryIndex"] = 0,
            ["rawItemId"] = 100,
            ["baseItemId"] = 100,
            ["outfitId"] = outfit ? JsonValue.Create(100) : null,
            ["components"] = new JsonArray(new JsonObject { ["slotIndex"] = 0, ["itemId"] = 100 }),
        });
        AssertAccepted(root, true);
        return root;
    }

    private static JsonArray Entries(JsonObject root) => root["currentItemPresence"]!["glamourDresser"]!["dresserEntries"]!.AsArray();

    private static void AssertAccepted(JsonObject root, bool accepted)
    {
        var json = root.ToJsonString();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(document.RootElement));
        Assert.Equal(accepted, CollectionDataSemanticValidator.ValidateRich(json).Success);
    }

    [Theory]
    [InlineData("entryIndex", -1)]
    [InlineData("entryIndex", 800)]
    [InlineData("rawItemId", 101)]
    [InlineData("baseItemId", 0)]
    [InlineData("outfitId", 101)]
    public void EntryIdentityMustBeConsistent(string field, int value)
    {
        var root = Snapshot();
        Entries(root)[0]![field] = value;
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData(false, "slotIndex", 1)]
    [InlineData(false, "itemId", 101)]
    [InlineData(true, "slotIndex", -1)]
    [InlineData(true, "slotIndex", 11)]
    [InlineData(true, "itemId", 0)]
    public void ComponentsMustUseValidSlotsAndItems(bool outfit, string field, int value)
    {
        var root = Snapshot(outfit);
        Entries(root)[0]!["components"]![0]![field] = value;
        AssertAccepted(root, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateComponentSlotsAreRejected(bool outfit)
    {
        var root = Snapshot(outfit);
        var components = Entries(root)[0]!["components"]!.AsArray();
        components.Add(components[0]!.DeepClone());
        AssertAccepted(root, false);
    }

    [Fact]
    public void EntryIndicesCannotRepeat()
    {
        var root = Snapshot();
        var entries = Entries(root);
        entries.Add(entries[0]!.DeepClone());
        AssertAccepted(root, false);
    }

    [Fact]
    public void DresserAcceptsEightHundredEntriesButNotMore()
    {
        var root = Snapshot();
        var entries = Entries(root);
        for (var index = 1; index < 800; index++)
        {
            var entry = entries[0]!.DeepClone();
            entry["entryIndex"] = index;
            entries.Add(entry);
        }
        AssertAccepted(root, true);
        var extra = entries[0]!.DeepClone();
        extra["entryIndex"] = 800;
        entries.Add(extra);
        AssertAccepted(root, false);
    }
}
