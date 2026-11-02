using BetterGlamorPlates.Bootstrap;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies editor lifecycle tests.</summary>
public sealed class EditorLifecycleTests
{
    /// <summary>Verifies closed windows reset drafts and suppress preview.</summary>
    [Theory]
    [InlineData(false, false, false, 1, 2, false)]
    [InlineData(true, false, false, 1, 1, false)]
    [InlineData(false, true, false, 1, 1, true)]
    [InlineData(true, true, true, 0, 1, true)]
    [InlineData(true, true, false, 0, 0, true)]
    public void ClosedWindowsResetDraftsAndSuppressPreview(bool window, bool editor, bool dyes, int resets, int cancellations, bool active)
    {
        var calls = new List<string>();
        var lifecycle = new EditorLifecycle(() => calls.Add("reset"), () => calls.Add("cancel"),
            () => calls.Add("show"), () => calls.Add("hide"));

        var result = lifecycle.Update(window, editor, dyes);

        Assert.Equal(active, result);
        Assert.Equal(resets, calls.Count(value => value == "reset"));
        Assert.Equal(cancellations, calls.Count(value => value == "cancel"));
        Assert.Equal(active ? "show" : "hide", calls[^1]);
    }

    /// <summary>Verifies opening is reported once until native editor closes.</summary>
    [Fact]
    public void OpeningIsReportedOnceUntilNativeEditorCloses()
    {
        var shown = 0;
        var hidden = 0;
        var lifecycle = new EditorLifecycle(() => { }, () => { }, () => shown++, () => hidden++);
        lifecycle.Update(true, true, false);
        lifecycle.Update(true, true, false);
        lifecycle.Update(false, true, false);
        Assert.Equal(1, shown);
        lifecycle.Update(false, false, false);
        lifecycle.Update(false, true, false);
        Assert.Equal(2, shown);
        Assert.Equal(1, hidden);
    }
}
