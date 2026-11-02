using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Approved companion interactions bind to exact local identities and preserve native safety.</summary>
public sealed class ApprovedUiTests
{
    /// <summary>Each aligned pair shares the same icon bounds at arbitrary supported scale.</summary>
    [Theory]
    [InlineData(0.6f)]
    [InlineData(0.8f)]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.75f)]
    public void AllTwelveIconsShareSixColumns(float scale)
    {
        var first = MainLayoutPolicy.EquipmentCell(0, 684 * scale, scale);
        for (var index = 0; index < 6; index++)
        {
            var upper = MainLayoutPolicy.EquipmentCell(index, 684 * scale, scale);
            var lower = MainLayoutPolicy.EquipmentCell(index + 6, 684 * scale, scale);
            Assert.Equal(upper.Position.X, lower.Position.X);
            Assert.Equal(upper.Width, lower.Width);
            Assert.Equal(first.IconSize, lower.IconSize);
            Assert.Equal(first.Position.Y, upper.Position.Y);
            Assert.Equal(MathF.Round(142 * scale), lower.Position.Y);
        }
    }

    /// <summary>Footer fallback and exceptional feedback receive dedicated height.</summary>
    [Fact]
    public void FooterReservesSeparateRowsOnlyWhenRequired()
    {
        Assert.Equal(68, MainLayoutPolicy.FooterSize(740, false));
        Assert.Equal(152, MainLayoutPolicy.FooterSize(740, true));
        Assert.Equal(90, MainLayoutPolicy.FooterSize(550, false));
        Assert.Equal(174, MainLayoutPolicy.FooterSize(550, true));
    }

    /// <summary>Clearing another occupied slot preserves selection and never reads or writes native collections.</summary>
    [Fact]
    public void RightClickClearsOnlyBoundSlotAndPersistsBothDyesAsEmpty()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 30, Stain0Id = 7, Stain1Id = 9 };
        var peer = PlateLibrary.Duplicate(plate, DateTime.UnixEpoch, Guid.NewGuid(), "Peer");
        var store = new FakePlateLibraryStore(plate, peer);
        var native = new FakeNativeCollectionValidator();
        var editor = new FakePlateEditor();
        using var controller = Create(store, native, editor);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Head, plate.Id, 30), DateTime.UnixEpoch, Guid.Empty);
        Assert.False(controller.View.Slots[(int)PlateSlot.Head].HasItem);
        Assert.NotNull(plate.Items[(int)PlateSlot.Head]);
        Assert.True(controller.View.HasUnsavedChanges);
        Assert.Equal(0, store.SaveCount);
        controller.Execute(new MainCommand.SelectPlate(peer.Id), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(new MainCommand.RenamePlate(peer.Id, "Changed"), DateTime.UnixEpoch, Guid.Empty);
        Assert.NotNull(store.State.SavedPlates.Single(p => p.Id == plate.Id).Items[(int)PlateSlot.Head]);
        controller.Execute(new MainCommand.SelectPlate(plate.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.True(controller.View.Slots[(int)PlateSlot.Head].HasItem);
        controller.Execute(new MainCommand.DiscardOutfit(plate.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.True(controller.View.Slots[(int)PlateSlot.Head].HasItem);
        Assert.False(controller.View.HasUnsavedChanges);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Head, plate.Id, 30), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(new MainCommand.SaveOutfit(plate.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.Null(store.State.SavedPlates.Single(p => p.Id == plate.Id).Items[(int)PlateSlot.Head]);
        Assert.False(controller.View.HasUnsavedChanges);
        using var reopened = Create(store);
        Assert.False(reopened.View.Slots[(int)PlateSlot.Head].HasItem);
        Assert.Equal(0, native.CaptureCount);
        Assert.Equal(0, editor.ApplyCount);
    }
    /// <summary>Closing resets drafts but preserves explicitly saved changes without native writes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingResetsOnlyUnsavedChanges(bool save)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body), DateTime.UnixEpoch, Guid.Empty);
        if (save) controller.Execute(new MainCommand.SaveOutfit(plate.Id), DateTime.UnixEpoch, Guid.Empty);
        var saves = store.SaveCount;
        controller.Execute(new MainCommand.CloseEditor(), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(!save, controller.View.Slots[(int)PlateSlot.Body].HasItem);
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(saves, store.SaveCount);
        controller.Execute(new MainCommand.CloseEditor(), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(!save, controller.View.Slots[(int)PlateSlot.Body].HasItem);
    }

    /// <summary>Clearing an outfit empties only its current draft and Reset restores it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearItemsIsReversibleAndRevisionBound(bool stale)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 30, Stain0Id = 7 };
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        using var controller = Create(store, editor: editor);
        var revision = controller.View.OutfitRevision;
        controller.Execute(new MainCommand.ClearOutfitItems(plate.Id, stale ? revision - 1 : revision), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(stale ? 2 : 0, controller.View.Slots.Count(slot => slot.HasItem));
        Assert.NotNull(plate.Items[(int)PlateSlot.Body]);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
        controller.Execute(new MainCommand.DiscardOutfit(plate.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(2, controller.View.Slots.Count(slot => slot.HasItem));
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Head].Stain0Id);
    }

    /// <summary>Clear Items sends twelve native clear steps, even when the local draft is already empty.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearItemsAppliesEmptyNativePlate(bool alreadyEmpty)
    {
        var plate = TestDomainValues.Plate("Local", alreadyEmpty ? 0u : 20u);
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        var source = new FakeNativeCollectionValidator
        { Snapshot = new([], true, true, DateTime.UnixEpoch) };
        using var controller = Create(store, source, editor);
        controller.Execute(new MainCommand.ClearOutfitItems(plate.Id, controller.View.OutfitRevision), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(12, editor.LastPlan!.Steps.Count);
        Assert.All(editor.LastPlan.Steps, step =>
        {
            Assert.Null(step.SavedItem);
            Assert.Null(step.Source);
            Assert.False(step.IsSkipped);
        });
        Assert.Equal(0, store.SaveCount);
        Assert.All(controller.View.Slots, slot => Assert.False(slot.HasItem));
    }

    /// <summary>Replacement items, switched outfits and invalid slots invalidate an old clear gesture.</summary>
    [Theory]
    [InlineData(3, 21u, false)]
    [InlineData(3, 20u, true)]
    [InlineData(-1, 20u, false)]
    [InlineData(12, 20u, false)]
    public void RejectsStaleOrInvalidClear(int slot, uint expected, bool wrongPlate)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        controller.Execute(new MainCommand.ClearSlot((PlateSlot)slot, wrongPlate ? Guid.Empty : plate.Id, expected), DateTime.UnixEpoch, Guid.Empty);
        Assert.NotNull(plate.Items[(int)PlateSlot.Body]);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Row actions use their own identity, duplicate icons/dyes and preserve selection on another row's deletion.</summary>
    [Fact]
    public void RowActionsAreIndependentFromActiveSelection()
    {
        var active = TestDomainValues.Plate("Active", 20);
        var other = PlateLibrary.Duplicate(active, DateTime.UnixEpoch, Guid.NewGuid(), "Other");
        var store = new FakePlateLibraryStore(active, other);
        using var controller = Create(store);
        controller.Execute(new MainCommand.AssignIcon(other.Id, 42), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(new MainCommand.RenamePlate(other.Id, " Renamed "), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal("Active", active.Name);
        Assert.Equal("Renamed", other.Name);
        Assert.Equal(active.Id, controller.View.SelectedPlateId);
        var copyId = Guid.NewGuid();
        controller.Execute(new MainCommand.DuplicatePlate(other.Id), DateTime.UnixEpoch, copyId);
        var copy = store.State.SavedPlates.Single(plate => plate.Id == copyId);
        Assert.Equal(42u, copy.IconId);
        Assert.Equal(other.Items[(int)PlateSlot.Body]!.ItemId, copy.Items[(int)PlateSlot.Body]!.ItemId);
        Assert.NotSame(other.Items[(int)PlateSlot.Body], copy.Items[(int)PlateSlot.Body]);
        controller.Execute(new MainCommand.ArmOrDelete(other.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.Contains(other, store.State.SavedPlates);
        Assert.Equal(other.Id, controller.View.DeleteArmedId);
        controller.Execute(new MainCommand.ArmOrDelete(other.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.DoesNotContain(other, store.State.SavedPlates);
        Assert.Equal(copyId, controller.View.SelectedPlateId);
    }

    /// <summary>Missing/invalid icons retain old outfit contents and get a stable item fallback.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void LegacyAndInvalidIconsDoNotLoseOutfits(uint icon)
    {
        var plate = TestDomainValues.Plate("Old", 20);
        plate.IconId = icon;
        var store = new FakePlateLibraryStore(plate);
        using var controller = Create(store);
        Assert.Equal(plate.Id, controller.View.SelectedPlateId);
        Assert.Equal(120u, controller.View.ActivePlate!.IconId);
        Assert.Equal(20u, plate.Items[(int)PlateSlot.Body]!.ItemId);
        controller.Execute(new MainCommand.AssignIcon(plate.Id, uint.MaxValue), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(0u, plate.IconId);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>The visible dye mode is captured at click and survives later preview changes.</summary>
    [Theory]
    [InlineData(0, 1, 7)]
    [InlineData(1, 0, 0)]
    public void LoadRetainsClickedMode(int clickedValue, int laterValue, byte expected)
    {
        var clicked = (PlateDyeMode)clickedValue;
        var later = (PlateDyeMode)laterValue;
        var plate = TestDomainValues.Plate("Dyed", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        plate.Items[(int)PlateSlot.Body]!.Stain1Id = 9;
        var native = new FakeNativeCollectionValidator { Snapshot = new([new(20, CollectionSource.Dresser, 8, 3, 4)], true, true, DateTime.UnixEpoch) };
        var editor = new FakePlateEditor();
        using var controller = Create(new FakePlateLibraryStore(plate), native, editor);
        controller.Execute(new MainCommand.PreviewActive(clicked), DateTime.UnixEpoch, Guid.Empty);
        var view = controller.View;
        var request = new MainCommand.LoadActive(view.PreviewDyeMode, view.SelectedPlateId, view.OutfitRevision);
        controller.Execute(new MainCommand.PreviewActive(later), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(request, DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(expected, editor.LastPlan!.Steps[(int)PlateSlot.Body].SavedItem!.Stain0Id);
        Assert.Equal(7, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(9, plate.Items[(int)PlateSlot.Body]!.Stain1Id);
        Assert.Equal(3, editor.LastPlan.Steps[(int)PlateSlot.Body].Source!.Stain0Id);
    }

    /// <summary>A queued load cannot accept an intervening local edit, including edits with the same clock timestamp.</summary>
    [Fact]
    public void LoadRejectsChangedOutfitBeforeNativeReads()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var native = new FakeNativeCollectionValidator();
        var editor = new FakePlateEditor();
        using var controller = Create(new FakePlateLibraryStore(plate), native, editor);
        var request = new MainCommand.LoadActive(controller.View.PreviewDyeMode, plate.Id, controller.View.OutfitRevision);
        controller.Execute(new MainCommand.ClearSlot(PlateSlot.Body, plate.Id, 20), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(request, DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(0, native.CaptureCount);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Contains("changed before loading", controller.View.OperationFeedback);
    }

    /// <summary>Channel support follows supplied item facts for weapons, accessories, empty and invalid data.</summary>
    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(7, 0, 0)]
    [InlineData(10, 2, 2)]
    [InlineData(3, 3, 0)]
    public void EverySlotUsesItemChannelFacts(int slot, byte channels, byte widgets)
    {
        var plate = PlateLibrary.Create("Facts", DateTime.UnixEpoch, Guid.NewGuid());
        plate.Items[slot] = new() { ItemId = 20 };
        var facts = Facts();
        facts.DyeCounts[20] = channels;
        var store = new FakePlateLibraryStore(plate);
        using var controller = new MainController(store, new FakeNativeCollectionValidator(), new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), facts, dyes: [new(7, "Test", 0)]);
        Assert.Equal(widgets, controller.View.Slots[slot].DyeCount);
        Assert.All(controller.View.Slots.Where(value => !value.HasItem), value => Assert.Equal(0, value.DyeCount));
        controller.Execute(new MainCommand.SetDye(plate.Id, (PlateSlot)slot, 20, 0, 7), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(widgets > 0, controller.View.HasUnsavedChanges);
    }

    /// <summary>Preview recovery clears its own warning without hiding a retained native result.</summary>
    [Fact]
    public void PreviewFeedbackRecoversAndLoadResultsSurviveBrowsing()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        using var controller = Create(new FakePlateLibraryStore(plate));
        controller.ReportPreviewResult(false);
        Assert.NotNull(controller.View.OperationFeedback);
        controller.ReportPreviewResult(true);
        Assert.Null(controller.View.OperationFeedback);
        controller.Execute(new MainCommand.LoadActive(), DateTime.UnixEpoch, Guid.Empty);
        var result = controller.View.OperationFeedback;
        controller.Execute(new MainCommand.SetFilter("Different"), DateTime.UnixEpoch, Guid.Empty);
        controller.ReportPreviewResult(true);
        Assert.Equal(result, controller.View.OperationFeedback);
    }

    /// <summary>Creating a plate clears the library query so the new row remains visible.</summary>
    [Fact]
    public void NewPlateResetsLibrarySearch()
    {
        using var controller = Create(new FakePlateLibraryStore(TestDomainValues.Plate("Local", 20)));
        controller.Execute(new MainCommand.SetPlateSearch("absent"), DateTime.UnixEpoch, Guid.Empty);
        Assert.Empty(controller.View.SavedPlates);
        controller.Execute(new MainCommand.CreatePlate(), DateTime.UnixEpoch, Guid.NewGuid());
        Assert.Equal(string.Empty, controller.View.PlateSearch);
        Assert.Equal(2, controller.View.SavedPlates.Count);
    }

    /// <summary>Duplicated rows can be armed and deleted without a paired disarm race; stale confirms are ignored.</summary>
    [Fact]
    public void DuplicateThenDeleteUsesOneExplicitConfirmationTarget()
    {
        var original = TestDomainValues.Plate("Original", 20);
        var store = new FakePlateLibraryStore(original);
        using var controller = Create(store);
        var copyId = Guid.NewGuid();
        controller.Execute(new MainCommand.DuplicatePlate(original.Id), DateTime.UnixEpoch, copyId);
        controller.Execute(new MainCommand.BeginDelete(copyId), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(copyId, controller.View.DeleteArmedId);
        controller.Execute(new MainCommand.ConfirmDelete(original.Id), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(2, store.State.SavedPlates.Count);
        controller.Execute(new MainCommand.ConfirmDelete(copyId), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(original.Id, Assert.Single(store.State.SavedPlates).Id);
        Assert.Equal(original.Id, controller.View.SelectedPlateId);
    }

    private static MainController Create(FakePlateLibraryStore store, FakeNativeCollectionValidator? native = null, FakePlateEditor? editor = null)
        => new(store, native ?? new(), new FakeSharedCollectionSnapshotSource(), editor ?? new(), Facts(), isGameIcon: id => id == 42);

    private static FakeItemFacts Facts()
    {
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        facts.Icons[20] = 120;
        facts.DyeCounts[20] = 2;
        return facts;
    }
}
