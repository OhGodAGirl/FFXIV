using System.Collections.Generic;
using System.Text.Json;

namespace CollectionExporter.Tests;

public sealed class CollectionPayloadTests
{
    [Fact]
    public void AddSortsAndDeduplicatesVerifiedCollection()
    {
        var collections = new SortedDictionary<string, uint[]>();

        CollectionPayload.Add(collections, "cards", [9, 2, 9, 4]);

        Assert.Equal([2U, 4U, 9U], collections["cards"]);
    }

    [Fact]
    public void AddPreservesVerifiedEmptyCollection()
    {
        var collections = new SortedDictionary<string, uint[]>();

        CollectionPayload.Add(collections, "cards", []);

        Assert.Empty(collections["cards"]);
    }

    [Fact]
    public void SerializeDoesNotInventOmittedCollectionKeys()
    {
        var collections = new SortedDictionary<string, uint[]>
        {
            ["cards"] = [2, 4]
        };

        var json = CollectionPayload.Serialize(collections);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("cards", out _));
        Assert.False(document.RootElement.TryGetProperty("outfits", out _));
        Assert.False(document.RootElement.TryGetProperty("relics", out _));
    }

    [Fact]
    public void MergeOwnedKeepsMatchesFromEarlierStoragePass()
    {
        var merged = CollectionPayload.MergeOwned([2, 4], [4, 7]);

        Assert.Equal([2U, 4U, 7U], merged.Order());
    }
}
