using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Exercises UI command factories together with real draft and persistence policy.</summary>
public sealed class OutfitWorkflowTests
{
    /// <summary>Preview toggles do not disconnect the drawer, and clearing scopes remain distinct.</summary>
    [Theory]
    [InlineData("selected", 7, 0, 7, 9)]
    [InlineData("both", 0, 0, 7, 9)]
    [InlineData("all", 0, 0, 0, 0)]
    public void DrawerActionsAfterPreviewTogglePersistOnlyOnSave(string scope, byte body0, byte body1, byte head0, byte head1)
    {
        // Arrange
        var plate = TestDomainValues.Plate("Outfit", 20);
        plate.Items[(int)PlateSlot.Body] = new() { ItemId = 20, Stain0Id = 7, Stain1Id = 9 };
        plate.Items[(int)PlateSlot.Head] = new() { ItemId = 30, Stain0Id = 7, Stain1Id = 9 };
        var store = new FakePlateLibraryStore(plate);
        var editor = new FakePlateEditor();
        var native = new FakeNativeCollectionValidator();
        var facts = new FakeItemFacts();
        foreach (var id in new uint[] { 20, 30 })
        {
            facts.Masks[id] = TestDomainValues.AllSlots;
            facts.DyeCounts[id] = 2;
        }
        DyeOption[] dyes = [new(7, "Red", 0), new(9, "Blue", 0)];
        using var controller = new MainController(store, native, new FakeSharedCollectionSnapshotSource(), editor, facts, dyes: dyes);
        var drawer = new DrawerSession();
        Assert.True(drawer.OpenDye(controller.View, controller.View.Slots[(int)PlateSlot.Body], 1, dyes));

        // Act
        Execute(controller, new MainCommand.PreviewActive(PlateDyeMode.None));
        Execute(controller, new MainCommand.PreviewActive(PlateDyeMode.Saved));
        Execute(controller, scope switch
        {
            "selected" => drawer.SelectDye(controller.View, 0),
            "both" => drawer.ClearBoth(controller.View),
            _ => drawer.ClearAll(controller.View),
        });

        // Assert: real draft changes, original storage and native plate remain untouched.
        Assert.Equal(body0, controller.View.Slots[(int)PlateSlot.Body].Stain0Id);
        Assert.Equal(body1, controller.View.Slots[(int)PlateSlot.Body].Stain1Id);
        Assert.Equal(head0, controller.View.Slots[(int)PlateSlot.Head].Stain0Id);
        Assert.Equal(head1, controller.View.Slots[(int)PlateSlot.Head].Stain1Id);
        Assert.Equal(9, plate.Items[(int)PlateSlot.Body]!.Stain1Id);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(0, native.CaptureCount);

        Execute(controller, OutfitInteraction.Save(controller.View));
        Assert.Equal(1, store.SaveCount);
        Assert.False(controller.View.HasUnsavedChanges);
        var saved = Assert.Single(store.State.SavedPlates);
        Assert.Equal(body0, saved.Items[(int)PlateSlot.Body]!.Stain0Id);
        Assert.Equal(body1, saved.Items[(int)PlateSlot.Body]!.Stain1Id);
        Assert.Equal(head0, saved.Items[(int)PlateSlot.Head]!.Stain0Id);
        Assert.Equal(head1, saved.Items[(int)PlateSlot.Head]!.Stain1Id);
    }

    /// <summary>Right-click clears remain drafts until saved across reset, close and plate switches.</summary>
    [Theory]
    [InlineData("reset")]
    [InlineData("close")]
    [InlineData("switch")]
    public void LeavingAnUnsavedClearRestoresTheStoredItem(string exit)
    {
        // Arrange
        var plate = TestDomainValues.Plate("Original", 20);
        var other = TestDomainValues.Plate("Other", 30);
        other.Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var store = new FakePlateLibraryStore(plate, other);
        var editor = new FakePlateEditor();
        using var controller = new MainController(store, new FakeNativeCollectionValidator(), new FakeSharedCollectionSnapshotSource(), editor, new FakeItemFacts());

        // Act
        Execute(controller, OutfitInteraction.ClearSlot(controller.View, controller.View.Slots[(int)PlateSlot.Body]));
        Assert.True(controller.View.HasUnsavedChanges);
        Assert.False(controller.View.Slots[(int)PlateSlot.Body].HasItem);
        Execute(controller, exit switch
        {
            "reset" => OutfitInteraction.Reset(controller.View),
            "close" => new MainCommand.CloseEditor(),
            _ => new MainCommand.SelectPlate(other.Id),
        });
        if (exit == "switch") Execute(controller, new MainCommand.SelectPlate(plate.Id));

        // Assert
        Assert.False(controller.View.HasUnsavedChanges);
        Assert.Equal(20u, controller.View.Slots[(int)PlateSlot.Body].ItemId);
        Assert.Equal(20u, plate.Items[(int)PlateSlot.Body]!.ItemId);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Equal(exit == "switch" ? 2 : 0, store.SaveCount);
    }

    private static void Execute(MainController controller, MainCommand? command)
    {
        Assert.NotNull(command);
        controller.Execute(command, TestDomainValues.Timestamp, Guid.Empty);
    }
}
