using System.Collections.Immutable;
using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper.Tests;

public sealed class ExportaramusOwnershipTests
{
    [Fact]
    public void RestoredDresserAndArmoireSuppressOwnedItemsDespiteAnIncompleteRetainerSet()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var health = snapshot.SourceHealth;
        foreach (var source in new[] { "glamourDresser", "armoire" })
            health = health.SetItem(source, health[source] with { SourceStatus = "Stale" });
        health = health.SetItem("retainers", health["retainers"] with
        {
            SourceStatus = "Unavailable",
            Completeness = "Unknown",
            ObservedAtUtc = null,
            ReasonCode = "IncompleteRetainerSet",
        });
        var state = ExportaramusFixture.State(snapshot with
        {
            SourceHealth = health,
            CurrentItemPresence = snapshot.CurrentItemPresence.Remove("retainers")
                .SetItem("glamourDresser", new([new(90001, 1)]))
                .SetItem("armoire", new([new(90002, 1)])),
        });

        Assert.True(state.Classify(ShoppingCategory.Outfit, 90001, 0).SuppressPurchase);
        Assert.True(state.Classify(ShoppingCategory.Outfit, 90002, 0).SuppressPurchase);
        Assert.DoesNotContain("glamourDresser", state.StatusDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("armoire", state.StatusDetail, StringComparison.Ordinal);
        Assert.Contains("IncompleteRetainerSet", state.StatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparedKnowledgeStatesKeepEveryPermissionAndReasonDistinct()
    {
        var pending = CollectionDataOwnershipPolicy.NotChecked;
        Assert.False(pending.WasChecked);
        Assert.Equal(new ItemOwnership(false, false, false, pending.StatusDetail), pending.Classify(ShoppingCategory.Minion, 99999, 99));
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot);
        Assert.True(state.WasChecked);
        Assert.Equal("Exportaramus supplied complete current or last-known physical observations. Unlock readiness is checked separately for each category.", state.StatusDetail);
        Assert.Equal(new ItemOwnership(true, true, false, "The item was observed in Exportaramus storage; this does not prove a permanent unlock."),
            state.Classify(ShoppingCategory.Minion, state.TrustedOwnedItemIds[0], 99));
        Assert.Equal(new ItemOwnership(false, false, false, "The public catalog entry has no verified native collection mapping."),
            state.Classify(ShoppingCategory.Minion, 99999, 99, false));
        var missing = snapshot with { Unlocks = snapshot.Unlocks.Remove("minions") };
        Assert.Equal(new ItemOwnership(false, false, false, "minions: Fresh, Complete (None)"),
            ExportaramusFixture.State(missing).Classify(ShoppingCategory.Minion, 99999, 99));
        var stale = snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("minions", snapshot.SourceHealth["minions"] with { SourceStatus = "Stale" }) };
        Assert.Equal(new ItemOwnership(false, false, false, "Exportaramus minions is last known; refresh this collection to verify unlock absence."),
            ExportaramusFixture.State(stale).Classify(ShoppingCategory.Minion, 99999, 99));
        var missingHealth = ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.Remove("inventory") });
        Assert.Equal("Exportaramus physical observations are incomplete: inventory: omitted, unknown (NoSourceHealth).", missingHealth.StatusDetail);
        Assert.Equal(new ItemOwnership(false, false, false, "inventory: omitted, unknown (NoSourceHealth)"), missingHealth.Classify(ShoppingCategory.Minion, 99999, 99));
        var twoMissing = ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.Remove("inventory").Remove("armoryChest") });
        Assert.Equal("Exportaramus physical observations are incomplete: inventory: omitted, unknown (NoSourceHealth); armoryChest: omitted, unknown (NoSourceHealth).", twoMissing.StatusDetail);
        var reports = snapshot with { ReportedOwnership = new([new(99999, "2026-09-13T00:00:00Z", "Other"), new(99998, "2026-09-13T00:00:00Z", "Other")]) };
        Assert.Equal(new ItemOwnership(true, true, false, "A reported acquisition suppresses duplicate purchases; it does not prove an unlock or current possession."),
            ExportaramusFixture.State(reports).Classify(ShoppingCategory.Minion, 99999, 99));
    }

    [Fact]
    public void GeneratedPhysicalObservationsPreserveOrderedDistinctCoverageAndSourceDetails()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var random = new Random(93217);
        for (var iteration = 0; iteration < 256; iteration++)
        {
            var presence = snapshot.CurrentItemPresence;
            var health = snapshot.SourceHealth;
            var expected = new SortedSet<uint>();
            foreach (var source in CollectionDataOwnershipPolicy.PhysicalSources)
            {
                var id = (uint)random.Next(1, 30);
                presence = presence.SetItem(source, new([new(id, 1)]));
                health = health.SetItem(source, health[source] with { SourceStatus = iteration % 2 == 0 ? "Fresh" : "Stale" });
                expected.Add(id);
            }
            var state = ExportaramusFixture.State(snapshot with { CurrentItemPresence = presence, SourceHealth = health });
            Assert.Equal(expected, state.TrustedOwnedItemIds);
            Assert.All(state.Sources, source =>
            {
                Assert.Equal(1, source.ItemCount);
                Assert.True(source.AssertsOwnership);
                Assert.Equal($"{source.Source}: {source.Status} complete observation", source.Reason);
            });
            Assert.True(state.CompleteFor(ShoppingCategory.Weapon));
        }
    }

    [Theory]
    [InlineData((int)ShoppingCategory.Minion, "minions")]
    [InlineData((int)ShoppingCategory.Mount, "mounts")]
    [InlineData((int)ShoppingCategory.Orchestrion, "orchestrions")]
    [InlineData((int)ShoppingCategory.Hairstyle, "hairstyles")]
    [InlineData((int)ShoppingCategory.Facewear, "facewear")]
    [InlineData((int)ShoppingCategory.Fashion, "fashions")]
    public void PermanentUnlockAndUnconsumedPresenceAreDifferentFacts(int categoryValue, string key)
    {
        var category = (ShoppingCategory)categoryValue;
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { Unlocks = snapshot.Unlocks.SetItem(key, new([9])) });
        Assert.Equal(new ItemOwnership(true, true, true, $"Exportaramus observed the {key} unlock."), state.Classify(category, 99999, 9));
        var present = state.Classify(category, state.TrustedOwnedItemIds[0], 9);
        Assert.True(present.SuppressPurchase);
        Assert.False(present.PermanentlyUnlocked);
    }

    [Theory]
    [InlineData((int)ShoppingCategory.Minion, "minions")]
    [InlineData((int)ShoppingCategory.Mount, "mounts")]
    [InlineData((int)ShoppingCategory.Orchestrion, "orchestrions")]
    [InlineData((int)ShoppingCategory.Hairstyle, "hairstyles")]
    [InlineData((int)ShoppingCategory.Facewear, "facewear")]
    [InlineData((int)ShoppingCategory.Fashion, "fashions")]
    public void FreshEmptyIsMissingButOmittedAndStaleAreUnknown(int categoryValue, string key)
    {
        var category = (ShoppingCategory)categoryValue;
        var snapshot = ExportaramusFixture.Snapshot();
        snapshot = snapshot with { Unlocks = snapshot.Unlocks.SetItem(key, new([])) };
        Assert.Equal(new ItemOwnership(true, false, false, "Complete observations contain neither this item nor its unlock."), ExportaramusFixture.State(snapshot).Classify(category, 99999, 9));
        Assert.True(ExportaramusFixture.State(snapshot).CompleteFor(category));
        var omitted = ExportaramusFixture.State(snapshot with { Unlocks = snapshot.Unlocks.Remove(key) });
        Assert.False(omitted.Classify(category, 99999, 9).Known);
        Assert.False(omitted.CompleteFor(category));
        var stale = ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.SetItem(key, snapshot.SourceHealth[key] with { SourceStatus = "Stale" }) });
        Assert.False(stale.Classify(category, 99999, 9).Known);
        Assert.Contains("last known", stale.Classify(category, 99999, 9).Reason, StringComparison.Ordinal);
        Assert.False(stale.CompleteFor(category));
    }

    [Theory]
    [InlineData("armoire")]
    [InlineData("glamourDresser")]
    [InlineData("armoryChest")]
    public void EquipmentStorageGapsDoNotBlockConsumables(string source)
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { CurrentItemPresence = snapshot.CurrentItemPresence.Remove(source) });
        Assert.True(state.Classify(ShoppingCategory.Minion, 99999, 99).Known);
        Assert.True(state.CompleteFor(ShoppingCategory.Minion));
        Assert.False(state.Classify(ShoppingCategory.Outfit, 99999, 99).Known);
        Assert.False(state.CompleteFor(ShoppingCategory.Weapon));
        Assert.Contains(source, state.StatusDetail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("chocoboSaddlebag")]
    [InlineData("premiumSaddlebag")]
    [InlineData("retainers")]
    public void UnobservedConsumableStorageBlocksMissingButNotConfirmedUnlocks(string source)
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { CurrentItemPresence = snapshot.CurrentItemPresence.Remove(source) });
        Assert.False(state.Classify(ShoppingCategory.Minion, 99999, 99).Known);
        Assert.Contains(source, state.Classify(ShoppingCategory.Minion, 99999, 99).Reason, StringComparison.Ordinal);
        Assert.True(state.Classify(ShoppingCategory.Minion, 99999, 1).PermanentlyUnlocked);
        Assert.False(state.CanInferMissingItemIds);
    }

    [Fact]
    public void UnrelatedUnknownUnlockDoesNotBlockReadyCategory()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { Unlocks = snapshot.Unlocks.Remove("facewear") });
        Assert.True(state.CompleteFor(ShoppingCategory.Minion));
        Assert.False(state.CompleteFor(ShoppingCategory.Facewear));
        Assert.True(state.Classify(ShoppingCategory.Minion, 99999, 99).Known);
    }

    [Fact]
    public void AdvisoryEvidenceSuppressesWithoutContributingToPhysicalProgressOrUnlocks()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { ReportedOwnership = new([new(99999, "2026-09-13T00:00:00Z", "Other")]) });
        var knowledge = state.Classify(ShoppingCategory.Facewear, 99999, 99);
        Assert.True(knowledge.SuppressPurchase);
        Assert.False(knowledge.PermanentlyUnlocked);
        Assert.DoesNotContain(99999U, state.TrustedOwnedItemIds);
    }

    [Theory]
    [InlineData("Unknown", "Complete", "Supported", "2026-09-13T00:00:00Z")]
    [InlineData("Fresh", "Partial", "Supported", "2026-09-13T00:00:00Z")]
    [InlineData("Fresh", "Complete", "Unsupported", "2026-09-13T00:00:00Z")]
    [InlineData("Fresh", "Complete", "Supported", null)]
    public void UnusableSourceHealthNeverProvesAbsence(string status, string completeness, string capability, string? observed)
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var health = snapshot.SourceHealth["inventory"] with { SourceStatus = status, Completeness = completeness, CapabilityState = capability, ObservedAtUtc = observed };
        var state = ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("inventory", health) });
        Assert.False(state.Classify(ShoppingCategory.Weapon, 99999, 99).Known);
        Assert.False(state.CompleteFor(ShoppingCategory.Weapon));
    }

    [Fact]
    public void MissingHealthAndInvalidMappingHaveSpecificReasons()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.Remove("inventory") });
        Assert.Contains("NoSourceHealth", state.Classify(ShoppingCategory.Minion, 99999, 99).Reason, StringComparison.Ordinal);
        Assert.False(ExportaramusFixture.State().Classify(ShoppingCategory.Facewear, 99999, 99, false).Known);
        Assert.Contains("mapping", ExportaramusFixture.State().Classify(ShoppingCategory.Facewear, 99999, 99, false).Reason, StringComparison.Ordinal);
        Assert.False(ExportaramusFixture.State(snapshot with { SourceHealth = snapshot.SourceHealth.Remove("minions") }).CompleteFor(ShoppingCategory.Minion));
    }

    [Theory]
    [InlineData(CollectionDataAvailability.Unavailable, CollectionDataValidationCode.ProviderUnavailable)]
    [InlineData(CollectionDataAvailability.Rejected, CollectionDataValidationCode.SchemaHashMismatch)]
    [InlineData(CollectionDataAvailability.Rejected, CollectionDataValidationCode.WrongProviderSession)]
    [InlineData(CollectionDataAvailability.Refreshing, CollectionDataValidationCode.None)]
    [InlineData(CollectionDataAvailability.Disposed, CollectionDataValidationCode.Disposed)]
    public void RejectionDoesNotRetainMissingPermission(CollectionDataAvailability availability, CollectionDataValidationCode failure)
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var state = CollectionDataOwnershipPolicy.Build(new(availability, snapshot, snapshot.ProviderSessionId, snapshot.ContentId, 1, failure), ExportaramusFixture.ContentId);
        Assert.True(state.WasChecked);
        Assert.Null(state.Snapshot);
        Assert.Empty(state.TrustedOwnedItemIds);
        Assert.False(state.CanInferMissingItemIds);
        Assert.False(state.Classify(ShoppingCategory.Outfit, 99999, 99).Known);
        Assert.Contains(failure.ToString(), state.StatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityAndDiagnosticsStayExplicit()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var view = new CollectionDataClientView(CollectionDataAvailability.Ready, snapshot, snapshot.ProviderSessionId, snapshot.ContentId, 1, CollectionDataValidationCode.None);
        Assert.Equal(CollectionDataValidationCode.WrongCharacter, CollectionDataOwnershipPolicy.Build(view, 42).Failure);
        Assert.Contains("character is loaded", CollectionDataOwnershipPolicy.Build(view, 0).StatusDetail, StringComparison.Ordinal);
        Assert.Equal(DiagnosticResultKind.NotAvailable, CollectionDataOwnershipPolicy.BuildDiagnosticCheck(CollectionDataOwnershipPolicy.NotChecked).Outcome);
        Assert.Equal(DiagnosticResultKind.Pass, CollectionDataOwnershipPolicy.BuildDiagnosticCheck(ExportaramusFixture.State()).Outcome);
        Assert.Contains("owned item types", CollectionDataOwnershipPolicy.BuildPlayerSummary(ExportaramusFixture.State()), StringComparison.Ordinal);
        Assert.Equal(CollectionDataOwnershipPolicy.NotChecked.StatusDetail, CollectionDataOwnershipPolicy.BuildPlayerSummary(CollectionDataOwnershipPolicy.NotChecked));
        Assert.Throws<ArgumentNullException>(() => CollectionDataOwnershipPolicy.Build(null!, 42));
        Assert.Throws<ArgumentNullException>(() => CollectionDataOwnershipPolicy.BuildDiagnosticCheck(null!));
        Assert.Throws<ArgumentNullException>(() => CollectionDataOwnershipPolicy.BuildPlayerSummary(null!));
    }
}
