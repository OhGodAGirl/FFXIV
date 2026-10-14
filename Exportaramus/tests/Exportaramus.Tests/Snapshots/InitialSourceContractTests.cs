using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class InitialSourceContractTests
{
    [Fact]
    public void InitialNativeSourcesExplainTheirUnknownStatusAndReportsRemainIndependent()
    {
        var state = CollectionStateFactory.Empty(123, Guid.NewGuid(), SnapshotFixture.Now);
        foreach (var health in state.Unlocks.Values.Select(source => source.Health).Concat(state.CurrentItemPresence.Values.Select(source => source.Health)))
        {
            Assert.Equal(SourceStatus.NeverObserved, health.SourceStatus);
            Assert.Equal(CapabilityState.UnavailablePendingValidation, health.CapabilityState);
            Assert.Equal(ReasonCode.PatchValidationRequired, health.ReasonCode);
            Assert.False(string.IsNullOrWhiteSpace(health.Reason));
            Assert.Null(health.ObservedAtUtc);
        }
        Assert.Empty(state.ReportedEvidence.Items);
        Assert.Equal(SourceStatus.Fresh, state.ReportedEvidence.Health.SourceStatus);
        Assert.Equal(ReasonCode.None, state.ReportedEvidence.Health.ReasonCode);
        Assert.Empty(state.ReportedEvidence.Health.Reason);
    }
}
