using Exportaramus.ConsumerKit.V1_2;

namespace CollectionShopper.Tests;

public sealed class PendingPurchaseGuardTests
{
    private static readonly PendingPurchase Guard = new(ExportaramusFixture.ContentId, 99999, ExportaramusFixture.Now);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidGuardIdentifiersCannotBeAcknowledgedByMatchingMalformedPresence(bool invalidCharacter)
    {
        var guard = invalidCharacter ? Guard with { ContentId = 0 } : Guard with { ItemId = 0 };
        var snapshot = Present() with
        {
            ContentId = guard.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CurrentItemPresence = Present().CurrentItemPresence.SetItem("inventory", new([new(guard.ItemId, 1)])),
        };
        var malformedState = ExportaramusFixture.State() with { Snapshot = snapshot };
        Assert.Null(PendingPurchaseGuard.ObservedAfter(guard, malformedState));
    }

    [Fact]
    public void ConfirmationAndAcknowledgementMayEqualTheCurrentInstant()
    {
        Assert.True(PendingPurchaseGuard.IsValid([Guard], Guard.ConfirmedAtUtc));
        Assert.True(PendingPurchaseGuard.IsValid([Guard with { AcknowledgedAtUtc = Guard.ConfirmedAtUtc }], Guard.ConfirmedAtUtc));
    }

    [Fact]
    public void FreshEmptyPresenceDoesNotAcknowledgeAnUnobservedPurchase()
    {
        var snapshot = Present();
        snapshot = snapshot with { CurrentItemPresence = snapshot.CurrentItemPresence.SetItem("inventory", new([])) };
        Assert.Null(PendingPurchaseGuard.ObservedAfter(Guard, ExportaramusFixture.State(snapshot)));
    }

    [Fact]
    public void LaterPhysicalSourceCanAcknowledgeAfterEarlierEmptyOrDifferentPresence()
    {
        var snapshot = Present("glamourDresser");
        snapshot = snapshot with {
            SourceHealth = snapshot.SourceHealth.SetItem("inventory", snapshot.SourceHealth["glamourDresser"]),
            CurrentItemPresence = snapshot.CurrentItemPresence.SetItem("inventory", new([new(88888, 1)])),
        };
        Assert.Equal(Guard.ConfirmedAtUtc.AddSeconds(1), PendingPurchaseGuard.ObservedAfter(Guard, ExportaramusFixture.State(snapshot)));
    }

