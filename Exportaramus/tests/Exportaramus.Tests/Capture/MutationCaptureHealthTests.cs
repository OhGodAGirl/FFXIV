using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Capture;

public sealed class MutationCaptureHealthTests
{
    [Theory]
    [InlineData(ReasonCode.None, SourceStatus.Fresh, Completeness.Complete, ProvenanceType.Observed, "")]
    [InlineData(ReasonCode.CaptureFailed, SourceStatus.Unavailable, Completeness.Unknown, ProvenanceType.None,
        "The source could not be completely read. Try again when it is ready.")]
    [InlineData(ReasonCode.PatchValidationRequired, SourceStatus.Unavailable, Completeness.Unknown, ProvenanceType.None,
        "This source requires compatibility review for this game build.")]
    public void HealthExplainsTheActualReadOutcomeWithoutClaimingMissingObservations(
        ReasonCode reason, SourceStatus expectedStatus, Completeness expectedCompleteness, ProvenanceType expectedProvenance, string expectedHint)
    {
        var now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");

        var health = CaptureReadPolicy.Health(DataType.CurrentItemPresence, 72, now, reason);

        Assert.Equal(expectedStatus, health.SourceStatus);
        Assert.Equal(expectedCompleteness, health.Completeness);
        Assert.Equal(expectedProvenance, health.ProvenanceType);
        Assert.Equal(expectedHint, health.Reason);
        Assert.Equal(reason, health.ReasonCode);
        Assert.Equal(reason == ReasonCode.None ? now : (DateTimeOffset?)null, health.ObservedAtUtc);
        Assert.Equal(72, health.CaptureGeneration);
    }

    [Fact]
    public void PresenceAcceptsTheDocumentedMaximumDistinctItems()
    {
        var items = Enumerable.Range(1, 50000).Select(id => new PresenceItem((uint)id, 1));

        var read = CaptureReadPolicy.Presence(items, true, true, 1, DateTimeOffset.UtcNow);

        Assert.Equal(50000, read.Items.Length);
        Assert.Equal(new PresenceItem(1, 1), read.Items[0]);
        Assert.Equal(new PresenceItem(50000, 1), read.Items[^1]);
        Assert.True(read.Health.IsUsable);
        Assert.True(read.SupportsQuantity);
        Assert.Equal(ReasonCode.None, read.Health.ReasonCode);
    }
}
