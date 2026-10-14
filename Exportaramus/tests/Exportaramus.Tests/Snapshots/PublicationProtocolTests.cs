using System.Collections.Immutable;
using System.Text.Json;
using Exportaramus.ConsumerKit.V1;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationProtocolTests
{
    [Fact]
    public void PublicationGettersExposeOnlyTheExactAcceptedBytesAndWithdrawIdentity()
    {
        var store = new CollectionDataPublicationStore();
        Assert.Null(store.Current); Assert.Empty(store.GetSnapshot()); Assert.Empty(store.GetCapabilities());
        var first = Bundle(SnapshotFixture.Create(), 1);
        store.SetCapabilities(first.CapabilitiesJson); store.Accept(first);
        Assert.Same(first, store.Current); Assert.Same(first.CompactJson, store.GetSnapshot());
        Assert.Same(first.CapabilitiesJson, store.GetCapabilities());
        var second = Bundle(first.Data, 2); store.Accept(second);
        Assert.Same(second.CompactJson, store.GetSnapshot()); Assert.NotEqual(first.CompactJson, store.GetSnapshot());
        store.Withdraw();
        Assert.Null(store.Current); Assert.Empty(store.GetSnapshot());
        Assert.Same(second.UnavailableReportsCapabilitiesJson, store.GetCapabilities());
        store.Withdraw(); Assert.Same(second.UnavailableReportsCapabilitiesJson, store.GetCapabilities());
    }

    [Theory]
    [InlineData("mounts", "Unlocks")]
    [InlineData("inventory", "CurrentItemPresence")]
    [InlineData("reportedOwnership", "ReportedOwnership")]
    public void ChangedNoticeIdentifiesOnlyTheChangedSourceAndItsDataType(string key, string type)
    {
        var data = SnapshotFixture.Create();
        var before = Bundle(data, 1);
        var state = key switch
        {
            "mounts" => data.State with { Unlocks = data.State.Unlocks.SetItem(key, data.State.Unlocks[key] with { UnlockedIds = [] }) },
            "inventory" => data.State with { CurrentItemPresence = data.State.CurrentItemPresence.SetItem(key, data.State.CurrentItemPresence[key] with { Items = [] }) },
            _ => data.State with { ReportedEvidence = data.State.ReportedEvidence with { Items = [new(100, "Consumer", SnapshotFixture.Now)] } }
        };
        var after = Bundle(CollectionDataAssembler.Assemble(data.Catalog, state), 2);
        var notices = new ChangedNoticeCoalescer();
        notices.Add(after, before, []);
        using var parsed = JsonDocument.Parse(notices.Take()!);
        AssertEventHeader(parsed.RootElement, after);
        Assert.Equal(new[] { key }, Strings(parsed.RootElement, "changedSources"));
        Assert.Equal(new[] { type, "SourceHealth" }.Order(StringComparer.Ordinal), Strings(parsed.RootElement, "changedDataTypes"));
        Assert.Null(notices.Take());
    }

    [Fact]
    public void CapabilityOnlyChangeIsNotClassifiedAsACollectionValueChange()
    {
        var data = SnapshotFixture.Create(); var before = Bundle(data, 1);
        var source = data.State.Unlocks["mounts"];
        var state = data.State with { Unlocks = data.State.Unlocks.SetItem("mounts", source with { Health = source.Health with { CapabilityState = CapabilityState.Degraded } }) };
        var after = Bundle(CollectionDataAssembler.Assemble(data.Catalog, state), 2);
        var notices = new ChangedNoticeCoalescer(); notices.Add(after, before, ["mounts", "mounts", "outfits", "unknown"]);
        using var parsed = JsonDocument.Parse(notices.Take()!);
        Assert.Equal(new[] { "mounts" }, Strings(parsed.RootElement, "changedSources"));
        Assert.Equal(new[] { "SourceCapabilities", "SourceHealth" }, Strings(parsed.RootElement, "changedDataTypes"));
    }

    [Fact]
    public void CoalescingRetainsPriorChangedSourcesAndNewestRevisionThenClearsAllState()
    {
        var first = Bundle(SnapshotFixture.Create(), 1);
        var latest = first with { Revision = 3 };
        var notices = new ChangedNoticeCoalescer();
        notices.Add(first, first, ["mounts"]); notices.Add(latest, first, ["inventory"]);
        using (var parsed = JsonDocument.Parse(notices.Take()!))
        {
            AssertEventHeader(parsed.RootElement, latest);
            Assert.Equal(new[] { "inventory", "mounts" }, Strings(parsed.RootElement, "changedSources"));
        }
        notices.Add(latest, latest, ["minions"]);
        using var next = JsonDocument.Parse(notices.Take()!);
        Assert.Equal(new[] { "minions" }, Strings(next.RootElement, "changedSources"));
        notices.Add(latest, latest, []); Assert.Null(notices.Take());
        notices.Clear(); Assert.Null(notices.Take());
    }

    [Fact]
    public void CharacterChangeDropsQueuedSourcesFromThePreviousCharacter()
    {
        var first = Bundle(SnapshotFixture.Create(), 1);
        var foreign = first with { Data = first.Data with { State = first.Data.State with { ContentId = 124 } }, Revision = 2 };
        var notices = new ChangedNoticeCoalescer();
        notices.Add(first, first, ["mounts"]); notices.Add(foreign, foreign, ["inventory"]);
        using var parsed = JsonDocument.Parse(notices.Take()!);
        AssertEventHeader(parsed.RootElement, foreign);
        Assert.Equal(new[] { "inventory" }, Strings(parsed.RootElement, "changedSources"));
        Assert.Equal(new[] { "SourceHealth" }, Strings(parsed.RootElement, "changedDataTypes"));
    }

    [Fact]
    public void InitialNoticeAdvertisesEveryCompactSourceAndNoDerivedOnlySource()
    {
        var first = Bundle(SnapshotFixture.Create(), 1); var notices = new ChangedNoticeCoalescer(); notices.Add(first, null, []);
        using var parsed = JsonDocument.Parse(notices.Take()!);
        AssertEventHeader(parsed.RootElement, first);
        Assert.Equal(CollectionKeys.Compact.Where(key => key is not ("facewear" or "beasts")).Order(StringComparer.Ordinal), Strings(parsed.RootElement, "changedSources"));
        Assert.Equal(new[] { "CurrentItemPresence", "ReportedOwnership", "SourceCapabilities", "SourceHealth", "Unlocks" }, Strings(parsed.RootElement, "changedDataTypes"));
    }

    private static AcceptedSnapshotBundle Bundle(CollectionData data, long revision) => SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, revision), null, true);
    private static string[] Strings(JsonElement value, string property) => value.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
    private static void AssertEventHeader(JsonElement value, AcceptedSnapshotBundle expected)
    {
        Assert.True(CollectionDataSemanticValidator.ValidateChanged(value.GetRawText()).Success);
        Assert.Equal("SnapshotChanged", value.GetProperty("eventType").GetString());
        Assert.Equal("1.0", value.GetProperty("schemaVersion").GetString());
        Assert.Equal(expected.Data.State.ProviderSessionId, value.GetProperty("providerSessionId").GetGuid());
        Assert.Equal(expected.Revision, value.GetProperty("revision").GetInt64());
        Assert.Equal(expected.Data.State.ContentId.ToString(), value.GetProperty("contentId").GetString());
        Assert.Equal(expected.Data.State.CapturedAtUtc, value.GetProperty("capturedAtUtc").GetDateTimeOffset());
    }
}
