using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Exercises the same drawer state and bound commands used by the renderer.</summary>
public sealed class DrawerSessionTests
{
    private static readonly Guid Plate = new("10000000-0000-0000-0000-000000000001");
    private static readonly SavedPlateSummary Summary = new(Plate, "Outfit", TestDomainValues.Timestamp, 1);
    private static readonly SavedSlotView Slot = new(PlateSlot.Body, "Body", "Coat", true, 100,
        SharedOwnershipState.FreshMatch, "Collected")
    { ItemId = 20, DyeCount = 2, Stain0Id = 3, Stain1Id = 4 };
    private static MainViewState View => MainViewState.Empty with
    { SelectedPlateId = Plate, ActivePlate = Summary, SavedPlates = [Summary], Slots = [Slot], OutfitRevision = 7 };

    /// <summary>Only one drawer is active, and opening a dye retains the exact channel identity.</summary>
    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(1, 4, true)]
    public void OpeningDyeReplacesIconDrawer(byte channel, byte stain, bool special)
    {
        // Arrange
        var session = new DrawerSession();
        session.OpenIcon(Summary);
        DyeOption[] dyes = [new(3, "Red", 0), new(4, "Gold", 0, true)];

        // Act
        var opened = session.OpenDye(View, Slot, channel, dyes);

        // Assert
        Assert.True(opened);
        Assert.True(session.DyeOpen);
        Assert.False(session.IconOpen);
        Assert.Equal(special, session.SpecialDyes);
        Assert.Equal(new MainCommand.SetDye(Plate, PlateSlot.Body, 20, channel, stain), session.DyeTarget);
        Assert.Same(Slot, session.CurrentDyeSlot(View));
    }

    /// <summary>A missing dye definition uses the regular group, without inventing a definition.</summary>
    [Fact]
    public void UnknownDyeStillOpensRegularGroup()
    {
        var session = new DrawerSession { SpecialDyes = true };
        Assert.True(session.OpenDye(View, Slot, 0, []));
        Assert.False(session.SpecialDyes);
    }

    /// <summary>Invalid click targets cannot open or replace a drawer.</summary>
    [Theory]
    [InlineData("plate")]
    [InlineData("empty")]
    [InlineData("channel")]
    public void InvalidDyeClickPreservesExistingDrawer(string invalid)
    {
        var session = new DrawerSession();
        session.OpenIcon(Summary);
        var view = invalid == "plate" ? View with { SelectedPlateId = null } : View;
        var slot = invalid == "empty" ? Slot with { HasItem = false } : Slot;

        var opened = session.OpenDye(view, slot, invalid == "channel" ? (byte)2 : (byte)0, []);

        Assert.False(opened);
        Assert.True(session.IconOpen);
        Assert.False(session.DyeOpen);
        Assert.Null(session.DyeTarget);
    }

    /// <summary>Plate/item/channel changes invalidate the previous target before a command is emitted.</summary>
    [Theory]
    [InlineData("plate")]
    [InlineData("no-plate")]
    [InlineData("slot")]
    [InlineData("item")]
    [InlineData("channel")]
    public void StaleDyeTargetClosesAndRejectsActions(string change)
    {
        var session = new DrawerSession();
        session.OpenDye(View, Slot, 1, []);
        var changed = change switch
        {
            "plate" => View with { SelectedPlateId = Guid.Empty },
            "no-plate" => View with { SelectedPlateId = null },
            "slot" => View with { Slots = [] },
            "item" => View with { Slots = [Slot with { ItemId = 21 }] },
            _ => View with { Slots = [Slot with { DyeCount = 1 }] },
        };

        Assert.Null(session.SelectDye(changed, 10));

        Assert.False(session.DyeOpen);
        Assert.Null(session.ClearBoth(changed));
        Assert.Null(session.ClearAll(changed));
    }

    /// <summary>The three clearing scopes remain distinct and capture the displayed revision.</summary>
    [Fact]
    public void DyeCommandsBindSelectedBothAndWholeOutfitSeparately()
    {
        var session = new DrawerSession();
        session.OpenDye(View, Slot, 1, []);

        var selected = session.SelectDye(View, 0);
        var both = session.ClearBoth(View);
        var all = session.ClearAll(View with { OutfitRevision = 12 });

        Assert.Equal(new MainCommand.SetDye(Plate, PlateSlot.Body, 20, 1, 0), selected);
        Assert.Equal(new MainCommand.ClearItemDyes(Plate, PlateSlot.Body, 20), both);
        Assert.Equal(new MainCommand.ClearOutfitDyes(Plate, 12), all);
        Assert.Equal(new MainCommand.SetDye(Plate, PlateSlot.Body, 20, 1, 9), session.SelectDye(View, 9));
        Assert.Equal((byte)4, session.DyeTarget!.DyeId);
    }

    /// <summary>Icon assignment uses the clicked row, including restoring its automatic icon.</summary>
    [Fact]
    public void IconDrawerReplacesDyeAndBindsRow()
    {
        var session = new DrawerSession();
        session.OpenDye(View, Slot, 0, []);

        session.OpenIcon(Summary);

        Assert.False(session.DyeOpen);
        Assert.True(session.IconOpen);
        Assert.Same(Summary, session.IconTarget);
        Assert.True(session.HasCurrentIconTarget(View));
        Assert.Equal(new MainCommand.AssignIcon(Plate, 100), session.AssignIcon(View, 100));
        Assert.Equal(new MainCommand.AssignIcon(Plate, 0), session.AssignIcon(View, 0));
        Assert.Null(session.AssignIcon(View with { SavedPlates = [] }, 100));
        Assert.False(session.IconOpen);
    }

    /// <summary>Closed or uninitialized drawers cannot issue commands.</summary>
    [Fact]
    public void CloseAndMissingTargetsRejectCommands()
    {
        var session = new DrawerSession { DyeOpen = true, IconOpen = true };
        Assert.Null(session.CurrentDyeSlot(View));
        Assert.False(session.HasCurrentIconTarget(View));
        session.OpenDye(View, Slot, 0, []);
        session.Close();
        Assert.False(session.DyeOpen);
        Assert.False(session.IconOpen);
        Assert.Null(session.SelectDye(View, 0));
        Assert.Null(session.ClearBoth(View));
        Assert.Null(session.ClearAll(View));
        Assert.Null(session.AssignIcon(View, 1));
        session.OpenIcon(Summary);
        session.Close();
        Assert.False(session.IconOpen);
    }

    /// <summary>Palette searches retain ordering and isolate regular and reagent dyes.</summary>
    [Fact]
    public void DyeFilteringMatchesGroupAndCaseInsensitiveName()
    {
        DyeOption[] dyes = [new(1, "Ruby Red", 0), new(2, "Ruby Blue", 0, true), new(3, "Red", 0)];
        Assert.Equal(new byte[] { 1, 3 }, DrawerSession.FilterDyes(dyes, false, "RED").Select(dye => dye.Id));
        Assert.Equal(new byte[] { 2 }, DrawerSession.FilterDyes(dyes, true, "ruby").Select(dye => dye.Id));
        Assert.Empty(DrawerSession.FilterDyes(dyes, false, "missing"));
    }
}
