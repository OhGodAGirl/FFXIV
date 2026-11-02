using System.Text.Json;
using EZDelivery.Bootstrap;

namespace EZDelivery.Tests.Interface;

public sealed class DiagnosticReportTests
{
    private static DiagnosticReport Report() => new("1.0", DateTimeOffset.Parse("2026-09-15T00:00:00Z"),
        "0.1.1.0", true, "Supported", "Idle", "Empty", 3, "Ready", 2,
        "Collection data ready", null, "No acquisition reports", [], 100, .01, .03)
        { LoadedBuildId = "test-build", DeliveryDialogCheck = "No dialog" };

    [Fact]
    public void SerializationPreservesPreparedFactsAndUnknownValues()
    {
        var report = Report();
        using var document = JsonDocument.Parse(report.ToJson());
        var root = document.RootElement;
        Assert.Equal("1.0", root.GetProperty("SchemaVersion").GetString());
        Assert.Equal("0.1.1.0", root.GetProperty("PluginVersion").GetString());
        Assert.Equal("test-build", root.GetProperty("LoadedBuildId").GetString());
        Assert.Equal(3, root.GetProperty("ConfirmedDeliveries").GetInt32());
        Assert.Equal(2, root.GetProperty("ConfirmedPurchases").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("IncompleteSource").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("DeliveryDialogCheckedAtUtc").ValueKind);
        Assert.Empty(root.GetProperty("Failures").EnumerateArray());
    }

    [Fact]
    public void SchemaExposesAggregatesWithoutIdentityInventoryOrNativeAddressFields()
    {
        using var document = JsonDocument.Parse(Report().ToJson());
        var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        // This checks the public schema. Native adapter text redaction still needs boundary review.
        Assert.DoesNotContain(fields, field => field.Contains("ContentId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, field => field.Contains("Inventory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, field => field.Contains("Address", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, field => field.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConcurrentSerializationIsDeterministicAndDoesNotCrossReports()
    {
        var expected = Report().ToJson();
        var outputs = new string[100];
        Parallel.For(0, outputs.Length, index => outputs[index] = Report().ToJson());
        Assert.All(outputs, output => Assert.Equal(expected, output));
        Assert.Contains("Different status", (Report() with { ShoppingStatus = "Different status" }).ToJson());
        Assert.Equal(expected, Report().ToJson());
    }
    [Fact]
    public void GeneratedSerializationPreservesExactPublicJsonForPopulatedReports()
    {
        // Arrange: include escaping, nullable dates, non-UTC offsets and failure entries.
        var timestamp = DateTimeOffset.Parse("2026-10-14T15:30:12.1234567-07:00");
        var report = Report() with
        {
            GeneratedAtUtc = timestamp,
            NativeBuildSupported = false,
            NativeStatus = "<Unavailable> & \"quoted\"\nnext line",
            DeliveryPhase = "Stopped",
            DeliveryReason = "Cancelled",
            ConfirmedDeliveries = 17,
            ShoppingStatus = "Unknown \\ path",
            ConfirmedPurchases = 9,
            CollectionStatus = "Collection café",
            IncompleteSource = "Retainers",
            AcquisitionReport = "Pending",
            Failures = new[] { "first\tfailure", "second\rfailure" },
            FrameworkSamples = long.MaxValue,
            MeanFrameworkMilliseconds = 0.123456789,
            MaximumFrameworkMilliseconds = 12.5,
            DeliveryDialogCheck = "Owned",
            LoadedBuildId = "candidate",
            DeliveryDialogCheckedAtUtc = timestamp,
            ShoppingDialogCheck = "Ready",
            DeliveryMappingCheck = "Matched",
            DeliveryRefreshCheck = "Refreshed",
            LastNativeDeliveryRefreshAtUtc = timestamp.AddSeconds(1),
        };
        var expected = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        // Act.
        var actual = report.ToJson();

        // Assert: preserve formatting, property order, escaping, values and null behavior.
        Assert.Equal(expected, actual);
        var empty = Report();
        Assert.Equal(JsonSerializer.Serialize(empty, new JsonSerializerOptions { WriteIndented = true }), empty.ToJson());
    }}
