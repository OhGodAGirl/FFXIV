using System.Collections.Immutable;
using System.Linq;
using SpringCleaning.Shared.CollectionData;

namespace SpringCleaning.Tests.CollectionData;

public sealed class CollectionDataStatusPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedCompleteReport =
    [
        "Shared collection state: requested=False; refreshing=False; last result=none; snapshot=available",
        "Shared collection provider: Provider 1.0.0.0",
        "Shared collection capture: revision 9 at 2026-09-02T12:00:00.0000000+00:00",
        "Shared inventory: Unavailable; 0 items",
        "Shared Armoury Chest: Unavailable; 0 items",
        "Shared saddlebag: Unavailable; 0 items",
        "Shared premium saddlebag: Unavailable; 0 items",
        "Shared retainers: Unavailable; 0 items",
        "Shared armoire: Unavailable; 0 items",
        "Shared glamour dresser: Unavailable; 0 items",
    ];

    [Fact]
    public void AcceptedSnapshotProducesRedactedPreviewAndReport()
    {
        var snapshot = CollectionDataStatusPolicy.Create(
            CreateSnapshot(),
            CollectionDataFailure.None,
            refreshRequested: false,
            refreshInProgress: false);

        Assert.Equal(
            "Shared collection data: armoire Stale, glamour dresser NeverObserved. Exportaramus supplies ownership; native UI revalidates every action.",
            snapshot.ToPreviewSummary());
        Assert.Equal(
            "Shared collection state: requested=False; refreshing=False; last result=none; snapshot=available",
            snapshot.ToReportLines()[0]);
        Assert.Equal(
            "Shared collection provider: CollectorsCompendium 0.3.0.0",
            snapshot.ToReportLines()[1]);
        Assert.DoesNotContain(
            snapshot.ToReportLines(),
            line => line.Contains("contentId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "Shared armoire: Stale; 1 items; observed 2026-09-01T12:00:00.0000000+00:00; reason Open the Armoire to refresh this observation.",
            snapshot.ToReportLines());
    }

    [Fact]
    public void FailureWithoutSnapshotStaysUnavailableAndAdvisory()
    {
        var snapshot = CollectionDataStatusPolicy.Create(
            latest: null,
            CollectionDataFailure.ProviderUnavailable,
            refreshRequested: false,
            refreshInProgress: false);

        Assert.Equal(
            "Shared collection data unavailable: provider unavailable. Exportaramus supplies ownership; native UI revalidates every action.",
            snapshot.ToPreviewSummary());
        Assert.Equal(
            "Shared collection provider: unavailable",
            snapshot.ToReportLines()[1]);
        Assert.All(
            snapshot.ToReportLines().Skip(3),
            line => Assert.Contains("Unavailable; 0 items", line, StringComparison.Ordinal));
    }

    [Fact]
    public void LastAcceptedSnapshotRemainsVisibleAfterAFailure()
    {
        var snapshot = CollectionDataStatusPolicy.Create(
            CreateSnapshot(),
            CollectionDataFailure.MalformedJson,
            refreshRequested: true,
            refreshInProgress: false);

        Assert.Equal(
            "Shared collection data: armoire Stale, glamour dresser NeverObserved. malformed json; showing the last accepted snapshot. Native UI revalidates every action.",
            snapshot.ToPreviewSummary());
        Assert.Equal(
            "Shared collection state: requested=True; refreshing=False; last result=malformed json; snapshot=available",
            snapshot.ToReportLines()[0]);
    }

    [Theory]
    [InlineData((int)CollectionDataFailure.None, "none")]
    [InlineData((int)CollectionDataFailure.NoCharacter, "no active character")]
    [InlineData((int)CollectionDataFailure.ProviderUnavailable, "provider unavailable")]
    [InlineData((int)CollectionDataFailure.Busy, "refresh already running")]
    [InlineData((int)CollectionDataFailure.Disposed, "disposed")]
    [InlineData((int)CollectionDataFailure.EmptyPayload, "empty payload")]
    [InlineData((int)CollectionDataFailure.PayloadTooLarge, "payload too large")]
    [InlineData((int)CollectionDataFailure.MalformedJson, "malformed json")]
    [InlineData((int)CollectionDataFailure.UnsupportedProtocol, "unsupported protocol")]
    [InlineData((int)CollectionDataFailure.InvalidProvider, "invalid provider")]
    [InlineData((int)CollectionDataFailure.WrongCharacter, "wrong character")]
    [InlineData((int)CollectionDataFailure.InvalidRevision, "invalid revision")]
    [InlineData((int)CollectionDataFailure.OutOfOrder, "out of order")]
    [InlineData((int)CollectionDataFailure.InvalidTimestamp, "invalid timestamp")]
    [InlineData((int)CollectionDataFailure.InvalidSources, "invalid sources")]
    [InlineData((int)CollectionDataFailure.StaleCompletion, "obsolete completion")]
    [InlineData(999, "unknown")]
    public void EveryFailureHasExactPlayerAndDiagnosticText(int failureValue, string expected)
    {
        var snapshot = CollectionDataStatusPolicy.Create(
            null,
            (CollectionDataFailure)failureValue,
            refreshRequested: false,
            refreshInProgress: false);

        Assert.Equal(
            $"Shared collection data unavailable: {expected}. Exportaramus supplies ownership; native UI revalidates every action.",
            snapshot.ToPreviewSummary());
        Assert.Equal(
            $"Shared collection state: requested=False; refreshing=False; last result={expected}; snapshot=none",
            snapshot.ToReportLines()[0]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EitherQueuedOrActiveRefreshUsesRefreshingPreview(bool requested, bool inProgress)
    {
        var snapshot = CollectionDataStatusPolicy.Create(
            null,
            CollectionDataFailure.Busy,
            requested,
            inProgress);

        Assert.Equal(
            "Shared collection data refreshing. Exportaramus supplies ownership; native UI revalidates every action.",
            snapshot.ToPreviewSummary());
    }

    [Fact]
    public void ReportFormattingDistinguishesProviderCaptureAndSourceFields()
    {
        var unavailable = CollectionDataSourceDiagnostic.Unavailable;
        Assert.Equal(CollectionSourceStatus.Unavailable, unavailable.Status);
        Assert.Equal(0, unavailable.ItemCount);
        Assert.Null(unavailable.ObservedAtUtc);
        Assert.Null(unavailable.Reason);
        Assert.Equal("Source: Unavailable; 0 items", unavailable.ToReportLine("Source"));

        var source = new CollectionDataSourceDiagnostic(
            CollectionSourceStatus.Stale,
            1_234,
            Now,
            "refresh me");
        Assert.Equal(
            "Wardrobe: Stale; 1,234 items; observed 2026-09-02T12:00:00.0000000+00:00; reason refresh me",
            source.ToReportLine("Wardrobe"));

        var noVersion = DirectSnapshot(provider: "Provider", version: null, revision: null, capturedAt: null);
        Assert.Equal("Shared collection provider: Provider", noVersion.ToReportLines()[1]);
        Assert.Equal("Shared collection capture: none", noVersion.ToReportLines()[2]);

        var whitespaceProvider = DirectSnapshot(provider: " ", version: "1.0.0.0", revision: null, capturedAt: Now);
        Assert.Equal("Shared collection provider: unavailable", whitespaceProvider.ToReportLines()[1]);
        Assert.Equal("Shared collection capture: none", whitespaceProvider.ToReportLines()[2]);

        var complete = DirectSnapshot(provider: "Provider", version: "1.0.0.0", revision: 9, capturedAt: Now);
        Assert.Equal(ExpectedCompleteReport, complete.ToReportLines());

        var revisionWithoutCapture = DirectSnapshot(provider: "Provider", version: "1.0.0.0", revision: 9, capturedAt: null);
        Assert.Equal("Shared collection capture: none", revisionWithoutCapture.ToReportLines()[2]);
    }

    [Fact]
    public void ParseResultAcceptanceReflectsTheCanonicalFailureCode()
    {
        var valid = CreateSnapshot();
        Assert.True(new CollectionDataParseResult(valid, CollectionDataFailure.None).Accepted);
        Assert.False(new CollectionDataParseResult(null, CollectionDataFailure.None).Accepted);
        Assert.False(new CollectionDataParseResult(valid, CollectionDataFailure.MalformedJson).Accepted);
        Assert.Throws<ArgumentNullException>(() => CollectionDataParseResult.Accept(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectionDataParseResult.Reject(CollectionDataFailure.None));
    }

    private static CollectionDataStatusSnapshot DirectSnapshot(
        string? provider,
        string? version,
        ulong? revision,
        DateTimeOffset? capturedAt) => new(
            CollectionDataFailure.None,
            false,
            false,
            provider,
            version,
            revision,
            capturedAt,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable,
            CollectionDataSourceDiagnostic.Unavailable);

    private static CollectionDataSnapshot CreateSnapshot() => new(
        "CollectorsCompendium",
        "0.3.0.0",
        42,
        7,
        Now,
        Fresh(1, 7, 99),
        Fresh(2),
        Fresh(),
        Fresh(),
        Fresh(3),
        new CollectionSourceSnapshot(
            CollectionSourceStatus.Stale,
            Now.AddDays(-1),
            [4],
            "Open the Armoire to refresh this observation."),
        new CollectionSourceSnapshot(
            CollectionSourceStatus.NeverObserved,
            null,
            [],
            null));

    private static CollectionSourceSnapshot Fresh(params uint[] itemIds) => new(
        CollectionSourceStatus.Fresh,
        Now,
        itemIds.ToImmutableArray(),
        null);
}
