using System.Globalization;

namespace CollectionShopper.Tests;

/// <summary>
/// Verifies diagnostics remain deterministic, honest, and safe to share.
/// </summary>
public sealed class DiagnosticTests
{
    [Theory]
    [InlineData(@"C:\Users\PrivateUser\notes.txt")]
    [InlineData("C:/Users/PrivateUser/notes.txt")]
    [InlineData(@"\\PrivateHost\PrivateShare\notes.txt")]
    [InlineData(@"\\?\C:\Users\PrivateUser\notes.txt")]
    [InlineData(@"\\?\UNC\PrivateHost\PrivateShare\notes.txt")]
    public void ReportRemovesWindowsPathVariantsFromRetainedFailureAndSources(string path)
    {
        var sources = new[] { new CollectionDataOwnershipSourceState("inventory", "Unavailable", false, 0, null, path) };
        var snapshot = new DiagnosticSnapshot("Collection Shopper", "CollectionShopper", "0.1.77.8", 15,
            "Development", DateTimeOffset.UnixEpoch, [],
            Operation: new("Idle", path, 0, [new("Path check", null, path, path)],
                new(DateTimeOffset.UnixEpoch, "WaitingForListings", path, 0, [], sources)), Sources: sources);

        var report = DiagnosticReport.Build(snapshot);
        using var document = System.Text.Json.JsonDocument.Parse(report);
        var root = document.RootElement;
        Assert.Equal("[local path removed]", root.GetProperty("current").GetProperty("status").GetString());
        Assert.Equal("[local path removed]", root.GetProperty("lastFailure").GetProperty("reason").GetString());
        Assert.Equal("[local path removed]", root.GetProperty("sources")[0].GetProperty("reason").GetString());
        Assert.Equal("[local path removed]", root.GetProperty("lastFailure").GetProperty("sources")[0].GetProperty("reason").GetString());
        Assert.Equal("[local path removed]", root.GetProperty("current").GetProperty("checks")[0].GetProperty("expected").GetString());
        Assert.Equal("[local path removed]", root.GetProperty("current").GetProperty("checks")[0].GetProperty("observed").GetString());
        Assert.DoesNotContain("Private", report, StringComparison.Ordinal);
    }

    [Fact]
    public void PathRedactionPreservesPublicLinksAndNonPathDiagnosticValues()
    {
        const string text = "https://universalis.app/api/v2\nhttps://ffxivcollect.com/api/minions\nVersion 0.1.77.8\n1,050 gil";
        Assert.Equal(text, DiagnosticReport.Redact(text));
    }

