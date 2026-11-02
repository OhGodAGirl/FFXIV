using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Local dye choices persist independently of native plate changes or dye consumption.</summary>
public sealed class LocalDyeTests
{
    /// <summary>Preview mode is reversible session state with no local save, inventory read or native load.</summary>
    [Fact]
    public void PreviewModesDoNotChangeSavedDyesOrLoadItems()
    {
        var plate = TestDomainValues.Plate("Saved", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        plate.Items[(int)PlateSlot.Body]!.Stain1Id = 9;
        var store = new FakePlateLibraryStore(plate);
        var native = new FakeNativeCollectionValidator();
        var editor = new FakePlateEditor();
        using var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, Facts(2));
        controller.Execute(new MainCommand.PreviewActive(PlateDyeMode.None), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(PlateDyeMode.None, controller.View.PreviewDyeMode);
        controller.ReportPreviewResult(true);
        Assert.Contains("without dyes", controller.View.Status);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        controller.Execute(new MainCommand.PreviewActive(PlateDyeMode.Saved), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(PlateDyeMode.Saved, controller.View.PreviewDyeMode);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, native.CaptureCount);
        Assert.Equal(0, editor.ApplyCount);
    }

    private static readonly DyeOption[] Palette = [new(7, "Test Red", 0xff0000), new(9, "Test Blue", 0x0000ff)];

    /// <summary>Each channel changes independently and survives reopening the local library.</summary>
    [Fact]
    public void RecoloursAndRemovesOnlyRequestedChannelWithoutNativeAccess()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var store = new FakePlateLibraryStore(plate);
        var native = new FakeNativeCollectionValidator();
        var editor = new FakePlateEditor();
        var facts = Facts(2);
        using (var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts, dyes: Palette))
        {
            var command = new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 7);
            controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);
            controller.Execute(command with { Channel = 1, DyeId = 9 }, TestDomainValues.Timestamp, Guid.Empty);
            Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
            Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
            controller.Execute(command with { DyeId = 0 }, TestDomainValues.Timestamp, Guid.Empty);
            Assert.Equal(0, store.SaveCount);
            controller.Execute(new MainCommand.SaveOutfit(plate.Id), TestDomainValues.Timestamp, Guid.Empty);
        }
        using var reopened = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts, dyes: Palette);
        var slot = reopened.View.Slots[(int)PlateSlot.Body];
        Assert.Equal(0, slot.Stain0Id);
        Assert.Equal(9, slot.Stain1Id);
        Assert.Equal(2, slot.DyeCount);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(0, native.CaptureCount);
    }

    /// <summary>Rejects unsupported channels, unknown colours and stale item bindings.</summary>
    [Theory]
    [InlineData(0, 0, 7, 20)]
    [InlineData(1, 1, 7, 20)]
    [InlineData(2, 2, 7, 20)]
    [InlineData(2, 0, 254, 20)]
    [InlineData(2, 0, 7, 21)]
    public void RejectsInvalidDyeRequests(byte channels, byte channel, byte dye, uint expectedItem)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var store = new FakePlateLibraryStore(plate);
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), Facts(channels), dyes: Palette);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, expectedItem, channel, dye), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(0, plate.Items[(int)PlateSlot.Body]!.Stain1Id);
    }

    /// <summary>A picker opened for one plate cannot recolour a newly selected plate.</summary>
    [Fact]
    public void RejectsPickerAfterSwitchingPlate()
    {
        var plate = TestDomainValues.Plate("Original", 20);
        var next = TestDomainValues.Plate("Next", 20);
        next.Id = Guid.NewGuid();
        var store = new FakePlateLibraryStore(plate, next);
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), Facts(2), dyes: Palette);
        controller.Execute(new MainCommand.SelectPlate(next.Id), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 7), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(0, next.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(0, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
    }

    /// <summary>Stored-dye restoration changes the selected item only, without applying a native plate.</summary>
    [Fact]
    public void RestoresStoredDyesForOneItem()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Head] = new SavedPlateItem { ItemId = 30, Stain0Id = 4, Stain1Id = 5 };
        var native = new FakeNativeCollectionValidator
        {
            Snapshot = new([new(20, CollectionSource.Dresser, 8, 7, 9)], true, true, TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        using var controller = new MainController(new FakePlateLibraryStore(plate), native,
            new FakeSharedCollectionSnapshotSource(), editor, Facts(2), dyes: Palette);
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(4, plate.Items[(int)PlateSlot.Head]!.Stain0Id);
        Assert.Equal(5, plate.Items[(int)PlateSlot.Head]!.Stain1Id);
        Assert.Equal(1, native.CaptureCount);
        Assert.Equal(0, editor.ApplyCount);
    }

    /// <summary>Load modes preserve local colours and carry separate source and desired dyes.</summary>
    [Fact]
    public void LoadWithAndWithoutDyesPreservesLocalColours()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        var native = new FakeNativeCollectionValidator
        {
            Snapshot = new([new(20, CollectionSource.Dresser, 8, 0, 0)], true, true, TestDomainValues.Timestamp),
        };
        var editor = new FakePlateEditor();
        using var controller = new MainController(new FakePlateLibraryStore(plate), native,
            new FakeSharedCollectionSnapshotSource(), editor, Facts(2), dyes: Palette);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 7), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.LoadActive(), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(1, editor.ApplyCount);
        Assert.Equal(7, editor.LastPlan!.Steps[(int)PlateSlot.Body].SavedItem!.Stain0Id);
        Assert.Equal(0, editor.LastPlan.Steps[(int)PlateSlot.Body].Source!.Stain0Id);
        controller.Execute(new MainCommand.LoadActive(PlateDyeMode.None), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(2, editor.ApplyCount);
        Assert.Equal(0, editor.LastPlan!.Steps[(int)PlateSlot.Body].SavedItem!.Stain0Id);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
    }

    /// <summary>Remove and restore leave Undyed preview so their actual draft colours are visible.</summary>
    [Fact]
    public void RemoveAndRestoreMakeDraftColoursVisible()
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        plate.Items[(int)PlateSlot.Body]!.Stain1Id = 9;
        var source = new FakeNativeCollectionValidator
        { Snapshot = new([new(20, CollectionSource.Dresser, 8, 7, 9)], true, true, TestDomainValues.Timestamp) };
        var store = new FakePlateLibraryStore(plate);
        using var controller = new MainController(store, source,
            new FakeSharedCollectionSnapshotSource(), new FakePlateEditor(), Facts(2), dyes: Palette);
        controller.Execute(new MainCommand.PreviewActive(PlateDyeMode.None), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.SetDye(plate.Id, PlateSlot.Body, 20, 0, 0), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(PlateDyeMode.Saved, controller.View.PreviewDyeMode);
        controller.Execute(new MainCommand.PreviewActive(PlateDyeMode.None), TestDomainValues.Timestamp, Guid.Empty);
        controller.Execute(new MainCommand.RestoreSlotDyes(plate.Id, PlateSlot.Body, 20), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(7, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(9, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(PlateDyeMode.Saved, controller.View.PreviewDyeMode);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Clearing item dyes targets both channels of the selected item without persisting or native writes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearItemDyesIsBoundToExactItem(bool stale)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        plate.Items[(int)PlateSlot.Body]!.Stain1Id = 9;
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), editor, Facts(2), dyes: Palette);
        controller.Execute(new MainCommand.ClearItemDyes(plate.Id, PlateSlot.Body, stale ? 21u : 20u), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(stale ? 7 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(stale ? 9 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(7, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(9, plate.Items[(int)PlateSlot.Body]!.Stain1Id);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
    }

    /// <summary>Whole-outfit clearing stays local and rejects obsolete draft revisions.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAllDyesUsesBoundDraft(bool stale)
    {
        var plate = TestDomainValues.Plate("Local", 20);
        plate.Items[(int)PlateSlot.Body]!.Stain0Id = 7;
        plate.Items[(int)PlateSlot.Body]!.Stain1Id = 9;
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 30, Stain0Id = 4, Stain1Id = 5 };
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        using var controller = new MainController(store, new FakeNativeCollectionValidator(),
            new FakeSharedCollectionSnapshotSource(), editor, Facts(2), dyes: Palette);
        var revision = controller.View.OutfitRevision;
        controller.Execute(new MainCommand.ClearOutfitDyes(plate.Id, stale ? revision - 1 : revision), TestDomainValues.Timestamp, Guid.Empty);
        Assert.Equal(stale ? 7 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(stale ? 9 : 0, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(stale ? 4 : 0, controller.View.Slots[(int)PlateSlot.Head].Stain0Id);
        Assert.Equal(stale ? 5 : 0, controller.View.Slots[(int)PlateSlot.Head].Stain1Id);
        Assert.Equal(7, plate.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
    }

    private static FakeItemFacts Facts(byte channels)
    {
        var facts = new FakeItemFacts();
        facts.Masks[20] = TestDomainValues.AllSlots;
        facts.DyeCounts[20] = channels;
        return facts;
    }
}
