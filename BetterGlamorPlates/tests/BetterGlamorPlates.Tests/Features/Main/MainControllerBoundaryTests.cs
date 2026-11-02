using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Delayed or stale UI requests cannot create missing targets or modify another outfit.</summary>
public sealed class MainControllerBoundaryTests
{
    /// <summary>The diagnostics source reports provider failure and its subsequent reset.</summary>
    [Fact]
    public void ProviderFailureIsClearedWithCollectionContext()
    {
        using var controller = Create(new());

        controller.UpdateSharedCollectionData(CollectionDataParseResult.Reject(CollectionDataFailure.ProviderUnavailable));
        Assert.Equal(CollectionDataFailure.ProviderUnavailable, controller.ProviderFailure);
        controller.ClearSharedCollectionData();

        Assert.Equal(CollectionDataFailure.None, controller.ProviderFailure);
    }

    /// <summary>Confirmation for a nonexistent plate never deletes an existing plate.</summary>
    [Fact]
    public void StaleDeleteConfirmationDoesNotDelete()
    {
        var plate = TestDomainValues.Plate("Retain", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        var missing = Guid.NewGuid();

        controller.Execute(new MainCommand.BeginDelete(missing), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.ConfirmDelete(missing), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(plate.Id, Assert.Single(store.State.SavedPlates).Id);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>An icon request cannot mutate a deleted target.</summary>
    [Fact]
    public void IconRequestForMissingPlateIsRejected()
    {
        var store = new FakePlateLibraryStore();
        using var controller = Create(store);

        controller.Execute(new MainCommand.AssignIcon(Guid.NewGuid(), 10), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Empty(store.State.SavedPlates);
        Assert.Equal(0, store.SaveCount);
        Assert.Contains("unavailable", controller.View.OperationFeedback);
    }

    /// <summary>Clear commands without an active outfit cannot reach native writes.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyLibraryRejectsClearCommands(bool items)
    {
        var editor = new FakePlateEditor();
        var store = new FakePlateLibraryStore();
        using var controller = Create(store, editor);
        MainCommand command = items
            ? new MainCommand.ClearOutfitItems(Guid.NewGuid(), controller.View.OutfitRevision)
            : new MainCommand.ClearOutfitDyes(Guid.NewGuid(), controller.View.OutfitRevision);

        controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(0, store.SaveCount);
        Assert.False(controller.View.HasUnsavedChanges);
    }

    /// <summary>Out-of-range slot input preserves the user's selected slot.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void InvalidSlotDoesNotChangeSelection(int slot)
    {
        using var controller = Create(new());
        var original = controller.View.SelectedSlot;

        controller.Execute(new MainCommand.SelectSlot((PlateSlot)slot), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(original, controller.View.SelectedSlot);
    }

    /// <summary>Deleting the final plate removes active selection instead of retaining its draft.</summary>
    [Fact]
    public void DeleteFinalPlateClearsSelection()
    {
        var plate = TestDomainValues.Plate("Last", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);

        controller.Execute(new MainCommand.DeletePlate(plate.Id), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Null(controller.View.SelectedPlateId);
        Assert.Null(controller.View.ActivePlate);
        Assert.Empty(store.State.SavedPlates);
    }

    /// <summary>A load for an outfit which no longer exists cannot act on a native plate.</summary>
    [Fact]
    public void BoundLoadWithoutSelectionIsRejected()
    {
        var editor = new FakePlateEditor();
        using var controller = Create(new(), editor);

        controller.Execute(new MainCommand.LoadActive(PlateDyeMode.Saved, Guid.NewGuid(),
            controller.View.OutfitRevision), TestDomainValues.Timestamp, Guid.Empty);

        Assert.Equal(0, editor.ApplyCount);
        Assert.Contains("No slots were changed", controller.View.OperationFeedback);
    }

    private static MainController Create(FakePlateLibraryStore store, FakePlateEditor? editor = null)
        => new(store, new FakeNativeCollectionValidator(), new FakeSharedCollectionSnapshotSource(),
            editor ?? new FakePlateEditor(), new FakeItemFacts());
}
