namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Shared.CollectionData;

/// <summary>
/// Verifies Exportaramus ownership policy and the separate
/// action-time native validation boundary.
/// </summary>
public sealed class CollectionProviderPolicyTests
{
    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void OwnershipClassificationDistinguishesEveryStateAndSource()
    {
        var snapshot = TestDomainValues.SharedSnapshot();
        Assert.Equal(SharedOwnershipState.Empty, CollectionProviderPolicy.ClassifyOwnership(null, snapshot));
        Assert.Equal(SharedOwnershipState.Unknown, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 61 }, null));
        Assert.Equal(SharedOwnershipState.FreshMatch, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 61 }, snapshot));
        Assert.Equal(SharedOwnershipState.FreshMatch, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 51 }, snapshot));

        var staleDresser = TestDomainValues.SharedSnapshot(
            armoire: CollectionSourceStatus.Unavailable,
            glamourDresser: CollectionSourceStatus.Stale);
        Assert.Equal(SharedOwnershipState.LastKnownMatch, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 61 }, staleDresser));
        var staleArmoire = TestDomainValues.SharedSnapshot(
            armoire: CollectionSourceStatus.Stale,
            glamourDresser: CollectionSourceStatus.Unavailable);
        Assert.Equal(SharedOwnershipState.LastKnownMatch, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 51 }, staleArmoire));
        Assert.Equal(SharedOwnershipState.NotObserved, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 999 }, snapshot));
        Assert.Equal(SharedOwnershipState.Unknown, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 999 }, staleDresser));
        var lastKnownBoth = TestDomainValues.SharedSnapshot(
            armoire: CollectionSourceStatus.Stale,
            glamourDresser: CollectionSourceStatus.Stale);
        Assert.Equal(SharedOwnershipState.Unknown, CollectionProviderPolicy.ClassifyOwnership(new SavedPlateItem { ItemId = 999 }, lastKnownBoth));

        Assert.Null(CollectionProviderPolicy.DescribeOwnership(SharedOwnershipState.Empty));
        Assert.Equal("Shared: Current match", CollectionProviderPolicy.DescribeOwnership(SharedOwnershipState.FreshMatch));
        Assert.Equal("Shared: Last-known match", CollectionProviderPolicy.DescribeOwnership(SharedOwnershipState.LastKnownMatch));
        Assert.Equal("Shared: Not collected", CollectionProviderPolicy.DescribeOwnership(SharedOwnershipState.NotObserved));
        Assert.Equal("Shared: Unknown", CollectionProviderPolicy.DescribeOwnership(SharedOwnershipState.Unknown));
        Assert.Equal("Shared: Unknown", CollectionProviderPolicy.DescribeOwnership((SharedOwnershipState)999));
        Assert.Equal("Empty", CollectionProviderPolicy.DescribeSlotOwnership(SharedOwnershipState.Empty));
        Assert.Equal("Collected", CollectionProviderPolicy.DescribeSlotOwnership(SharedOwnershipState.FreshMatch));
        Assert.Equal("Stale", CollectionProviderPolicy.DescribeSlotOwnership(SharedOwnershipState.LastKnownMatch));
        Assert.Equal("Not collected", CollectionProviderPolicy.DescribeSlotOwnership(SharedOwnershipState.NotObserved));
        Assert.Equal("Unknown", CollectionProviderPolicy.DescribeSlotOwnership(SharedOwnershipState.Unknown));
        Assert.Equal("Unknown", CollectionProviderPolicy.DescribeSlotOwnership((SharedOwnershipState)999));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void PlateCompletenessCountsAndSummariesAreExact()
    {
        Assert.Same(SharedPlateCompleteness.Empty, CollectionProviderPolicy.CreatePlateCompleteness(null, null));
        var plate = PlateLibrary.Create("Empty", TestDomainValues.Timestamp, Guid.NewGuid());
        var empty = CollectionProviderPolicy.CreatePlateCompleteness(plate, TestDomainValues.SharedSnapshot());
        Assert.Equal(0, empty.FilledSlots);
        Assert.Equal("This saved plate is empty.", empty.Summary);

        plate.Items[(int)PlateSlot.Head] = new SavedPlateItem { ItemId = 61 };
        plate.Items[(int)PlateSlot.Body] = new SavedPlateItem { ItemId = 999 };
        var unavailable = CollectionProviderPolicy.CreatePlateCompleteness(plate, null);
        Assert.Equal(2, unavailable.FilledSlots);
        Assert.Equal(2, unavailable.Unknown);
        Assert.Equal("2 item(s); ownership cannot be checked right now.", unavailable.Summary);

        plate.Items[(int)PlateSlot.Hands] = new SavedPlateItem { ItemId = 51 };
        var mixed = CollectionProviderPolicy.CreatePlateCompleteness(
            plate,
            TestDomainValues.SharedSnapshot(
                armoire: CollectionSourceStatus.Stale,
                glamourDresser: CollectionSourceStatus.Fresh));
        Assert.Equal(3, mixed.FilledSlots);
        Assert.Equal(1, mixed.FreshMatches);
        Assert.Equal(1, mixed.LastKnownMatches);
        Assert.Equal(0, mixed.NotObserved);
        Assert.Equal(1, mixed.Unknown);
        Assert.Equal("2 of 3 collected (1 last known); 0 not collected; 1 unknown.", mixed.Summary);
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void FreshSharedSnapshotIsCollectionAuthority()
    {
        var snapshot = TestDomainValues.SharedSnapshot();
        var summary = CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.None, snapshot);
        var diagnostics = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, snapshot);

        Assert.Equal("Collection ready.", summary);
        Assert.Equal(
            "Collection ready · 1,438 owned",
            CollectionProviderPolicy.DescribePrimaryStatus(summary, 1438));
        Assert.Contains("ownership authority", diagnostics.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Current", diagnostics.GlamourDresser.ProviderStatus);
        Assert.Equal("Glamour Dresser", diagnostics.GlamourDresser.Name);
        Assert.Equal("Current", diagnostics.Armoire.ProviderStatus);
        Assert.Equal("Armoire", diagnostics.Armoire.Name);
        Assert.Equal(
            "Used for collection views and plate planning. Loading revalidates exact native source IDs and dyes. Last observed 2026-09-01 12:00:00 UTC.",
            diagnostics.GlamourDresser.Detail);
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void LastKnownSharedSnapshotRemainsOperationalAndClearlyLabelled()
    {
        var snapshot = TestDomainValues.SharedSnapshot(
            armoire: CollectionSourceStatus.Stale,
            glamourDresser: CollectionSourceStatus.Stale);
        var diagnostics = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, snapshot);

        Assert.Equal(
            "Collection ready from last-known data.",
            CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.None, snapshot));
        Assert.Contains("Last-known ownership remains available", diagnostics.Guidance, StringComparison.Ordinal);
        Assert.Equal("Last known", diagnostics.GlamourDresser.ProviderStatus);
        Assert.Equal("Last known", diagnostics.Armoire.ProviderStatus);
        Assert.Equal(
            "Saved ownership remains usable for collection views and plate planning. It updates automatically when the game exposes this source again. Last observed 2026-09-01 12:00:00 UTC.",
            diagnostics.GlamourDresser.Detail);
        Assert.Equal("Current", CollectionProviderPolicy.DescribeFreshness(CollectionFreshness.Fresh));
        Assert.Equal("Last known", CollectionProviderPolicy.DescribeFreshness(CollectionFreshness.LastKnown));
        Assert.Equal("Unknown", CollectionProviderPolicy.DescribeFreshness((CollectionFreshness)999));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void ActionValidationHintRequiresFreshMatchingOwnership()
    {
        var plate = PlateLibrary.Create("Distinct", TestDomainValues.Timestamp, Guid.NewGuid());
        plate.Items[(int)PlateSlot.Head] = new SavedPlateItem { ItemId = 61 };
        plate.Items[(int)PlateSlot.Body] = new SavedPlateItem { ItemId = 61 };
        plate.Items[(int)PlateSlot.Hands] = new SavedPlateItem { ItemId = 51 };
        var snapshot = TestDomainValues.SharedSnapshot();

        Assert.Equal(
            "Exportaramus still sees 2 matching item ID(s) in Fresh storage. The load was stopped because action-time native validation could not confirm the exact source and dyes.",
            CollectionProviderPolicy.DescribeActionValidationHint(snapshot, plate));
        Assert.Throws<ArgumentNullException>(() => CollectionProviderPolicy.DescribeActionValidationHint(null, null!));
        Assert.Null(CollectionProviderPolicy.DescribeActionValidationHint(null, plate));
        Assert.Null(CollectionProviderPolicy.DescribeActionValidationHint(
            snapshot,
            PlateLibrary.Create("No match", TestDomainValues.Timestamp, Guid.NewGuid())));

        foreach (var armoire in Enum.GetValues<CollectionSourceStatus>())
        {
            foreach (var dresser in Enum.GetValues<CollectionSourceStatus>())
            {
                var candidate = TestDomainValues.SharedSnapshot(
                    armoire: armoire,
                    glamourDresser: dresser);
                var hasFreshMatch = armoire == CollectionSourceStatus.Fresh ||
                    dresser == CollectionSourceStatus.Fresh;
                Assert.Equal(
                    hasFreshMatch,
                    CollectionProviderPolicy.DescribeActionValidationHint(candidate, plate) is not null);
            }
        }
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void MissingFailedAndRetainedProviderDiagnosticsAreExact()
    {
        Assert.Equal("Collection not available yet.", CollectionProviderPolicy.DescribeSummary(false, CollectionDataFailure.None, null));
        Assert.Equal("Exportaramus is unavailable; ownership is unknown.", CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.ProviderUnavailable, null));

        var uncheckedState = CollectionProviderPolicy.CreateDiagnostics(false, CollectionDataFailure.None, null);
        Assert.Equal("Not checked yet.", uncheckedState.GlamourDresser.Detail);
        Assert.Equal("Not Available", uncheckedState.GlamourDresser.ProviderStatus);
        Assert.Contains("Ownership remains unknown", uncheckedState.Guidance, StringComparison.Ordinal);

        var failed = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.ProviderUnavailable, null);
        Assert.Equal(
            "Exportaramus ownership is unavailable. The collection is unknown, not empty; local saved plates remain available.",
            failed.Guidance);
        Assert.Equal("Glamour Dresser", failed.GlamourDresser.Name);
        Assert.Equal("Not Available", failed.GlamourDresser.ProviderStatus);
        Assert.Equal("Shared collection data provider unavailable.", failed.GlamourDresser.Detail);
        Assert.Equal("Armoire", failed.Armoire.Name);
        Assert.Equal("Not Available", failed.Armoire.ProviderStatus);
        Assert.Equal("Shared collection data provider unavailable.", failed.Armoire.Detail);

        var snapshot = TestDomainValues.SharedSnapshot();
        Assert.Equal(
            "Collection ready. Refresh failed; keeping saved ownership.",
            CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.ProviderUnavailable, snapshot));
        Assert.Equal(
            "Collection ready.",
            CollectionProviderPolicy.DescribeSummary(false, CollectionDataFailure.ProviderUnavailable, snapshot));

        var partial = TestDomainValues.SharedSnapshot(
            armoire: CollectionSourceStatus.Unavailable,
            glamourDresser: CollectionSourceStatus.Fresh);
        Assert.Equal(
            "Collection partially available; some ownership is unknown.",
            CollectionProviderPolicy.DescribeSummary(true, CollectionDataFailure.None, partial));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void SourceDiagnosticsDescribeEveryFreshnessState()
    {
        var observed = new DateTimeOffset(TestDomainValues.Timestamp, TimeSpan.Zero);
        var baseline = TestDomainValues.SharedSnapshot();
        var unavailableSnapshot = baseline with
        {
            GlamourDresser = new CollectionSourceSnapshot(CollectionSourceStatus.Unavailable, null, [], "custom reason"),
            Armoire = new CollectionSourceSnapshot(CollectionSourceStatus.Unavailable, null, [], null),
        };
        var unavailable = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, unavailableSnapshot);
        Assert.Equal("custom reason", unavailable.GlamourDresser.Detail);
        Assert.Equal("The provider marked this source unavailable.", unavailable.Armoire.Detail);

        var neverSnapshot = baseline with
        {
            GlamourDresser = new CollectionSourceSnapshot(CollectionSourceStatus.NeverObserved, null, [], null),
            Armoire = new CollectionSourceSnapshot((CollectionSourceStatus)999, null, [], null),
        };
        var unusual = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, neverSnapshot);
        Assert.Equal("The provider has not observed this source yet.", unusual.GlamourDresser.Detail);
        Assert.Equal("The provider state is unavailable.", unusual.Armoire.Detail);

        var staleSnapshot = baseline with
        {
            GlamourDresser = new CollectionSourceSnapshot(CollectionSourceStatus.Stale, observed, [61U], null),
        };
        var stale = CollectionProviderPolicy.CreateDiagnostics(true, CollectionDataFailure.None, staleSnapshot);
        Assert.Contains("Saved ownership remains usable", stale.GlamourDresser.Detail, StringComparison.Ordinal);
        Assert.Equal(
            "Last-known ownership remains available. Missing items remain unknown until both sources are current; loading still revalidates native sources and dyes.",
            stale.Guidance);
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void CollectionViewStatusPreservesUnavailableAndKnownStates()
    {
        Assert.Equal(
            "Collection not available yet.",
            CollectionProviderPolicy.DescribeCollectionView(false, CollectionDataFailure.None, null, 0));
        Assert.Equal(
            "Exportaramus is unavailable; ownership is unknown.",
            CollectionProviderPolicy.DescribeCollectionView(true, CollectionDataFailure.ProviderUnavailable, null, 0));
        Assert.Equal(
            "Showing 3 collected item(s).",
            CollectionProviderPolicy.DescribeCollectionView(true, CollectionDataFailure.None, TestDomainValues.SharedSnapshot(), 3));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Fact]
    public void BackgroundRefreshPublicationRequiresCurrentSuccessfulCharacter()
    {
        Assert.True(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.None, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(0, 42, CollectionDataFailure.None, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 0, CollectionDataFailure.None, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 99, CollectionDataFailure.None, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.Busy, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.Disposed, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.StaleCompletion, false));
        Assert.False(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.ProviderUnavailable, true));
        Assert.True(CollectionProviderPolicy.CanPublishBackgroundRefresh(42, 42, CollectionDataFailure.ProviderUnavailable, false));
    }

    /// <summary>Verifies the named policy scenario.</summary>
    [Theory]
    [InlineData((int)CollectionDataFailure.NoCharacter, "Collection will be available after logging in.")]
    [InlineData((int)CollectionDataFailure.Busy, "Collection is refreshing.")]
    [InlineData((int)CollectionDataFailure.ProviderUnavailable, "Exportaramus is unavailable; ownership is unknown.")]
    [InlineData((int)CollectionDataFailure.MalformedJson, "Exportaramus is unavailable; ownership is unknown.")]
    [InlineData(999, "Exportaramus is unavailable; ownership is unknown.")]
    public void PlayerFailureSummariesAreActionable(int failureValue, string expected)
    {
        Assert.Equal(expected, CollectionProviderPolicy.DescribeSummary(true, (CollectionDataFailure)failureValue, null));
    }
}