    [Fact]
    public void PersistedTrackingRejectsMalformedDuplicateFutureAndOversizedRecords()
    {
        var now = ExportaramusFixture.Now.AddSeconds(10);
        Assert.True(PendingPurchaseGuard.IsValid([], now));
        Assert.True(PendingPurchaseGuard.IsValid([Guard, Guard with { ContentId = 42 }], now));
        Assert.True(PendingPurchaseGuard.IsValid([Guard with { AcknowledgedAtUtc = now }], now));
        Assert.False(PendingPurchaseGuard.IsValid(null, now));
        Assert.False(PendingPurchaseGuard.IsValid([null!], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard, Guard], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard with { ContentId = 0 }], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard with { ItemId = 0 }], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard with { ConfirmedAtUtc = now.AddSeconds(1) }], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard with { AcknowledgedAtUtc = now.AddSeconds(1) }], now));
        Assert.False(PendingPurchaseGuard.IsValid([Guard with { AcknowledgedAtUtc = Guard.ConfirmedAtUtc.AddSeconds(-1) }], now));
        var maximum = Enumerable.Range(1, PendingPurchaseGuard.MaximumRecords).Select(i => Guard with { ItemId = (uint)i }).ToArray();
        Assert.True(PendingPurchaseGuard.IsValid(maximum, now));
        Assert.False(PendingPurchaseGuard.IsValid(maximum.Append(Guard with { ItemId = uint.MaxValue }).ToArray(), now));
    }

    [Fact]
    public void FreshObservationOfAnotherItemDoesNotRetireGuard()
    {
        var snapshot = Present() with { CurrentItemPresence = Present().CurrentItemPresence.SetItem("inventory", new([new(99998, 1)])) };
        Assert.Null(PendingPurchaseGuard.ObservedAfter(Guard, ExportaramusFixture.State(snapshot)));
    }

    [Fact]
    public void LegacySelfReportsNoLongerOverrideCanonicalMissingOwnership()
    {
        var snapshot = ExportaramusFixture.Snapshot() with
        {
            ReportedOwnership = new([new(99999, ExportaramusFixture.Now.ToString("O"), PluginIdentity.InternalName)])
        };
        var state = ExportaramusFixture.State(snapshot);
        Assert.True(state.Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        state = state with { LegacyPurchaseArchive = state.LegacyPurchaseArchive.Add(99999) };
        Assert.False(state.Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        Assert.True(state.Classify(ShoppingCategory.Outfit, 99999, 0).Known);
        Assert.False((state with { Snapshot = null }).Classify(ShoppingCategory.Outfit, 99999, 0).Known);
        var other = snapshot with { ReportedOwnership = new([new(99999, ExportaramusFixture.Now.ToString("O"), "OtherPlugin")]) };
        Assert.True((state with { Snapshot = other }).Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        var present = Present() with { ReportedOwnership = snapshot.ReportedOwnership };
        Assert.True((ExportaramusFixture.State(present) with { LegacyPurchaseArchive = state.LegacyPurchaseArchive })
            .Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
    }

    [Fact]
    public void AcknowledgedReportDoesNotPermanentlyHideLaterCanonicalAbsence()
    {
        var snapshot = ExportaramusFixture.Snapshot();
        var report = new ReportedEvidence(99999, ExportaramusFixture.Now.ToString("O"), "CollectionShopper");
        snapshot = snapshot with { ReportedOwnership = new([report]) };
        var state = ExportaramusFixture.State(snapshot);
        Assert.True(state.Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        state = state with { AcknowledgedAcquisitions = state.AcknowledgedAcquisitions.Add(99999, ExportaramusFixture.Now.AddSeconds(1)) };
        Assert.Equal(new ItemOwnership(true, false, false, "Complete observations contain neither this item nor its unlock."),
            state.Classify(ShoppingCategory.Outfit, 99999, 0));
        var newer = snapshot with { ReportedOwnership = new([report with { ReportedAtUtc = ExportaramusFixture.Now.AddSeconds(2).ToString("O") }]) };
        Assert.True((state with { Snapshot = newer }).Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        var malformed = snapshot with { ReportedOwnership = new([report with { ReportedAtUtc = "invalid" }]) };
        Assert.True((state with { Snapshot = malformed }).Classify(ShoppingCategory.Outfit, 99999, 0).SuppressPurchase);
        Assert.False((state with { Snapshot = null }).Classify(ShoppingCategory.Outfit, 99999, 0).Known);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("armoryChest")]
    [InlineData("chocoboSaddlebag")]
    [InlineData("premiumSaddlebag")]
    [InlineData("retainers")]
    [InlineData("armoire")]
    [InlineData("glamourDresser")]
    public void FreshCanonicalPresenceAfterConfirmationRetiresGuard(string source)
    {
        var state = ExportaramusFixture.State(Present(source));
        Assert.True(PendingPurchaseGuard.CanRetire(Guard, state));
        Assert.Equal(ExportaramusFixture.Now.AddSeconds(1), PendingPurchaseGuard.ObservedAfter(Guard, state));
    }

    [Theory]
    [InlineData("Stale", "Complete", "Supported", "2026-09-13T12:00:01Z")]
    [InlineData("Unavailable", "Complete", "Supported", "2026-09-13T12:00:01Z")]
    [InlineData("Fresh", "Partial", "Supported", "2026-09-13T12:00:01Z")]
    [InlineData("Fresh", "Unknown", "Supported", "2026-09-13T12:00:01Z")]
    [InlineData("Fresh", "Complete", "Unsupported", "2026-09-13T12:00:01Z")]
    [InlineData("Fresh", "Complete", "Supported", null)]
    [InlineData("Fresh", "Complete", "Supported", "invalid")]
    [InlineData("Fresh", "Complete", "Supported", "2026-09-13T12:00:00Z")]
    [InlineData("Fresh", "Complete", "Supported", "2026-09-13T11:59:59Z")]
    public void UnqualifiedOrOldPresenceCannotRetire(string status, string completeness, string capability, string? time)
    {
        var snapshot = Present();
        snapshot = snapshot with { SourceHealth = snapshot.SourceHealth.SetItem("inventory",
            snapshot.SourceHealth["inventory"] with { SourceStatus = status, Completeness = completeness,
                CapabilityState = capability, ObservedAtUtc = time }) };
        Assert.False(PendingPurchaseGuard.CanRetire(Guard, ExportaramusFixture.State(snapshot)));
    }

    [Fact]
    public void UnknownWrongIdentityAndInvalidGuardCannotRetire()
    {
        var state = ExportaramusFixture.State(Present());
        Assert.False(PendingPurchaseGuard.CanRetire(Guard, CollectionDataOwnershipPolicy.NotChecked));
        Assert.False(PendingPurchaseGuard.CanRetire(Guard with { ContentId = 0 }, state));
        Assert.False(PendingPurchaseGuard.CanRetire(Guard with { ContentId = 42 }, state));
        Assert.False(PendingPurchaseGuard.CanRetire(Guard with { ItemId = 0 }, state));
        Assert.False(PendingPurchaseGuard.CanRetire(Guard with { ItemId = 88888 }, state));
    }

    [Fact]
    public void ReportsUnlocksAndMissingSourceNeverSubstituteForPhysicalPresence()
    {
        var snapshot = Present();
        var reportOnly = snapshot with {
            CurrentItemPresence = snapshot.CurrentItemPresence.Remove("inventory"),
            ReportedOwnership = new([new(Guard.ItemId, "2026-09-13T12:00:01Z", "CollectionShopper")]),
            Unlocks = snapshot.Unlocks.SetItem("minions", new([Guard.ItemId])),
            Revision = snapshot.Revision + 100,
        };
        Assert.False(PendingPurchaseGuard.CanRetire(Guard, ExportaramusFixture.State(reportOnly)));
        Assert.False(PendingPurchaseGuard.CanRetire(Guard, ExportaramusFixture.State(snapshot with {
            SourceHealth = snapshot.SourceHealth.Remove("inventory") })));
    }

    private static CollectionDataSnapshot Present(string source = "inventory")
    {
        var snapshot = ExportaramusFixture.Snapshot();
        return snapshot with {
            CurrentItemPresence = snapshot.CurrentItemPresence.SetItem(source, new([new(Guard.ItemId, 1)])),
            SourceHealth = snapshot.SourceHealth.SetItem(source, snapshot.SourceHealth[source] with {
                SourceStatus = "Fresh", Completeness = "Complete", CapabilityState = "Supported",
                ObservedAtUtc = "2026-09-13T12:00:01Z" }),
        };
    }
}
