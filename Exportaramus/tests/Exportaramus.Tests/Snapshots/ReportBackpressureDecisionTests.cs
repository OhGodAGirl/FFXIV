using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class ReportBackpressureDecisionTests
{
    [Fact]
    public void ReporterRefillAndOtherLabelsCannotBypassThePerReporterRate()
    {
        var limiter = new ReporterRateLimiter();
        for (var i = 0; i < 10; i++) Assert.True(limiter.TryAccept("A", 0));
        Assert.True(limiter.TryAccept("B", 0));
        Assert.False(limiter.TryAccept("A", 0));
        Assert.False(limiter.TryAccept("A", 1));
        Assert.True(limiter.TryAccept("A", 2));
        Assert.False(limiter.TryAccept("A", 2));
    }

    [Fact]
    public void EarlierClockSamplesDoNotMintTokensWhenTheClockReturns()
    {
        var limiter = new ReporterRateLimiter();
        for (var i = 0; i < 10; i++) Assert.True(limiter.TryAccept("A", 0));
        Assert.True(limiter.TryAccept("A", 4));
        Assert.True(limiter.TryAccept("A", 0));
        Assert.False(limiter.TryAccept("A", 4));
    }

    [Fact]
    public void ExpiredReporterLabelsDoNotAccumulateWithoutBound()
    {
        var limiter = new ReporterRateLimiter();
        for (var i = 0; i < 1000; i++) Assert.True(limiter.TryAccept("Reporter " + i, i * 2));
        // Inspect retained storage to prove the documented resource bound, not just successful admission.
        var field = typeof(ReporterRateLimiter).GetField("reporters", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.InRange(((IDictionary)field.GetValue(limiter)!).Count, 1, 40);
    }

    [Fact]
    public void AcceptedKeysReserveCapacityUntilPersistenceCompletesAndScopeResetClearsOldWork()
    {
        var queue = new ReportedEvidenceQueue();
        var items = Enumerable.Range(1, 10001).Select(value => (uint)value).ToImmutableHashSet();
        var retained = Enumerable.Range(1, 9999).Select(value => new ReportedEvidence((uint)value, "Prior", SnapshotFixture.Now));
        queue.SetScope(123, items, retained);
        Assert.Equal("Accepted", queue.TryEnqueue("123", 10000, "New", 0, SnapshotFixture.Now));
        Assert.Single(queue.TakeBatch());
        Assert.Equal("CapacityReached", queue.TryEnqueue("123", 10001, "New", 1, SnapshotFixture.Now));
        Assert.Equal("Accepted", queue.TryEnqueue("123", 10000, "New", 2, SnapshotFixture.Now));

        queue.SetScope(124, items, []);

        Assert.Empty(queue.TakeBatch());
        for (var i = 0; i < 10; i++) Assert.Equal("Accepted", queue.TryEnqueue("124", 10001, "New", 2, SnapshotFixture.Now));
        Assert.Equal("RateLimited", queue.TryEnqueue("124", 10001, "New", 2, SnapshotFixture.Now));
        queue.Stop();
        Assert.Equal(0, queue.Count);
        Assert.False(queue.Available);
        Assert.Empty(queue.TakeBatch());
        Assert.Equal("ProviderStopping", queue.TryEnqueue("124", 1, "New", 3, SnapshotFixture.Now));
    }

    [Fact]
    public void QueueBatchUsesItemThenOrdinalReporterOrder()
    {
        var queue = new ReportedEvidenceQueue(); queue.SetScope(123, [1, 2], []);
        foreach (var (item, reporter) in new[] { (2u, "Z"), (1u, "Z"), (1u, "A") })
            Assert.Equal("Accepted", queue.TryEnqueue("123", item, reporter, 0, SnapshotFixture.Now));

        var batch = queue.TakeBatch();

        Assert.Equal(new[] { (1u, "A"), (1u, "Z"), (2u, "Z") }, batch.Select(value => (value.ItemId, value.Reporter)));
        Assert.Empty(queue.TakeBatch());
    }
}
