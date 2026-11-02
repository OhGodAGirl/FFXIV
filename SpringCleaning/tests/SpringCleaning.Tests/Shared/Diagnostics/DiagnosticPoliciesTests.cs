namespace SpringCleaning.Tests;

public sealed class DiagnosticPoliciesTests
{
    private static readonly string[] ReadyReportLines =
    [
        "Build: test",
        "Mode: InventoryToDresser",
        "Phase: ScanningInventoryForDresser",
        "Status: Scanning",
        "Current item: None",
        "Progress: 0 stored / 2 planned; 2 eligible",
        "Prisms: 10 held / 2 planned",
        "Data: dresser=ready; armoire=ready; candidates=ready",
        "Native UI: none",
        "Queues: 1 outfits; 1 items; source category 1/1",
        "Attempts: interaction=0; dresser-open=0",
        "Requests: navigation=False; interaction=False; menu=False; confirmation=False",
    ];

    private static readonly string[] UnavailableReportLines =
    [
        "Build: build-42",
        "Mode: InventoryToDresser",
        "Phase: StoringDresserItem",
        "Status: Working",
        "Current item: Sample Hat",
        "Progress: 3 stored / 9 planned; 7 eligible",
        "Prisms: unavailable held / 8 planned",
        "Data: dresser=not ready; armoire=not ready; candidates=refreshing",
        "Native UI: dresser,armoire,creation,outfit,outfit-confirm,item-confirm,context,merge-prompt",
        "Queues: 2 outfits; 5 items; source category 3/5",
        "Attempts: interaction=4; dresser-open=6",
        "Requests: navigation=True; interaction=True; menu=True; confirmation=True",
    ];

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    public void VisibilityRequiresAnOpenStorageStationAndDiagnosticSetting(
        bool dresserOpen,
        bool armoireOpen,
        bool diagnosticMode,
        bool expected)
    {
        // Arrange

        // Act
        var visible = DiagnosticVisibilityPolicy.ShouldOpen(
            dresserOpen,
            armoireOpen,
            diagnosticMode);

        // Assert
        Assert.Equal(expected, visible);
    }

    [Fact]
    public void ActivityLogIgnoresRepeatedIdenticalObservations()
    {
        // Arrange
        var log = new DiagnosticActivityLog();
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);

        // Act
        var snapshot = CreateSnapshot();

        // Act
        var first = log.Observe(timestamp, snapshot);
        var second = log.Observe(timestamp.AddSeconds(1), snapshot);