    [Fact]
    public void ReportBoundsBothCurrentAndRetainedSourceAndPredicateCollections()
    {
        var sources = Enumerable.Range(0, 80).Select(index =>
            new CollectionDataOwnershipSourceState($"source-{index}", "Unavailable", false, 50, null, new string('s', 1_000))).ToArray();
        var predicates = Enumerable.Range(0, 80).Select(index =>
            new DiagnosticPredicate($"check-{index}", null, new string('e', 1_000), new string('o', 1_000))).ToArray();
        var snapshot = new DiagnosticSnapshot("Collection Shopper", "CollectionShopper", "0.1.77.8", 15,
            "Development", DateTimeOffset.UnixEpoch, [],
            Operation: new("Idle", new string('x', 1_000), 0, predicates,
                new(DateTimeOffset.UnixEpoch, "WaitingForListings", new string('r', 1_000), 0, predicates, sources)),
            Sources: sources, CollectionCompleteness: predicates);

        using var document = System.Text.Json.JsonDocument.Parse(DiagnosticReport.Build(snapshot));
        var root = document.RootElement;
        Assert.Equal(48, root.GetProperty("collectionCompleteness").GetArrayLength());
        Assert.Equal(32, root.GetProperty("sources").GetArrayLength());
        Assert.Equal(48, root.GetProperty("current").GetProperty("checks").GetArrayLength());
        Assert.Equal(48, root.GetProperty("lastFailure").GetProperty("checks").GetArrayLength());
        Assert.Equal(32, root.GetProperty("lastFailure").GetProperty("sources").GetArrayLength());
        Assert.Equal(384, root.GetProperty("current").GetProperty("status").GetString()!.Length);
        Assert.Equal(384, root.GetProperty("lastFailure").GetProperty("reason").GetString()!.Length);
        Assert.Equal(384, root.GetProperty("sources")[0].GetProperty("reason").GetString()!.Length);
        Assert.Equal(384, root.GetProperty("current").GetProperty("checks")[0].GetProperty("expected").GetString()!.Length);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            root.GetProperty("lastFailure").GetProperty("checks")[0].GetProperty("passed").ValueKind);
    }

    [Fact]
    public void JsonIncludesSourceAgeAndCompletenessWithoutInventoryContents()
    {
        var snapshot = new DiagnosticSnapshot("Collection Shopper", "CollectionShopper", "0.1.77.0", 15,
            "Development", DateTimeOffset.UnixEpoch, [], Operation: new("Idle", "Ready", 0, [], null),
            Sources: [new("inventory", "stale", true, 2000, "2026-09-14T00:00:00Z", null)],
            CollectionCompleteness: [new("Outfits", false, "Complete", "False")]);
        using var document = System.Text.Json.JsonDocument.Parse(DiagnosticReport.Build(snapshot));
        var source = document.RootElement.GetProperty("sources")[0];
        Assert.Equal("stale", source.GetProperty("status").GetString());
        Assert.Equal("2026-09-14T00:00:00Z", source.GetProperty("observedAtUtc").GetString());
        Assert.False(source.TryGetProperty("items", out _));
        Assert.False(document.RootElement.GetProperty("collectionCompleteness")[0].GetProperty("passed").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("lastFailure").ValueKind);
    }

    [Fact]
    public void JsonReportIsVersionedBoundedRedactedAndPreservesBuildAndFailureTime()
    {
        var captured = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var buildId = Guid.Parse("12345678-1234-4321-8765-123456789012");
        var predicates = Enumerable.Range(0, 100).Select(index => new DiagnosticPredicate(
            "Retainer name: Private Name", null, "Token=secret", "pointer 0x12345678 " + new string('x', 1000))).ToArray();
        var snapshot = new DiagnosticSnapshot("Collection Shopper", "CollectionShopper", "0.1.77.0", 15,
            "Development", captured, Enumerable.Repeat(new DiagnosticCheck("Check", DiagnosticResultKind.NotAvailable, new string('z', 1000)), 100).ToArray(),
            buildId, new("Idle", "Stopped", 2, predicates,
                new(captured.AddMinutes(-1), "WaitingForPurchase", "Retainer name: Hidden Name", 2, predicates)));
        var report = DiagnosticReport.Build(snapshot);
        using var document = System.Text.Json.JsonDocument.Parse(report);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(buildId, root.GetProperty("loadedBuildMvid").GetGuid());
        Assert.Equal(captured, root.GetProperty("capturedAtUtc").GetDateTimeOffset());
        Assert.Equal(captured.AddMinutes(-1), root.GetProperty("lastFailure").GetProperty("capturedAtUtc").GetDateTimeOffset());
        Assert.Equal(2, root.GetProperty("lastFailure").GetProperty("confirmedActions").GetInt32());
        Assert.Equal(32, root.GetProperty("checks").GetArrayLength());
        Assert.Equal(48, root.GetProperty("current").GetProperty("checks").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("current").GetProperty("checks")[0].GetProperty("passed").ValueKind);
        Assert.DoesNotContain("Private Name", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden Name", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", report, StringComparison.Ordinal);
        Assert.DoesNotContain("0x12345678", report, StringComparison.Ordinal);
        Assert.True(report.Length < 100_000);
        Assert.Contains("\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonWithoutAnObservedOperationDoesNotInventSuccessfulChecks()
    {
        var snapshot = new DiagnosticSnapshot("Collection Shopper", "CollectionShopper", "0.1.77.0", 15,
            "Development", DateTimeOffset.UnixEpoch, []);
        using var document = System.Text.Json.JsonDocument.Parse(DiagnosticReport.Build(snapshot));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("current").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("lastFailure").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("loadedBuildMvid").ValueKind);
    }

    /// <summary>
    /// Verifies checks stay in a stable support-friendly order and unavailable services are not called Pass.
    /// </summary>
    [Fact]
    public void BuildChecksUsesStableOrderAndHonestUnavailableResults()
    {
        // Arrange
        var inputs = new DiagnosticInputs(
            ConfigurationVersion: 1,
            ExpectedConfigurationVersion: 1,
            PlayerLoaded: true,
            CatalogReady: true,
            CollectionLoaded: true,
            CollectionRefreshing: false,
            MarketBoardOpen: false,
            Purchasing: false,
            ProviderSnapshotReady: false,
            CollectionDataOwnership: CollectionDataOwnershipPolicy.NotChecked);

        // Act
        var checks = DiagnosticPolicy.BuildChecks(inputs);

        // Assert
        Assert.Equal(
            [
                new("Configuration", DiagnosticResultKind.Pass, "Configuration schema 1 is current."),
                new("Character session", DiagnosticResultKind.Pass,
                    "A logged-in character session is available."),
                new("Bundled outfit data", DiagnosticResultKind.Pass,
                    "The bundled outfit definitions loaded successfully."),
                new("Collection data", DiagnosticResultKind.Pass,
                    "The current collection list has been loaded."),
                new("Market Board", DiagnosticResultKind.NotAvailable,
                    "The normal Market Board browsing context is not open.",
                    "Open a Market Board before testing shopping controls."),
                new("Automated purchasing", DiagnosticResultKind.Pass,
                    "No automated purchase run is active."),
                new("Exportaramus ownership snapshot", DiagnosticResultKind.NotAvailable,
                    "No accepted Exportaramus ownership snapshot is available yet.",
                    "Keep Exportaramus loaded; ownership arrives automatically when available."),
                new("Exportaramus collection data", DiagnosticResultKind.NotAvailable,
                    "Exportaramus ownership has not been received for the current character.",
                    "Keep Exportaramus loaded. Refresh the named unavailable sources there; unknown ownership never enters the purchase list."),
                new("FFXIV Collect catalog connectivity", DiagnosticResultKind.NotAvailable,
                    "Not tested. Run Checks never contacts the public FFXIV Collect catalog.",
                    "Use Refresh when you deliberately want to refresh catalog metadata."),
                new("Universalis connectivity", DiagnosticResultKind.NotAvailable,
                    "Not tested. Run Checks never contacts Universalis.",
                    "Use Refresh at a Market Board when you deliberately want current price estimates."),
            ],
            checks);
    }

    /// <summary>
    /// Verifies active work, refreshes, and unsupported configuration are represented without changing them.
    /// </summary>
    [Fact]
    public void BuildChecksReportsFailureWaitingAndActiveWork()
    {
        // Arrange
        var inputs = new DiagnosticInputs(
            ConfigurationVersion: 99,
            ExpectedConfigurationVersion: 1,
            PlayerLoaded: false,
            CatalogReady: false,
            CollectionLoaded: false,
            CollectionRefreshing: true,
            MarketBoardOpen: true,
            Purchasing: true,
            ProviderSnapshotReady: true,
            CollectionDataOwnership: new CollectionDataOwnershipState(
                true,
                Exportaramus.ConsumerKit.V1_2.CollectionDataValidationCode.None,
                ExportaramusFixture.Snapshot(),
                [100U, 200U],
                [],
                "Exportaramus supplied fresh current-character ownership."));

        // Act
        var checks = DiagnosticPolicy.BuildChecks(inputs);

        // Assert
        Assert.Equal(
            [
                new("Configuration", DiagnosticResultKind.Failure,
                    "The saved configuration schema is not supported by this build.",
                    "Reload the plugin. If the failure remains, copy this report and open an issue."),
                new("Character session", DiagnosticResultKind.Waiting,
                    "No logged-in character session is available.",
                    "Log in to a character, then run the checks again."),
                new("Bundled outfit data", DiagnosticResultKind.NotAvailable,
                    "The bundled outfit definitions have not been observed in the current collection cache.",
                    "Refresh the collection to load the bundled outfit definitions."),
                new("Collection data", DiagnosticResultKind.Waiting,
                    "A collection refresh is currently running.",
                    "Wait for Refresh to finish, then run the checks again."),
                new("Market Board", DiagnosticResultKind.Pass,
                    "The normal Market Board browsing context is open."),
                new("Automated purchasing", DiagnosticResultKind.Warning,
                    "An automated purchase run is active. Diagnostics did not change it.",
                    "Use Cancel Automated Purchasing beside the progress bar if you need to stop it."),
                new("Exportaramus ownership snapshot", DiagnosticResultKind.Pass,
                    "Exportaramus supplied character-bound ownership from its canonical sources."),
                new("Exportaramus collection data", DiagnosticResultKind.Pass,
                    "Exportaramus supplied fresh current-character ownership."),
                new("FFXIV Collect catalog connectivity", DiagnosticResultKind.NotAvailable,
                    "Not tested. Run Checks never contacts the public FFXIV Collect catalog.",
                    "Use Refresh when you deliberately want to refresh catalog metadata."),
                new("Universalis connectivity", DiagnosticResultKind.NotAvailable,
                    "Not tested. Run Checks never contacts Universalis.",
                    "Use Refresh at a Market Board when you deliberately want current price estimates."),
            ],
            checks);
    }

    /// <summary>
    /// Verifies the idle-before-refresh collection state retains exact warning guidance.
    /// </summary>
    [Fact]
    public void BuildChecksReportsExactUnloadedCollectionState()
    {
        var inputs = new DiagnosticInputs(
            1,
            1,
            true,
            true,
            false,
            false,
            true,
            false,
            true,
            CollectionDataOwnershipPolicy.NotChecked);

        var check = DiagnosticPolicy.BuildChecks(inputs)[3];

        Assert.Equal(new DiagnosticCheck(
            "Collection data",
            DiagnosticResultKind.Warning,
            "The collection list has not been loaded in this session.",
            "Open Settings and select Refresh."), check);
    }

    [Fact]
    public void BuildChecksReportsUnknownUnlockCategory()
    {
        var inputs = new DiagnosticInputs(
            1,
            1,
            true,
            true,
            true,
            false,
            true,
            false,
            true,
            new CollectionDataOwnershipState(
                true,
                Exportaramus.ConsumerKit.V1_2.CollectionDataValidationCode.None,
                ExportaramusFixture.Snapshot() with { Unlocks = ExportaramusFixture.Snapshot().Unlocks.Remove("minions") },
                [100U],
                [],
                "Exportaramus supplied ownership, but absence remains unknown."));

        var check = DiagnosticPolicy.BuildChecks(inputs)[7];

        Assert.Equal(new DiagnosticCheck(
            "Exportaramus collection data",
            DiagnosticResultKind.Warning,
            "Exportaramus supplied ownership, but absence remains unknown.",
            "Keep Exportaramus loaded. Refresh the named unavailable sources there; unknown ownership never enters the purchase list."), check);
    }

    /// <summary>
    /// Verifies the shareable report removes identifiers, credentials, URLs containing profile IDs, and local paths.
    /// </summary>
    [Fact]
    public void BuildRedactsPersonalAndMachineSpecificValues()
    {
        // Arrange
        var checks = new[]
        {
            new DiagnosticCheck(
                "Adverse fixture",
                DiagnosticResultKind.Warning,
                "Character name: Example Character\nCharacter ID: 61496107\n"
                + "Authorization: Bearer private-value\nToken=secret\nCookie: session\n"
                + "Profile https://ffxivcollect.com/characters/61496107\n"
                + @"Path C:\Users\Example\AppData\Local\secret.txt"),
        };
        var snapshot = new DiagnosticSnapshot(
            PluginIdentity.PublicName,
            PluginIdentity.InternalName,
            "0.1.65.0",
            PluginIdentity.DalamudApiLevel,
            "Development",
            new DateTimeOffset(2026, 8, 30, 12, 34, 56, TimeSpan.Zero),
            checks);

        // Act
        var report = DiagnosticReport.Build(snapshot);

        // Assert
        Assert.DoesNotContain("Example Character", report, StringComparison.Ordinal);
        Assert.DoesNotContain("61496107", report, StringComparison.Ordinal);
        Assert.DoesNotContain("private-value", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", report, StringComparison.Ordinal);
        Assert.DoesNotContain("session", report, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Character name: [redacted]", report, StringComparison.Ordinal);
        using var parsed = System.Text.Json.JsonDocument.Parse(report);
    }

    /// <summary>
    /// Verifies successful checks do not add a meaningless recovery line.
    /// </summary>
    [Fact]
    public void BuildOmitsEmptyRecoveryText()
    {
        // Arrange
        var snapshot = new DiagnosticSnapshot(
            PluginIdentity.PublicName,
            PluginIdentity.InternalName,
            "0.1.65.0",
            PluginIdentity.DalamudApiLevel,
            "Development",
            new DateTimeOffset(2026, 8, 30, 12, 34, 56, TimeSpan.Zero),
            [new DiagnosticCheck("Configuration", DiagnosticResultKind.Pass, "Ready.")]);

        // Act
        var report = DiagnosticReport.Build(snapshot);

        // Assert
        using var parsed = System.Text.Json.JsonDocument.Parse(report);
        Assert.Equal(string.Empty, parsed.RootElement.GetProperty("checks")[0].GetProperty("recovery").GetString());
    }

    /// <summary>
    /// Verifies actionable checks include their recovery guidance in the report.
    /// </summary>
    [Fact]
    public void BuildIncludesRecoveryText()
    {
        // Arrange
        var snapshot = new DiagnosticSnapshot(
            PluginIdentity.PublicName,
            PluginIdentity.InternalName,
            "0.1.65.0",
            PluginIdentity.DalamudApiLevel,
            "Development",
            new DateTimeOffset(2026, 8, 30, 12, 34, 56, TimeSpan.Zero),
            [new DiagnosticCheck(
                "Collection data",
                DiagnosticResultKind.Warning,
                "Not loaded.",
                "Refresh Collection in Settings.")]);

        // Act
        var report = DiagnosticReport.Build(snapshot);

        // Assert
        using var parsed = System.Text.Json.JsonDocument.Parse(report);
        Assert.Equal("Refresh Collection in Settings.", parsed.RootElement.GetProperty("checks")[0].GetProperty("recovery").GetString());
    }

    /// <summary>
    /// Verifies every report label, outcome format, metadata field, and footer is stable.
    /// </summary>
    [Fact]
    public void BuildProducesExactStableReport()
    {
        var snapshot = new DiagnosticSnapshot(
            "Fixture Plugin",
            "FixtureInternal",
            "1.2.3.4",
            15,
            "Development",
            new DateTimeOffset(2026, 8, 30, 12, 34, 56, TimeSpan.Zero),
            [
                new DiagnosticCheck("Ready", DiagnosticResultKind.Pass, "Ready."),
                new DiagnosticCheck("Offline", DiagnosticResultKind.NotAvailable, "Unavailable.", "Open it."),
            ]);

        var report = DiagnosticReport.Build(snapshot);

        using var parsed = System.Text.Json.JsonDocument.Parse(report);
        var root = parsed.RootElement;
        Assert.Equal("Fixture Plugin", root.GetProperty("plugin").GetString());
        Assert.Equal("FixtureInternal", root.GetProperty("internalName").GetString());
        Assert.Equal("1.2.3.4", root.GetProperty("version").GetString());
        Assert.Equal(15, root.GetProperty("api").GetInt32());
        Assert.Equal("Development", root.GetProperty("channel").GetString());
        Assert.Equal("Pass", root.GetProperty("checks")[0].GetProperty("outcome").GetString());
        Assert.Equal("Not Available", root.GetProperty("checks")[1].GetProperty("outcome").GetString());
        Assert.Equal("Open it.", root.GetProperty("checks")[1].GetProperty("recovery").GetString());
        Assert.Equal(string.Empty, root.GetProperty("checks")[0].GetProperty("recovery").GetString());
    }

    /// <summary>
    /// Verifies every redaction class uses its exact public replacement token.
    /// </summary>
    [Fact]
    public void RedactProducesExactReplacementTokens()
    {
        const string input = "Token=private\nCharacter ID: 61496107\n"
            + "https://ffxivcollect.com/characters/61496107\n"
            + "C:\\Users\\Fixture\\secret.txt\nAccount 12345678\nAddress 0xABCDEF";

        var redacted = DiagnosticReport.Redact(input);

        Assert.Equal(
            "Token: [redacted]\nCharacter ID: [redacted]\n"
            + "https://ffxivcollect.com/characters/[redacted]\n"
            + "[local path removed]\nAccount [identifier removed]\nAddress [pointer removed]",
            redacted);
    }

    /// <summary>
    /// Verifies generated identifiers and secret assignments never survive redaction.
    /// </summary>
    [Fact]
    public void GeneratedSensitiveInputsAreAlwaysRedacted()
    {
        var random = new Random(20260901);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var secret = $"secret-{random.Next():X8}-{iteration}";
            var identifier = random.NextInt64(10_000_000, 9_999_999_999);
            var input = $"Authorization: {secret}\nCharacter ID: {identifier}\n"
                + $"https://ffxivcollect.com/characters/{identifier}";

            var redacted = DiagnosticReport.Redact(input);

            Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
            Assert.DoesNotContain(identifier.ToString(CultureInfo.InvariantCulture), redacted, StringComparison.Ordinal);
            Assert.Equal(3, redacted.Split("[redacted]", StringSplitOptions.None).Length - 1);
            Assert.Contains("characters/[redacted]", redacted, StringComparison.Ordinal);
        }
    }
}
