using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Capture;

public sealed class TargetedFreshnessTests
{
    [Fact]
    public void FreshnessChangesAreReportedWhenObservedUnlocksStayTheSame()
    {
        var prior = SnapshotFixture.Create().State;
        var original = prior.Unlocks["orchestrions"];
        var raw = new RawCaptureBatch(ImmutableDictionary<string, PermanentUnlockState>.Empty.Add("orchestrions", original), ImmutableDictionary<string, CurrentItemPresenceState>.Empty, []);
        Assert.False(TargetedCaptureChangePolicy.HasChanges(prior, prior, raw));
        var stale = prior with
        {
            Unlocks = prior.Unlocks.SetItem("orchestrions", original with
            {
                Health = original.Health with
                {
                    SourceStatus = SourceStatus.Stale
                }
            })
        };
        Assert.True(TargetedCaptureChangePolicy.HasChanges(prior, stale, raw));
        Assert.True(TargetedCaptureChangePolicy.HasChanges(stale, prior, raw));
    }
}
