using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;
using Kit = Exportaramus.ConsumerKit.V1_2;

namespace BetterGlamorPlates.Tests;

/// <summary>Projection never treats missing qualification or identity as usable ownership.</summary>
public sealed class ProjectionBoundaryTests
{
    private const ulong Character = 12345678901234567;

    /// <summary>Each source qualification is independently required.</summary>
    [Theory]
    [InlineData("health")]
    [InlineData("capability")]
    [InlineData("capability-state")]
    [InlineData("health-state")]
    [InlineData("presence")]
    [InlineData("time")]
    [InlineData("partial")]
    [InlineData("unavailable")]
    public void MissingSourceQualificationIsNotCollected(string missing)
    {
        var snapshot = Snapshot();
        const string key = "glamourDresser";
        snapshot = missing switch
        {
            "health" => snapshot with { SourceHealth = snapshot.SourceHealth.Remove(key) },
            "capability" => snapshot with { SourceCapabilities = snapshot.SourceCapabilities.Remove(key) },
            "capability-state" => snapshot with { SourceCapabilities = snapshot.SourceCapabilities.SetItem(key, snapshot.SourceCapabilities[key] with { CapabilityState = "Unsupported" }) },
            "health-state" => snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, snapshot.SourceHealth[key] with { CapabilityState = "Unsupported" }) },
            "partial" => snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, snapshot.SourceHealth[key] with { Completeness = "Partial" }) },
            "unavailable" => snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, snapshot.SourceHealth[key] with { SourceStatus = "Unavailable" }) },
            "presence" => snapshot with { CurrentItemPresence = snapshot.CurrentItemPresence.Remove(key) },
            _ => snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, snapshot.SourceHealth[key] with { ObservedAtUtc = "invalid" }) },
        };
        var result = ExportaramusProjection.Project(Ready(snapshot), Character);
        Assert.True(result.Accepted);
        Assert.Empty(result.Snapshot!.GlamourDresser.ItemIds);
        Assert.Equal(missing == "time" ? CollectionSourceStatus.NeverObserved : CollectionSourceStatus.Unavailable,
            result.Snapshot.GlamourDresser.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Snapshot.GlamourDresser.Reason));
    }

    /// <summary>Identity and provider availability are required before projecting any source.</summary>
    [Fact]
    public void UnavailableIdentityOrSnapshotRejectsProjection()
    {
        var ready = Ready(Snapshot());
        Assert.Equal(CollectionDataFailure.NoCharacter, ExportaramusProjection.Project(ready, 0).Failure);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, ExportaramusProjection.Project(ready with { Snapshot = null }, Character).Failure);
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, ExportaramusProjection.Project(ready with { Availability = Kit.CollectionDataAvailability.Unavailable }, Character).Failure);
    }

    /// <summary>Collection truncation uses deterministic item-ID tie-breaking.</summary>
    [Fact]
    public void CollectionCapsAtTwoThousandAfterStableSort()
    {
        var ids = Enumerable.Range(1, 2001).Select(value => (uint)value).Reverse().ToArray();
        var snapshot = TestDomainValues.SharedSnapshot(dresserItems: ids);
        var result = CollectionViewPlanner.Create(snapshot, PlateSlot.Body, null,
            id => new ItemDisplayFacts("Same name", id, TestDomainValues.AllSlots));
        Assert.Equal(Enumerable.Range(1, 2000).Select(value => (uint)value), result.Select(item => item.ItemId));
        Assert.Equal(2001, CollectionViewPlanner.CountKnownOwnedItems(snapshot));
        Assert.Equal(0, CollectionViewPlanner.CountKnownOwnedItems(null));
    }

    /// <summary>Each named presence source preserves IDs, freshness and observation time.</summary>
    [Theory]
    [InlineData("Fresh")]
    [InlineData("Stale")]
    public void AllQualifiedSourcesPreserveObservation(string status)
    {
        var snapshot = Snapshot();
        var pairs = new Dictionary<CollectionSourceName, string>
        {
            [CollectionSourceName.Inventory] = "inventory",
            [CollectionSourceName.ArmoryChest] = "armoryChest",
            [CollectionSourceName.ChocoboSaddlebag] = "chocoboSaddlebag",
            [CollectionSourceName.PremiumSaddlebag] = "premiumSaddlebag",
            [CollectionSourceName.Retainers] = "retainers",
            [CollectionSourceName.Armoire] = "armoire",
            [CollectionSourceName.GlamourDresser] = "glamourDresser",
        };
        var health = snapshot.SourceHealth["glamourDresser"] with { SourceStatus = status };
        var capability = snapshot.SourceCapabilities["glamourDresser"];
        var presence = snapshot.CurrentItemPresence["glamourDresser"];
        foreach (var key in pairs.Values)
            snapshot = snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, health), SourceCapabilities = snapshot.SourceCapabilities.SetItem(key, capability), CurrentItemPresence = snapshot.CurrentItemPresence.SetItem(key, presence) };
        var result = ExportaramusProjection.Project(Ready(snapshot), Character);
        Assert.True(result.Accepted);
        foreach (var pair in pairs)
        {
            var source = result.Snapshot!.GetSource(pair.Key);
            Assert.Equal(status == "Fresh" ? CollectionSourceStatus.Fresh : CollectionSourceStatus.Stale, source.Status);
            Assert.Equal(presence.Items.Select(item => item.ItemId).Distinct().Order(), source.ItemIds);
            Assert.Equal(DateTimeOffset.Parse(health.ObservedAtUtc!, System.Globalization.CultureInfo.InvariantCulture), source.ObservedAtUtc);
        }
        Assert.Equal(CollectionDataFailure.WrongCharacter, ExportaramusProjection.Project(Ready(snapshot), Character + 1).Failure);
        Assert.Throws<OverflowException>(() => ExportaramusProjection.Project(Ready(snapshot with { Revision = -1 }), Character));
    }

    private static Kit.CollectionDataSnapshot Snapshot()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "compact-valid-full.json"));
        return Kit.CollectionDataSemanticValidator.ValidateSnapshot(json, Character.ToString(System.Globalization.CultureInfo.InvariantCulture)).Value!;
    }
    private static Kit.CollectionDataClientView Ready(Kit.CollectionDataSnapshot snapshot)
        => new(Kit.CollectionDataAvailability.Ready, snapshot, snapshot.ProviderSessionId, snapshot.ContentId,
            snapshot.Revision, Kit.CollectionDataValidationCode.None);
}
