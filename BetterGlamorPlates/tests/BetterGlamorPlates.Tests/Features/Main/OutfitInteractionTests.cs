using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Disabled controls cannot create commands, and enabled controls capture displayed identity.</summary>
public sealed class OutfitInteractionTests
{
    private static MainViewState Ready => MainViewState.Empty with
    {
        SelectedPlateId = Guid.Empty,
        ActivePlate = new(Guid.Empty, "A", TestDomainValues.Timestamp, 1),
        CanLoad = true,
        HasUnsavedChanges = true,
        OutfitRevision = 8,
        PreviewDyeMode = PlateDyeMode.None,
    };

    /// <summary>Clear, save, reset and load bind the outfit and preview state at click time.</summary>
    [Fact]
    public void EnabledActionsCaptureIdentityAndRevision()
    {
        var view = Ready;
        Assert.Equal(new MainCommand.ClearOutfitItems(Guid.Empty, 8), OutfitInteraction.ClearItems(view, false));
        Assert.Equal(new MainCommand.SaveOutfit(Guid.Empty), OutfitInteraction.Save(view));
        Assert.Equal(new MainCommand.DiscardOutfit(Guid.Empty), OutfitInteraction.Reset(view));
        Assert.Equal(new MainCommand.LoadActive(PlateDyeMode.None, Guid.Empty, 8), OutfitInteraction.Load(view, false));
    }

    /// <summary>Each disabled clear condition independently prevents dispatch.</summary>
    [Theory]
    [InlineData("active")]
    [InlineData("load")]
    [InlineData("queue")]
    [InlineData("selection")]
    public void DisabledClearDoesNotDispatch(string condition)
    {
        var view = condition switch
        {
            "active" => Ready with { ActivePlate = null },
            "load" => Ready with { CanLoad = false },
            "selection" => Ready with { SelectedPlateId = null },
            _ => Ready,
        };
        Assert.Null(OutfitInteraction.ClearItems(view, condition == "queue"));
    }

    /// <summary>Clean or unselected outfits cannot save/reset; queued or invalid loads cannot resubmit.</summary>
    [Fact]
    public void DisabledSaveResetAndLoadDoNotDispatch()
    {
        Assert.Null(OutfitInteraction.Save(Ready with { HasUnsavedChanges = false }));
        Assert.Null(OutfitInteraction.Reset(Ready with { HasUnsavedChanges = false }));
        Assert.Null(OutfitInteraction.Save(Ready with { SelectedPlateId = null }));
        Assert.Null(OutfitInteraction.Reset(Ready with { SelectedPlateId = null }));
        Assert.Null(OutfitInteraction.Load(Ready with { CanLoad = false }, false));
        Assert.Null(OutfitInteraction.Load(Ready, true));
    }

    /// <summary>Right-click clears only populated slots and binds the expected item.</summary>
    [Fact]
    public void RightClickBindsTheVisibleItem()
    {
        var slot = new SavedSlotView(PlateSlot.Body, "Body", "Coat", true, 1, SharedOwnershipState.Unknown, null) { ItemId = 20 };
        Assert.Equal(new MainCommand.ClearSlot(PlateSlot.Body, Guid.Empty, 20), OutfitInteraction.ClearSlot(Ready, slot));
        Assert.Null(OutfitInteraction.ClearSlot(Ready, slot with { HasItem = false }));
    }

    /// <summary>Operation feedback takes precedence over collection attention; queue status takes priority.</summary>
    [Fact]
    public void FeedbackUsesTheHighestPriorityCurrentMessage()
    {
        var view = Ready with { OperationFeedback = "Failed", CollectionNeedsAttention = true, SharedCollectionStatus = "Unavailable" };
        Assert.Equal("Preparing the current plate…", OutfitInteraction.Feedback(view, true));
        Assert.Equal("Failed", OutfitInteraction.Feedback(view, false));
        Assert.Equal("Unavailable", OutfitInteraction.Feedback(view with { OperationFeedback = null }, false));
        Assert.Null(OutfitInteraction.Feedback(Ready, false));
    }
}
