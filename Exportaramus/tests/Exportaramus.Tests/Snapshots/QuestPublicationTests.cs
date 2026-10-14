using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class QuestPublicationTests
{
    [Theory]
    [InlineData(false, false, QuestStatus.NotStarted)]
    [InlineData(false, true, QuestStatus.InProgress)]
    [InlineData(true, true, QuestStatus.Completed)]
    public void QuestRowsCombinePermanentCompletionWithCurrentProgress(bool completed, bool active, QuestStatus expected)
    {
        var fixture = SnapshotFixture.Create();
        var state = fixture.State with
        {
            Unlocks = fixture.State.Unlocks.SetItem("quests", fixture.State.Unlocks["quests"] with { UnlockedIds = completed ? [1u] : [] }),
            Quests = active ? [new(1, QuestStatus.InProgress, 7)] : []
        };
        var data = CollectionDataAssembler.Assemble(fixture.Catalog, state);
        var quest = Assert.Single(data.Collections["quests"].Items);
        Assert.Equal(completed, quest.Owned);
        Assert.Equal(expected, quest.Status);
        Assert.Equal(active ? (byte?)7 : null, quest.Sequence);
        using var rich = JsonDocument.Parse(CollectionDataSerializer.Rich(data, "Test", SnapshotFixture.Now));
        var row = rich.RootElement.GetProperty("collections").GetProperty("quests").GetProperty("items")[0];
        Assert.Equal(expected.ToString(), row.GetProperty("status").GetString());
        Assert.Equal(active, row.TryGetProperty("progress", out _));
    }

    [Theory]
    [InlineData("", "Collection data is unavailable.")]
    [InlineData("Window is not ready.", "Window is not ready.")]
    public void OmittedCollectionExplainsItsUnavailableSource(string reason, string expected)
    {
        var fixture = SnapshotFixture.Create();
        var source = fixture.State.Unlocks["mounts"];
        var state = fixture.State with
        {
            Unlocks = fixture.State.Unlocks.SetItem("mounts", source with
            {
                Health = source.Health with { SourceStatus = SourceStatus.Unavailable, Reason = reason }
            })
        };
        var data = CollectionDataAssembler.Assemble(fixture.Catalog, state);
        using var rich = JsonDocument.Parse(CollectionDataSerializer.Rich(data, "Test", SnapshotFixture.Now));
        Assert.False(rich.RootElement.GetProperty("collections").TryGetProperty("mounts", out _));
        Assert.Equal(expected, rich.RootElement.GetProperty("omittedCollections").GetProperty("mounts").GetProperty("message").GetString());
    }
}
