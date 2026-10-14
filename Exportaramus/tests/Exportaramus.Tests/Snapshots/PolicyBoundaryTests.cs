using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class PolicyBoundaryTests
{
    [Fact]
    public void DerivedRelicTruthTableRequiresKnownAbsenceFromBothAlternatives()
    {
        var data = SnapshotFixture.Create();
        foreach (var present in new bool?[] { true, false, null })
            foreach (var achievement in new bool?[] { true, false, null })
                foreach (var link in new[] { true, false })
                {
                    var sources = data.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
                    {
                        Items = present == true ? [new(1675, pair.Value.SupportsQuantity ? 1u : null)] : [],
                        Health = present is null ? pair.Value.Health with
                        { SourceStatus = SourceStatus.Unavailable } : pair.Value.Health
                    });
                    var unlock = data.State.Unlocks["achievements"] with
                    {
                        UnlockedIds = achievement == true ? [129u] : [],
                        Health = achievement is null
                        ? data.State.Unlocks["achievements"].Health with { SourceStatus = SourceStatus.Unavailable } : data.State.Unlocks["achievements"].Health
                    };
                    var state = data.State with { CurrentItemPresence = sources, Unlocks = data.State.Unlocks.SetItem("achievements", unlock) };
                    var definition = new CollectionDefinition("relics", "Relics", DataType.Derived, true, [new(1675, 1675, "Relic", false, AchievementId: link ? 129u : null)]);
                    var result = DerivedCollectionPolicy.Derive(definition, state);
                    bool? expected = !link ? present : present == true || achievement == true ? true : present == false && achievement == false ? false : null;
                    Assert.Equal(expected is not null, result.Health.IsUsable);
                    if (expected is { } value) Assert.Equal(value, Assert.Single(result.Items).Owned);
                }
    }
    [Fact]
    public void DerivedMappingRejectsMissingConstituentsAndCarriesStaleDegradedInputs()
    {
        var data = SnapshotFixture.Create(); var definition = new CollectionDefinition("outfits", "Outfits", DataType.Derived, true, []);
        Assert.Equal(ReasonCode.MappingValidationFailed, DerivedCollectionPolicy.Derive(definition, data.State).Health.ReasonCode);
        definition = definition with { Items = [new(1, 1, "Outfit", false)] };
        Assert.Equal(ReasonCode.MappingValidationFailed, DerivedCollectionPolicy.Derive(definition, data.State).Health.ReasonCode);
        definition = definition with { Items = [new(1, 1, "Outfit", false, RequiredItemIds: [100u])] };
        var inventory = data.State.CurrentItemPresence["inventory"];
        var state = data.State with
        {
            CurrentItemPresence = data.State.CurrentItemPresence.SetItem("inventory", inventory with
            { Health = inventory.Health with { SourceStatus = SourceStatus.Stale, CapabilityState = CapabilityState.Degraded } })
        };
        var result = DerivedCollectionPolicy.Derive(definition, state);
        Assert.True(Assert.Single(result.Items).Owned); Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
        Assert.Equal(CapabilityState.Degraded, result.Health.CapabilityState);
    }
    [Fact]
    public void RetentionTruthTableNeverUsesMissingOrInvalidPriorEvidence()
    {
        var health = SnapshotFixture.Health(DataType.CurrentItemPresence);
        var fresh = new CurrentItemPresenceState([new(100, 2)], true, health);
        var failed = fresh with { Health = health with { SourceStatus = SourceStatus.Unavailable, ReasonCode = ReasonCode.CaptureFailed } };
        Assert.Equal(SourceStatus.Stale, CollectionStatePolicy.Resolve(fresh, failed, true, true).Health.SourceStatus);
        foreach (var prior in new CurrentItemPresenceState?[] { null, failed })
            Assert.Empty(CollectionStatePolicy.Resolve(prior, failed, true, true).Items);
        foreach (var (same, retain) in new[] { (false, true), (true, false) })
            Assert.Empty(CollectionStatePolicy.Resolve(fresh, failed, same, retain).Items);
        Assert.Equal(new uint[] { 1, 2 }, CollectionStatePolicy.Resolve(null, new PermanentUnlockState([2, 0, 1, 2], health with { DataType = DataType.PermanentUnlock }), true, false).UnlockedIds);
        Assert.Throws<InvalidDataException>(() => CollectionStatePolicy.Restore(SnapshotFixture.Create().State, 0, Guid.NewGuid(), SnapshotFixture.Now));
    }
    [Fact]
    public void ReporterRawWhitespaceDoesNotChangeNormalizedByteContract()
    {
        Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, new string(' ', 300) + "A" + new string(' ', 300), out _, out var label));
        Assert.Equal("A", label);
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize(null, 1, "A", out _, out _));
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 1, null, out _, out _));
        var limiter = new ReporterRateLimiter();
        for (var i = 0; i < 1000; i++) Assert.True(limiter.TryAccept(i.ToString(), i * 2d));
    }
    [Fact]
    public void QueueLimitsAreIndependentOfRetainedCapacityAndRate()
    {
        var queue = new ReportedEvidenceQueue(); queue.SetScope(123, Enumerable.Range(1, 300).Select(i => (uint)i).ToImmutableHashSet(), []);
        for (var i = 1; i <= 32; i++) Assert.Equal("Accepted", queue.TryEnqueue("123", (uint)i, "One", i * 3d, SnapshotFixture.Now));
        Assert.Equal("QueueFull", queue.TryEnqueue("123", 33, "One", 100, SnapshotFixture.Now));
        for (var i = 33; i <= 256; i++) Assert.Equal("Accepted", queue.TryEnqueue("123", (uint)i, i.ToString(), i * 3d, SnapshotFixture.Now));
        Assert.Equal("QueueFull", queue.TryEnqueue("123", 257, "Other", 900, SnapshotFixture.Now));
        queue.SetScope(123, [1], []); for (var i = 0; i < 10; i++) Assert.Equal("Accepted", queue.TryEnqueue("123", 1, "One", 0, SnapshotFixture.Now));
        Assert.Equal("RateLimited", queue.TryEnqueue("123", 1, "One", 0, SnapshotFixture.Now));
    }
    [Fact]
    public void FailedLightweightAndDueFullRetainTheirIndependentCadence()
    {
        var schedule = new SnapshotSchedulePolicy(); schedule.Login(0); schedule.Failed(CaptureRequest.AutomaticFull, 15, false);
        schedule.Tick(44); Assert.Equal(CaptureRequest.None, schedule.Take()); schedule.Tick(45); Assert.Equal(CaptureRequest.AutomaticFull, schedule.Take());
        schedule.Accepted(true, 45); schedule.Failed(CaptureRequest.AutomaticLightweight, 345, false);
        schedule.Tick(374); Assert.Equal(CaptureRequest.None, schedule.Take()); schedule.Tick(375); Assert.Equal(CaptureRequest.AutomaticLightweight, schedule.Take());
        schedule.Failed(CaptureRequest.None, 400, false); schedule.Failed(CaptureRequest.AutomaticFull, 400, true);
        var identity = new SnapshotWithdrawalPolicy(); Assert.Null(identity.Observe(123, true, false, 0)); Assert.Equal(0ul, identity.ActiveContentId);
    }
}
