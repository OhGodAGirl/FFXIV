using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class MainOrchestrationTests
{
    [Fact]
    public void FirstGuidedCaptureCanCompleteAfterStartingWithoutAPublishedSnapshot()
    {
        var tracker = new GuidedCaptureCompletionTracker();
        tracker.Begin(new(GuidedEffectKind.Capture, GuidedSource.Dresser, 1), 0);

        var result = tracker.Apply(Frame, Bundle, null);

        Assert.Equal(GuidedResult.Succeeded, result.CaptureResult);
    }
    [Theory]
    [InlineData(MainIntent.Close, MainUiAction.Close, GuidedCommand.None)]
    [InlineData(MainIntent.SyncNow, MainUiAction.Sync, GuidedCommand.None)]
    [InlineData(MainIntent.ToggleReduceMotion, MainUiAction.ToggleReduceMotion, GuidedCommand.None)]
    [InlineData(MainIntent.GuidedCapture, MainUiAction.None, GuidedCommand.Start)]
    [InlineData(MainIntent.CancelGuided, MainUiAction.None, GuidedCommand.Cancel)]
    [InlineData(MainIntent.ContinueGuided, MainUiAction.None, GuidedCommand.Continue)]
    [InlineData(MainIntent.SavePartial, MainUiAction.None, GuidedCommand.SavePartial)]
    [InlineData(MainIntent.StateDisclosure, MainUiAction.None, GuidedCommand.None)]
    [InlineData(MainIntent.ViewHistory, MainUiAction.None, GuidedCommand.None)]
    [InlineData(MainIntent.CopyJson, MainUiAction.None, GuidedCommand.None)]
    public void IntentPolicyMapsOnlyAuthorizedCommands(MainIntent intent, MainUiAction action, GuidedCommand command)
    {
        var decision = MainIntentPolicy.Decide(intent, false);
        Assert.Equal(action, decision.Action); Assert.Equal(command, decision.GuidedCommand);
    }

    [Fact]
    public void AbsentUnknownAndActiveGuidedIntentsRemainDeliberate()
    {
        Assert.Equal(default, MainIntentPolicy.Decide(null, false));
        Assert.Equal(default, MainIntentPolicy.Decide((MainIntent)999, false));
        Assert.Equal(new(MainUiAction.None, GuidedCommand.Cancel), MainIntentPolicy.Decide(MainIntent.GuidedCapture, true));
    }

    [Fact]
    public void PresentationUpdatesOncePerSecondAndImmediatelyOnEachSemanticChange()
    {
        var policy = new PresentationInvalidationPolicy();
        Assert.True(policy.ShouldRefresh(0, 0, false, GuidedState.Idle));
        Assert.False(policy.ShouldRefresh(.999, 0, false, GuidedState.Idle));
        Assert.True(policy.ShouldRefresh(1, 0, false, GuidedState.Idle));
        Assert.True(policy.ShouldRefresh(1, 1, false, GuidedState.Idle));
        Assert.True(policy.ShouldRefresh(1, 1, true, GuidedState.Idle));
        Assert.True(policy.ShouldRefresh(1, 1, false, GuidedState.Paused));
        Assert.False(policy.ShouldRefresh(1.9, 1, false, GuidedState.Paused));
    }

    private static GuidedFrame Frame => new(TimeSpan.Zero, 123, true, false, true, true, 0, [], 1, true, true, true);
    private static AcceptedSnapshotBundle Bundle => new(SnapshotFixture.Create(), 2, "{}", null, "hash", ImmutableDictionary<string, string>.Empty,
        PreparedFFXIVCollectExport.Unavailable("Pending"), 0, null);

    [Theory]
    [InlineData(GuidedSource.Dresser, "glamourDresser")]
    [InlineData(GuidedSource.Armoire, "armoire")]
    public void CaptureCompletionRequiresNewPublicationAndFreshMatchingSource(GuidedSource source, string key)
    {
        var tracker = new GuidedCaptureCompletionTracker(); var frame = Frame; var bundle = Bundle;
        Assert.Same(frame, tracker.Apply(frame, bundle, null));
        tracker.Begin(new(GuidedEffectKind.Capture, source, 1), 1);
        Assert.Equal(GuidedResult.Pending, tracker.Apply(frame, null, null).CaptureResult);
        Assert.Equal(GuidedResult.Pending, tracker.Apply(frame, bundle with { Revision = 1 }, null).CaptureResult);
        Assert.Equal(GuidedResult.Pending, tracker.Apply(frame, bundle with { Revision = 0 }, null).CaptureResult);
        Assert.Equal(GuidedResult.Succeeded, tracker.Apply(frame, bundle, null).CaptureResult);
        Assert.Equal(GuidedResult.Failed, tracker.Apply(frame, bundle, "Failed").CaptureResult);
        var stale = bundle with { Data = bundle.Data with { SourceHealth = bundle.Data.SourceHealth.SetItem(key, bundle.Data.SourceHealth[key] with { SourceStatus = SourceStatus.Stale }) } };
        Assert.Equal(GuidedResult.Failed, tracker.Apply(frame, stale, null).CaptureResult);
        tracker.Reset(); Assert.Same(frame, tracker.Apply(frame, bundle, "Failed"));
    }

    [Theory]
    [InlineData(GuidedEffectKind.SaveFull)]
    [InlineData(GuidedEffectKind.SavePartial)]
    public void SaveCompletionDoesNotFabricateACaptureResult(GuidedEffectKind kind)
    {
        var tracker = new GuidedCaptureCompletionTracker(); tracker.Begin(new(kind, GuidedSource.Armoire, 1), 1);
        var completed = tracker.Apply(Frame, Bundle, null);
        Assert.Equal(GuidedResult.Succeeded, completed.SaveResult); Assert.Equal(GuidedResult.Pending, completed.CaptureResult);
        Assert.Equal(GuidedResult.Pending, tracker.Apply(Frame, null, null).SaveResult);
        Assert.Equal(GuidedResult.Failed, tracker.Apply(Frame, null, "Failed").SaveResult);
    }
}
