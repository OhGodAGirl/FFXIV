using System.Text.Json;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublicationOrderingAndLogoutContractTests
{
    [Fact]
    public void ConfirmedLogoutClearsEstablishedScopeEvenWhenTheIdentitySampleRetainsItsLastValue()
    {
        var identity = new SnapshotWithdrawalPolicy();
        Assert.Null(identity.Observe(123, false, false, 0));
        Assert.Equal(123ul, identity.ActiveContentId);

        var reason = identity.Observe(123, true, false, 1);

        Assert.Equal("LoggedOut", reason);
        Assert.Equal(0ul, identity.ActiveContentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicSnapshotAndCapabilityMapsUseOrdinalKeysForFullAndPartiallyUnavailableData(bool partial)
    {
        // Section 18 makes ordinal sorting a semantic output rule outside JSON Schema.
        var data = SnapshotFixture.Create();
        string[] omitted = partial ? ["mounts", "minions"] : [];
        if (partial)
        {
            var unlocks = data.State.Unlocks;
            foreach (var key in omitted)
                unlocks = unlocks.SetItem(key, unlocks[key] with
                {
                    UnlockedIds = [],
                    Health = SnapshotFixture.Health(DataType.PermanentUnlock, SourceStatus.Unavailable)
                });
            data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
            {
                CaptureType = CaptureType.Lightweight,
                Unlocks = unlocks
            });
        }

        using var compact = JsonDocument.Parse(CollectionDataSerializer.Compact(data, 1));
        using var rich = JsonDocument.Parse(CollectionDataSerializer.Rich(data, "Fixture", SnapshotFixture.Now));
        using var capabilities = JsonDocument.Parse(CapabilityDocumentWriter.Build(data, true));

        AssertOrdinalMap(compact.RootElement, "unlocks", CollectionKeys.Unlocks.Where(key => key is not ("facewear" or "beasts")).Except(omitted));
        AssertOrdinalMap(compact.RootElement, "currentItemPresence", CollectionKeys.Presence);
        AssertOrdinalMap(compact.RootElement, "sourceCapabilities", CollectionKeys.Compact.Where(key => key is not ("facewear" or "beasts")));
        AssertOrdinalMap(compact.RootElement, "sourceHealth", CollectionKeys.Compact.Where(key => key is not ("facewear" or "beasts")));
        AssertOrdinalMap(rich.RootElement, "collections", CollectionKeys.Unlocks.Where(key => key is not ("facewear" or "beasts")).Concat(CollectionKeys.RichOnly).Except(omitted));
        AssertOrdinalMap(rich.RootElement, "omittedCollections", omitted);
        AssertOrdinalMap(rich.RootElement, "currentItemPresence", CollectionKeys.Presence);
        AssertOrdinalMap(rich.RootElement, "sourceCapabilities", CollectionKeys.All.Where(key => key is not ("facewear" or "beasts")));
        AssertOrdinalMap(rich.RootElement, "sourceHealth", CollectionKeys.All.Where(key => key is not ("facewear" or "beasts")));
        AssertOrdinalMap(capabilities.RootElement, "sourceCapabilities", CollectionKeys.Compact.Where(key => key is not ("facewear" or "beasts")));
        Assert.Equal(partial ? "MixedObservationTimes" : "Coherent", rich.RootElement.GetProperty("consistency").GetString());
    }

    private static void AssertOrdinalMap(JsonElement root, string name, IEnumerable<string> expectedKeys)
    {
        var expected = expectedKeys.Order(StringComparer.Ordinal).ToArray();
        var actual = root.GetProperty(name).EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(expected, actual);
    }
}
