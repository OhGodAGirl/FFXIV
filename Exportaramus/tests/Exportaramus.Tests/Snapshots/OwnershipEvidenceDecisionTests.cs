using System.Collections.Immutable;
using Exportaramus.Features.CollectionData;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.CollectionData.Contracts;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class OwnershipEvidenceDecisionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelicEvidencePreservesIndependentPermanentUnlockAndCompletePhysicalAbsence(bool unlocked)
    {
        var fixture = SnapshotFixture.Create();
        var state = fixture.State with
        {
            CurrentItemPresence = fixture.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Items = [] }),
            Unlocks = fixture.State.Unlocks.SetItem("achievements", fixture.State.Unlocks["achievements"] with { UnlockedIds = unlocked ? [129u] : [] })
        };

        var result = DerivedCollectionPolicy.Derive(fixture.Catalog.Collections["relics"], state);

        var item = Assert.Single(result.Items);
        Assert.Equal(unlocked, item.Owned);
        Assert.Equal("PermanentUnlockOrCurrentItemPresence", item.OwnershipEvidence!.RuleCode);
        Assert.Equal(CollectionKeys.Presence.Length + 1, item.OwnershipEvidence.Facts.Length);
        foreach (var source in CollectionKeys.Presence)
            Assert.Contains(new OwnershipFact(DataType.CurrentItemPresence, source, 1675, "Absent", SourceStatus.Fresh), item.OwnershipEvidence.Facts);
        Assert.Contains(new OwnershipFact(DataType.PermanentUnlock, "achievements", 129, unlocked ? "Present" : "Absent", SourceStatus.Fresh), item.OwnershipEvidence.Facts);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, null)]
    public void OutfitRequiresEveryConstituentAndUnknownIsNotAbsent(bool first, bool second, bool unknown, bool? expected)
    {
        var fixture = SnapshotFixture.Create();
        var presence = fixture.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with
        {
            Items = pair.Key == "inventory" ? [.. new[] { first ? 2642u : 0u, second ? 2965u : 0u }.Where(id => id != 0).Select(id => new PresenceItem(id, 1))] : [],
            Health = unknown && pair.Key == "retainers" ? pair.Value.Health with { SourceStatus = SourceStatus.Unavailable } : pair.Value.Health
        });
        var definition = new CollectionDefinition("outfits", "Outfits", DataType.Derived, true,
            [new(45094, 45094, "Two-item outfit", false, RequiredItemIds: [2642, 2965])]);

        var result = DerivedCollectionPolicy.Derive(definition, fixture.State with { CurrentItemPresence = presence });

        Assert.Equal(expected.HasValue, result.Health.IsUsable);
        if (expected is { } owned)
        {
            var item = Assert.Single(result.Items);
            Assert.Equal(owned, item.Owned);
            Assert.Equal("AllRequiredConstituents", item.OwnershipEvidence!.RuleCode);
            foreach (var (id, present) in new[] { (2642u, first), (2965u, second) })
            {
                var facts = item.OwnershipEvidence.Facts.Where(fact => fact.SubjectId == id).ToArray();
                Assert.Equal(present ? 1 : CollectionKeys.Presence.Length, facts.Length);
                Assert.All(facts, fact => Assert.Equal(present ? "Present" : "Absent", fact.State));
                Assert.All(facts, fact => Assert.Equal(DataType.CurrentItemPresence, fact.DataType));
                Assert.All(facts, fact => Assert.Equal(SourceStatus.Fresh, fact.SourceStatus));
            }
        }
        else
        {
            Assert.Empty(result.Items);
            Assert.True(result.TotalKnown);
            Assert.Equal(DataType.Derived, result.OwnershipKind);
            Assert.Equal(Completeness.Unknown, result.Health.Completeness);
            Assert.Equal(CapabilityState.UnavailablePendingValidation, result.Health.CapabilityState);
            Assert.Equal(ReasonCode.IncompleteContainerSet, result.Health.ReasonCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Health.Reason));
        }
    }

    [Fact]
    public void DerivedHealthPreservesOldestObservationAndAnyStaleDegradedWitness()
    {
        var fixture = SnapshotFixture.Create();
        var older = SnapshotFixture.Now.AddMinutes(-20);
        var presence = fixture.State.CurrentItemPresence.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Items = [] });
        presence = presence.SetItem("inventory", presence["inventory"] with { Items = [new(2642, 1)] });
        presence = presence.SetItem("armoryChest", presence["armoryChest"] with
        {
            Items = [new(2965, 1)],
            Health = presence["armoryChest"].Health with { SourceStatus = SourceStatus.Stale, CapabilityState = CapabilityState.Degraded, ObservedAtUtc = older }
        });
        var definition = new CollectionDefinition("outfits", "Outfits", DataType.Derived, true,
            [new(45094, 45094, "Two-item outfit", false, RequiredItemIds: [2642, 2965])]);

        var result = DerivedCollectionPolicy.Derive(definition, fixture.State with { CurrentItemPresence = presence });

        Assert.True(result.TotalKnown);
        Assert.True(Assert.Single(result.Items).Owned);
        Assert.Equal(SourceStatus.Stale, result.Health.SourceStatus);
        Assert.Equal(CapabilityState.Degraded, result.Health.CapabilityState);
        Assert.Equal(older, result.Health.ObservedAtUtc);
        Assert.Equal(string.Empty, result.Health.Reason);
    }

    [Fact]
    public void AssembledEvidenceUsesCollectionSpecificIdentityAndDoesNotLeakQuestProgress()
    {
        var fixture = SnapshotFixture.Create();
        var state = fixture.State with
        {
            Unlocks = fixture.State.Unlocks.SetItem("mounts", fixture.State.Unlocks["mounts"] with { UnlockedIds = [] }),
            CurrentItemPresence = fixture.State.CurrentItemPresence.SetItem("armoire", fixture.State.CurrentItemPresence["armoire"] with { Items = [] }),
            Quests = [new(1, QuestStatus.InProgress, 7)]
        };

        var data = CollectionDataAssembler.Assemble(fixture.Catalog, state);

        var mount = Assert.Single(data.Collections["mounts"].Items);
        Assert.False(mount.Owned);
        Assert.Null(mount.Sequence);
        Assert.Null(mount.Status);
        Assert.Equal("DirectPermanentUnlock", mount.OwnershipEvidence!.RuleCode);
        Assert.Equal(new OwnershipFact(DataType.PermanentUnlock, "mounts", 1, "Absent", SourceStatus.Fresh), Assert.Single(mount.OwnershipEvidence.Facts));
        var armoire = Assert.Single(data.Collections["armoires"].Items);
        Assert.False(armoire.Owned);
        Assert.Equal("DirectCurrentItemPresence", armoire.OwnershipEvidence!.RuleCode);
        Assert.Equal(new OwnershipFact(DataType.CurrentItemPresence, "armoire", 100, "Absent", SourceStatus.Fresh), Assert.Single(armoire.OwnershipEvidence.Facts));
    }
}