        // Assert
        Assert.Equal(DiagnosticObservationKind.Transition, first.Kind);
        Assert.Equal(DiagnosticObservationKind.None, second.Kind);
        Assert.Single(log.Entries);
    }

    [Fact]
    public void ActivityLogRecordsModePhaseOrStatusChanges()
    {
        // Arrange
        var log = new DiagnosticActivityLog();
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);

        // Act
        log.Observe(timestamp, CreateSnapshot());
        log.Observe(
            timestamp.AddSeconds(1),
            CreateSnapshot(phase: MigrationPhase.StoringDresserItem, status: "Storing"));

        // Assert
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains("InventoryToDresser/StoringDresserItem: Storing", log.Entries[1]);
    }

    [Fact]
    public void ActivityLogDiscardsOldestEntriesAtCapacity()
    {
        // Arrange
        var log = new DiagnosticActivityLog(capacity: 2);
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);

        // Act
        log.Observe(timestamp, CreateSnapshot(status: "First"));
        log.Observe(timestamp.AddSeconds(1), CreateSnapshot(status: "Second"));
        log.Observe(timestamp.AddSeconds(2), CreateSnapshot(status: "Third"));

        // Assert
        Assert.Equal(2, log.Entries.Count);
        Assert.DoesNotContain(log.Entries, entry => entry.Contains("First", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Contains("Third", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ActivityLogRejectsNonPositiveCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticActivityLog(capacity));
    }

    [Fact]
    public void ActivityLogRetainsEveryEntryAtTheExactCapacity()
    {
        var log = new DiagnosticActivityLog(capacity: 2);
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);

        log.Observe(timestamp, CreateSnapshot(status: "First"));
        log.Observe(timestamp.AddSeconds(1), CreateSnapshot(status: "Second"));

        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(log.Entries, entry => entry.Contains("First", StringComparison.Ordinal));
        Assert.Contains(log.Entries, entry => entry.Contains("Second", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivityLogClearRemovesVisibleHistory()
    {
        // Arrange
        var log = new DiagnosticActivityLog();
        log.Observe(DateTime.UtcNow, CreateSnapshot());

        // Act
        log.Clear();

        // Assert
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ActiveUnchangedStateProducesBoundedWaitingNotice()
    {
        // Arrange
        var log = new DiagnosticActivityLog();
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);
        var snapshot = CreateSnapshot();
        log.Observe(timestamp, snapshot);

        // Act
        var tooSoon = log.Observe(timestamp.AddSeconds(7), snapshot);
        var firstWaiting = log.Observe(timestamp.AddSeconds(15), snapshot);
        var rateLimited = log.Observe(timestamp.AddSeconds(20), snapshot);
        var secondWaiting = log.Observe(timestamp.AddSeconds(30), snapshot);

        // Assert
        Assert.Equal(DiagnosticObservationKind.None, tooSoon.Kind);
        Assert.Equal(DiagnosticObservationKind.Waiting, firstWaiting.Kind);
        Assert.Equal(DiagnosticObservationKind.None, rateLimited.Kind);
        Assert.Equal(DiagnosticObservationKind.Waiting, secondWaiting.Kind);
        Assert.Contains("STILL WAITING", firstWaiting.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitingNoticeUsesInclusiveThresholdsAndExactRateLimit()
    {
        var log = new DiagnosticActivityLog();
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);
        var snapshot = CreateSnapshot();
        log.Observe(timestamp, snapshot);

        var atStateThreshold = log.Observe(timestamp.AddSeconds(8), snapshot);
        var beforeInterval = log.Observe(timestamp.AddSeconds(14), snapshot);
        var atInterval = log.Observe(timestamp.AddSeconds(15), snapshot);

        Assert.Equal(DiagnosticObservationKind.None, atStateThreshold.Kind);
        Assert.Equal(DiagnosticObservationKind.None, beforeInterval.Kind);
        Assert.Equal(DiagnosticObservationKind.Waiting, atInterval.Kind);
    }

    [Fact]
    public void SnapshotReportCapturesNativeUiAndAutomationRequests()
    {
        // Arrange
        var snapshot = CreateSnapshot() with
        {
            CurrentItem = "Sample Hat",
            DresserOpen = true,
            GlamourCreationOpen = true,
            ItemConfirmationOpen = true,
            NavigationIssued = true,
            ConfirmationIssued = true,
        };

        // Act
        var summary = snapshot.ToActivitySummary();
        var report = string.Join(Environment.NewLine, snapshot.ToReportLines());

        // Assert
        Assert.Contains("dresser,creation,item-confirm", report, StringComparison.Ordinal);
        Assert.Contains("item=Sample Hat", summary, StringComparison.Ordinal);
        Assert.Contains("navigation=True", report, StringComparison.Ordinal);
        Assert.Contains("confirmation=True", report, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotReportDistinguishesUnavailableCountsAndEveryNativeWindow()
    {
        // Arrange
        var snapshot = CreateSnapshot() with
        {
            PrismCountReady = false,
            DresserLoaded = false,
            CabinetLoaded = false,
            CandidateListReady = false,
            DresserOpen = true,
            ArmoireOpen = true,
            GlamourCreationOpen = true,
            OutfitCreationOpen = true,
            OutfitConfirmationOpen = true,
            ItemConfirmationOpen = true,
            InventoryContextOpen = true,
            ExistingOutfitPromptOpen = true,
        };

        // Act
        var summary = snapshot.ToActivitySummary();
        var report = string.Join(Environment.NewLine, snapshot.ToReportLines());

        // Assert
        Assert.Contains("prisms=unknown", summary, StringComparison.Ordinal);
        Assert.Contains("Prisms: unavailable", report, StringComparison.Ordinal);
        Assert.Contains("dresser,armoire,creation,outfit,outfit-confirm,item-confirm,context,merge-prompt", report, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotSummaryAndReportPreserveEveryReadyFieldExactly()
    {
        var snapshot = CreateSnapshot();

        Assert.Equal(
            "InventoryToDresser/ScanningInventoryForDresser: Scanning | item=none | stored=0/2 | queues=1 outfits,1 items | prisms=10/2 | ui=none | attempts=0/0",
            snapshot.ToActivitySummary());
        Assert.Equal(ReadyReportLines, snapshot.ToReportLines());
    }

    [Fact]
    public void SnapshotSummaryAndReportPreserveUnavailableDataAndAllOpenWindowsExactly()
    {
        var snapshot = CreateSnapshot(phase: MigrationPhase.StoringDresserItem, status: "Working") with
        {
            Build = "build-42",
            CurrentItem = "Sample Hat",
            StoredCount = 3,
            TotalPieceCount = 9,
            EligibleCount = 7,
            PrismsHeld = 4,
            PrismsRequired = 8,
            PrismCountReady = false,
            DresserLoaded = false,
            CabinetLoaded = false,
            CandidateListReady = false,
            DresserOpen = true,
            ArmoireOpen = true,
            GlamourCreationOpen = true,
            OutfitCreationOpen = true,
            OutfitConfirmationOpen = true,
            ItemConfirmationOpen = true,
            InventoryContextOpen = true,
            ExistingOutfitPromptOpen = true,
            PendingOutfits = 2,
            PendingItems = 5,
            SourceCategoryPosition = 3,
            SourceCategoryCount = 5,
            InteractionAttempts = 4,
            DresserOpenAttempts = 6,
            NavigationIssued = true,
            InteractionIssued = true,
            MenuSelectionIssued = true,
            ConfirmationIssued = true,
        };

        Assert.Equal(
            "InventoryToDresser/StoringDresserItem: Working | item=Sample Hat | stored=3/9 | queues=2 outfits,5 items | prisms=unknown/8 | ui=dresser,armoire,creation,outfit,outfit-confirm,item-confirm,context,merge-prompt | attempts=4/6",
            snapshot.ToActivitySummary());
        Assert.Equal(UnavailableReportLines, snapshot.ToReportLines());
    }

    [Fact]
    public void InactiveRepeatedStateNeverProducesWaitingWarning()
    {
        // Arrange
        var log = new DiagnosticActivityLog();
        var timestamp = new DateTime(2026, 8, 30, 12, 0, 0);
        var snapshot = CreateSnapshot(phase: MigrationPhase.Idle, status: "Idle");
        log.Observe(timestamp, snapshot);

        // Act
        var observation = log.Observe(timestamp.AddMinutes(1), snapshot);

        // Assert
        Assert.Equal(DiagnosticObservationKind.None, observation.Kind);
        Assert.Single(log.Entries);
    }

    [Theory]
    [InlineData((int)MigrationPhase.Complete)]
    [InlineData((int)MigrationPhase.Paused)]
    [InlineData((int)MigrationPhase.Cancelled)]
    public void TerminalMigrationPhasesAreNotActive(int phaseValue)
    {
        // Arrange
        var phase = (MigrationPhase)phaseValue;
        var snapshot = CreateSnapshot(phase: phase, status: "Stopped");

        // Act
        var active = snapshot.IsActive;

        // Assert
        Assert.False(active);
    }

    [Fact]
    public void SupportReportRetainsFailureAndExcludesUntrustedText()
    {
        var diagnostics = new SupportDiagnostics();
        var snapshot = CreateSnapshot() with
        {
            Build = "1.0.2.52",
            Status = @"C:\private\secret token=ABC",
            CurrentItem = "Character Secret",
            StoredCount = 3
        };
        var unavailable = SpringCleaning.Shared.CollectionData.CollectionDataSourceDiagnostic.Unavailable;
        var sources = new SpringCleaning.Shared.CollectionData.CollectionDataStatusSnapshot(
            SpringCleaning.Shared.CollectionData.CollectionDataFailure.None, false, false,
            "private-provider", "secret-version", null, null,
            unavailable, unavailable, unavailable, unavailable, unavailable, unavailable, unavailable);
        Assert.False(diagnostics.Compare("GlamourPrismsAvailable", 1, 0));
        diagnostics.RetainFailure(snapshot, sources, "No Glamour Prisms");
        Assert.True(diagnostics.Compare("GlamourPrismsAvailable", 1, 1));
        var module = Guid.NewGuid();
        var report = diagnostics.Serialize(snapshot with { Phase = MigrationPhase.Cancelled, StoredCount = 0 }, sources, module);
        using var json = System.Text.Json.JsonDocument.Parse(report);
        Assert.Equal(module.ToString(), json.RootElement.GetProperty("loadedAssemblyMvid").GetString());
        Assert.Equal("SpringCleaning", json.RootElement.GetProperty("plugin").GetString());
        Assert.Equal("Uninstrumented native comparisons are not inferred from window visibility.",
            json.RootElement.GetProperty("unavailableChecks").GetString());
        Assert.Contains("\n  \"schemaVersion\"", report);
        var failure = json.RootElement.GetProperty("lastFailure");
        Assert.Equal(3, failure.GetProperty("ConfirmedActions").GetInt32());
        Assert.Equal("Failure", failure.GetProperty("Checks")[0].GetProperty("State").GetString());
        Assert.Equal("Pass", json.RootElement.GetProperty("current").GetProperty("checks")[0].GetProperty("State").GetString());
        Assert.DoesNotContain("secret", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Character", report);
        Assert.True(report.Length < 32768);
    }

    [Fact]
    public void SupportReportBoundsChecksAndPreservesUnknown()
    {
        var diagnostics = new SupportDiagnostics();
        for (var i = 0; i < 100; i++) diagnostics.Compare($"Check{i}", 1, null);
        Assert.False(diagnostics.Compare("Unavailable", 1, null));
    }

    [Fact]
    public void SupportChecksEvictOldestAtCapacityAndUpdateWithoutEviction()
    {
        var diagnostics = new SupportDiagnostics();
        for (var i = 0; i < 32; i++) diagnostics.Compare($"Check{i}", i, i);
        diagnostics.Compare("Check12", 12, null);
        using (var report = System.Text.Json.JsonDocument.Parse(diagnostics.Serialize(CreateSnapshot(), EmptySources(), Guid.Empty)))
        {
            var checks = report.RootElement.GetProperty("current").GetProperty("checks");
            Assert.Equal(32, checks.GetArrayLength());
            Assert.Contains(checks.EnumerateArray(), c => c.GetProperty("Name").GetString() == "Check0");
            var updated = checks.EnumerateArray().Single(c => c.GetProperty("Name").GetString() == "Check12");
            Assert.Equal("Not Available", updated.GetProperty("State").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, updated.GetProperty("Observed").ValueKind);
        }
        diagnostics.Compare("NewCheck", 1, 0);
        using var final = System.Text.Json.JsonDocument.Parse(diagnostics.Serialize(CreateSnapshot(), EmptySources(), Guid.Empty));
        var finalChecks = final.RootElement.GetProperty("current").GetProperty("checks");
        Assert.Equal(32, finalChecks.GetArrayLength());
        Assert.DoesNotContain(finalChecks.EnumerateArray(), c => c.GetProperty("Name").GetString() == "Check0");
        Assert.Contains(finalChecks.EnumerateArray(), c => c.GetProperty("Name").GetString() == "NewCheck");
        Assert.Equal("unknown", final.RootElement.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("PRISM secret", "GlamourPrismsUnavailable")]
    [InlineData("CONFIRM secret", "ConfirmationRejectedOrTimedOut")]
    [InlineData("CHANGED secret", "EvidenceChanged")]
    [InlineData("secret", "OperationStopped")]
    public void SupportFailureStoresOnlyReasonCategory(string reason, string category)
    {
        var diagnostics = new SupportDiagnostics();
        diagnostics.RetainFailure(CreateSnapshot(), EmptySources(), reason);
        using var report = System.Text.Json.JsonDocument.Parse(diagnostics.Serialize(CreateSnapshot(), EmptySources(), Guid.Empty));
        Assert.Equal(category, report.RootElement.GetProperty("lastFailure").GetProperty("Reason").GetString());
        Assert.DoesNotContain("secret", report.RootElement.GetRawText());
    }

    [Fact]
    public void OperationStopRetainsItsOwnCountAndChecks()
    {
        var diagnostics = new SupportDiagnostics();
        Assert.False(diagnostics.Compare("PromptOwned", 1, 0));
        diagnostics.RetainOperationFailure("DuplicateDiscard", 4, EmptySources());
        Assert.True(diagnostics.Compare("PromptOwned", 1, 1));
        using var report = System.Text.Json.JsonDocument.Parse(diagnostics.Serialize(CreateSnapshot(), EmptySources(), Guid.Empty));
        var failure = report.RootElement.GetProperty("lastFailure");
        Assert.Equal("DuplicateDiscard", failure.GetProperty("Phase").GetString());
        Assert.Equal("OperationStopped", failure.GetProperty("Reason").GetString());
        Assert.Equal(4, failure.GetProperty("ConfirmedActions").GetInt32());
        Assert.Equal(0, failure.GetProperty("Checks")[0].GetProperty("Observed").GetInt64());
        Assert.Equal(1, report.RootElement.GetProperty("current").GetProperty("checks")[0].GetProperty("Observed").GetInt64());
    }

    [Fact]
    public void SupportSourceAgeIsUnknownOrNonnegativeWithoutProviderReason()
    {
        var diagnostics = new SupportDiagnostics();
        var now = DateTimeOffset.UtcNow;
        var sources = EmptySources() with
        {
            Inventory = SpringCleaning.Shared.CollectionData.CollectionDataSourceDiagnostic.Unavailable with
            { ObservedAtUtc = now.AddSeconds(-20), Reason = "secret-provider-reason" },
            Armoire = SpringCleaning.Shared.CollectionData.CollectionDataSourceDiagnostic.Unavailable with
            { ObservedAtUtc = now.AddDays(1) },
        };
        using var report = System.Text.Json.JsonDocument.Parse(diagnostics.Serialize(CreateSnapshot(), sources, Guid.Empty));
        var data = report.RootElement.GetProperty("current").GetProperty("sources");
        Assert.True(data.GetProperty("inventory").GetProperty("ageSeconds").GetDouble() >= 20);
        Assert.Equal(0, data.GetProperty("armoire").GetProperty("ageSeconds").GetDouble());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, data.GetProperty("armouryChest").GetProperty("ageSeconds").ValueKind);
        Assert.DoesNotContain("secret-provider-reason", report.RootElement.GetRawText());
    }
    private static SpringCleaning.Shared.CollectionData.CollectionDataStatusSnapshot EmptySources()
    {
        var unavailable = SpringCleaning.Shared.CollectionData.CollectionDataSourceDiagnostic.Unavailable;
        return new(SpringCleaning.Shared.CollectionData.CollectionDataFailure.None, false, false,
            "private-provider", "secret-version", null, null,
            unavailable, unavailable, unavailable, unavailable, unavailable, unavailable, unavailable);
    }
    private static MigrationDiagnosticSnapshot CreateSnapshot(
        MigrationPhase phase = MigrationPhase.ScanningInventoryForDresser,
        string status = "Scanning") => new(
            Build: "test",
            Mode: MigrationMode.InventoryToDresser,
            Phase: phase,
            Status: status,
            CurrentItem: null,
            StoredCount: 0,
            TotalPieceCount: 2,
            EligibleCount: 2,
            PrismsHeld: 10,
            PrismsRequired: 2,
            PrismCountReady: true,
            DresserLoaded: true,
            CabinetLoaded: true,
            CandidateListReady: true,
            DresserOpen: false,
            ArmoireOpen: false,
            GlamourCreationOpen: false,
            OutfitCreationOpen: false,
            OutfitConfirmationOpen: false,
            ItemConfirmationOpen: false,
            InventoryContextOpen: false,
            ExistingOutfitPromptOpen: false,
            PendingOutfits: 1,
            PendingItems: 1,
            SourceCategoryPosition: 1,
            SourceCategoryCount: 1,
            InteractionAttempts: 0,
            DresserOpenAttempts: 0,
            NavigationIssued: false,
            InteractionIssued: false,
            MenuSelectionIssued: false,
            ConfirmationIssued: false);
}
