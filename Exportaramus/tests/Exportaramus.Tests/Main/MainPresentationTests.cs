using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Shared.Ui;

namespace Exportaramus.Tests.Main;

public sealed class MainPresentationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");
    private static MainPresentationInput Input => new(1, MainStatus.Current, true, true, false, true, 25, Now, Now, "", "Ready To Share",
        UiTone.Success, [new("Observed just now", UiTone.Success), new("No collector errors", UiTone.Success), new("IPC available", UiTone.Success)],
        ImmutableDictionary<string, CollectionRowObservation>.Empty, new(GuidedState.Idle, "Ready", "", false, false, false, false, false));
    [Fact]
    public void EveryRichCollectionIsInExactRowMajorOrderAndUnknownIsNeverZero()
    {
        var view = MainPresenter.Prepare(Input, Now);
        Assert.Equal(23, view.Rows.Length);
        Assert.Equal(new[] { "aetherytes", "armoires", "bardings", "cards", "chocobo_routes", "emotes", "fashions", "field_records", "frames", "glamour_dresser", "hairstyles", "leves", "minions", "mounts", "npcs", "occult_records", "orchestrions", "outfits", "quests", "relics", "spells", "survey_records", "achievements" }, view.Rows.Select(x => x.Key));
        Assert.All(view.Rows, x => { Assert.Equal("—", x.Count); Assert.Equal(UiTone.Muted, x.CountTone); });
        Assert.True(view.HasSnapshot);
        Assert.Equal("23 Collections", view.CollectionCountText);
        Assert.Equal("25 Snapshots Retained", view.HistoryCountText);
        Assert.Equal("Latest: Just now", view.LatestSnapshotText);
    }
    [Theory]
    [InlineData(MainStatus.NoCharacter, "Waiting", UiTone.Muted)]
    [InlineData(MainStatus.NoSnapshot, "No Snapshot", UiTone.Muted)]
    [InlineData(MainStatus.Synchronizing, "Synchronizing", UiTone.Working)]
    [InlineData(MainStatus.Current, "Current", UiTone.Success)]
    [InlineData(MainStatus.Partial, "Partial", UiTone.Warning)]
    [InlineData(MainStatus.Error, "Error", UiTone.Error)]
    public void StatusCopyAndToneArePrepared(MainStatus status, string label, UiTone tone)
    {
        var view = MainPresenter.Prepare(Input with { Status = status }, Now);
        Assert.Equal(label, view.StateLabel); Assert.Equal(tone, view.StateTone);
        Assert.Equal(status != MainStatus.Synchronizing, view.CanSync);
        Assert.Equal(status != MainStatus.Synchronizing, view.CanGuide);
    }
    [Fact]
    public void PriorSummaryAndExportRemainWhileSynchronizingOrFailed()
    {
        foreach (var status in new[] { MainStatus.Synchronizing, MainStatus.Error })
        {
            var view = MainPresenter.Prepare(Input with { Status = status }, Now);
            Assert.Equal(23, view.Rows.Length); Assert.True(view.CanCopy);
        }
        var unavailable = MainPresenter.Prepare(Input with { PreparedExportAvailable = false }, Now);
        Assert.False(unavailable.CanCopy); Assert.Equal(23, unavailable.Rows.Length);
    }
    [Fact]
    public void NoCharacterAndNoSnapshotNeverCreateZeroFilledRows()
    {
        var view = MainPresenter.Prepare(Input with { HasCharacter = false, HasSnapshot = false, HistoryCount = -4 }, Now);
        Assert.False(view.CanCopy); Assert.False(view.CanSync); Assert.False(view.CanGuide); Assert.False(view.HasSnapshot); Assert.Equal(0, view.HistoryCount);
        Assert.Equal(25, MainPresenter.Prepare(Input with { HistoryCount = 100 }, Now).HistoryCount);
    }
    [Fact]
    public void GuidedCaptureKeepsSummaryAndProvidesOnlyPreparedStepAndPrompt()
    {
        var guided = Input.Guided with { Active = true, StepLabel = "Opening Dresser", Paused = true, CanSavePartial = true };
        var view = MainPresenter.Prepare(Input with { Guided = guided }, Now);
        Assert.Equal(guided, view.Guided); Assert.Equal("Opening Dresser", view.StateLabel); Assert.Equal(UiTone.Working, view.StateTone);
        Assert.False(view.CanSync); Assert.True(view.CanGuide); Assert.True(view.CanCopy); Assert.Equal(23, view.Rows.Length);
    }
    [Theory]
    [InlineData(5, RowEvidenceKind.PermanentUnlock, "+5", UiTone.Success)]
    [InlineData(0, RowEvidenceKind.Derived, "+0", UiTone.Success)]
    [InlineData(-3, RowEvidenceKind.CurrentItemPresence, "-3", UiTone.Warning)]
    [InlineData(-3, RowEvidenceKind.PermanentUnlock, "-3", UiTone.Error)]
    [InlineData(-3, RowEvidenceKind.Derived, "-3", UiTone.Error)]
    public void DeltasRetainTypedMeaning(int delta, RowEvidenceKind kind, string text, UiTone tone)
    {
        var row = MainPresenter.Row(("mounts", "Mounts", "Mount"), new(1234, true, delta, kind));
        Assert.Equal("1,234", row.Count); Assert.Equal("!", row.Marker); Assert.Equal(text, row.Delta); Assert.Equal(tone, row.DeltaTone);
        var fresh = MainPresenter.Row(("mounts", "Mounts", "Mount"), new(0, false, null, kind)); Assert.Equal("0", fresh.Count); Assert.Equal("", fresh.Marker); Assert.Equal("", fresh.Delta);
    }
    [Theory]
    [InlineData(-10, "Just now")]
    [InlineData(59, "Just now")]
    [InlineData(60, "1 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(3600, "1 hours ago")]
    [InlineData(86399, "23 hours ago")]
    [InlineData(86400, "1 days ago")]
    public void RelativeTimesHaveExplicitBoundaries(int seconds, string expected) => Assert.Equal(expected, MainPresenter.RelativeTime(Now.AddSeconds(-seconds), Now));
    [Fact]
    public void TextAndHealthFactBoundsAreAppliedBeforeDraw()
    {
        var view = MainPresenter.Prepare(Input with { Message = new('x', 200), HealthHeadline = new('y', 200), HealthFacts = [new(new('z', 200), UiTone.Error), new("2", UiTone.Warning), new("3", UiTone.Muted), new("4", UiTone.Primary)] }, Now);
        Assert.Equal(160, view.Message.Length); Assert.Equal(160, view.HealthHeadline.Length); Assert.Equal(3, view.HealthFacts.Length); Assert.Equal(160, view.HealthFacts[0].Text.Length);
        Assert.Equal("Never", MainPresenter.RelativeTime(null, Now));
    }
    [Theory]
    [InlineData(459, false, 1)]
    [InlineData(460, false, 2)]
    [InlineData(699, false, 2)]
    [InlineData(700, false, 3)]
    [InlineData(719, false, 3)]
    [InlineData(720, true, 3)]
    public void ResponsiveBreakpointsAreLogicalUnits(float width, bool wide, int columns)
    {
        var layout = LayoutPolicy.At(width); Assert.Equal(wide, layout.Wide); Assert.Equal(columns, layout.CollectionColumns);
        Assert.Equal(width - 8, layout.OperationsWidth + layout.RightWidth, .001f); Assert.Equal((width - 8) * .54f, layout.OperationsWidth);
    }
    [Fact]
    public void MotionChangesOnlyVisualDisplacementAndTooltipsRemainDeferred()
    {
        Assert.Equal(1.5f, LayoutPolicy.PressOffset(false, true)); Assert.Equal(0, LayoutPolicy.PressOffset(true, true)); Assert.Equal(0, LayoutPolicy.PressOffset(false, false));
        Assert.False(LayoutPolicy.WideDiagnosticActions(589)); Assert.True(LayoutPolicy.WideDiagnosticActions(590));
        Assert.All(Enum.GetValues<TooltipId>(), x => Assert.Null(Tooltips.Approved(x)));
        Assert.Equal(VisualTokens.Muted, VisualTokens.Tone(UiTone.Muted)); Assert.Equal(VisualTokens.Success, VisualTokens.Tone(UiTone.Success));
        Assert.Equal(VisualTokens.Heading, VisualTokens.Tone(UiTone.Working)); Assert.Equal(VisualTokens.Warning, VisualTokens.Tone(UiTone.Warning));
        Assert.Equal(VisualTokens.Error, VisualTokens.Tone(UiTone.Error)); Assert.Equal(VisualTokens.Primary, VisualTokens.Tone(UiTone.Primary));
    }
}
