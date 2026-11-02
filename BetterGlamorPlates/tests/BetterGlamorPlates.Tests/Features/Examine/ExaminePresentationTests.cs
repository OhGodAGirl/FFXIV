using System.Numerics;
using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Examine;

/// <summary>The native save control is shown and enabled only for appropriate copied facts.</summary>
public sealed class ExaminePresentationTests
{
    private static readonly ExamineView View = new(1, "Player", new(0, 0, 620, 960));
    private static readonly WindowBounds Viewport = new(0, 0, 1920, 1080);

    /// <summary>Covered, absent and offscreen windows never receive an overlay position.</summary>
    [Fact]
    public void PositionRequiresVisibleUncoveredBounds()
    {
        Assert.NotNull(ExaminePresentation.Position(View, Viewport));
        Assert.Null(ExaminePresentation.Position(null, Viewport));
        Assert.Null(ExaminePresentation.Position(View with { OverlayOccluded = true }, Viewport));
        Assert.Null(ExaminePresentation.Position(View with { Bounds = new(-2000, 0, 620, 960) }, Viewport));
    }

    /// <summary>Saving prevents duplicate submissions; unavailable facts prevent new submissions.</summary>
    [Fact]
    public void SavingAndUnavailableFactsDisableAction()
    {
        Assert.True(ExaminePresentation.CanSave(View, false));
        Assert.False(ExaminePresentation.CanSave(View, true));
        Assert.False(ExaminePresentation.CanSave(View with { UnavailableReason = "Loading" }, false));
    }

    /// <summary>Status presentation keeps busy, unavailable and completed messages distinct.</summary>
    [Fact]
    public void StatusAndSavedGlyphUseExactState()
    {
        Assert.Equal("Saving outfit...", ExaminePresentation.Message(View with { UnavailableReason = "Loading" }, true, "Saved A"));
        Assert.Equal("Loading", ExaminePresentation.Message(View with { UnavailableReason = "Loading" }, false, "Saved A"));
        Assert.Equal("Saved A", ExaminePresentation.Message(View, false, "Saved A"));
        Assert.True(ExaminePresentation.IsSaved("Saved A"));
        Assert.False(ExaminePresentation.IsSaved("saved A"));
        Assert.False(ExaminePresentation.IsSaved("Saved"));
        Assert.False(ExaminePresentation.IsSaved("Not Saved A"));
    }
}
