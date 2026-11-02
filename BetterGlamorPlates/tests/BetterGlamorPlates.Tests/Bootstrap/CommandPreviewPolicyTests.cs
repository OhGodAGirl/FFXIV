using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies command preview policy tests.</summary>
public sealed class CommandPreviewPolicyTests
{
    private static readonly Guid Plate = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>A null command has no preview intent unless selection itself changed.</summary>
    [Fact]
    public void NullCommandCannotCreatePreviewIntent()
    {
        Assert.False(CommandPreviewPolicy.ShouldPreview(null!, true, false, Plate, Plate));
        Assert.False(CommandPreviewPolicy.ShouldPreview(new MainCommand.RefreshCollection(), true, false, null, null));
        Assert.True(CommandPreviewPolicy.ShouldPreview(new MainCommand.RefreshCollection(), true, false, Plate, null));
    }

    /// <summary>Verifies command cases.</summary>
    public static TheoryData<object, bool, bool> Commands => new()
    {
        { new MainCommand.AddItem(PlateSlot.Body, null!), true, true },
        { new MainCommand.SelectSlot(PlateSlot.Body), true, true },
        { new MainCommand.SelectPlate(Plate), true, true },
        { new MainCommand.ClearSlot(PlateSlot.Body), true, true },
        { new MainCommand.PreviewActive(), true, true },
        { new MainCommand.CaptureCurrentPlate(), true, true },
        { new MainCommand.CreatePlate(), true, true },
        { new MainCommand.DuplicatePlate(Plate), true, true },
        { new MainCommand.DiscardOutfit(Plate), true, true },
        { new MainCommand.UseStoredDyes(), true, false },
        { new MainCommand.SetDye(Plate, PlateSlot.Body, 1, 0, 2), true, false },
        { new MainCommand.ClearOutfitDyes(Plate, 1), true, false },
        { new MainCommand.ClearItemDyes(Plate, PlateSlot.Body, 1), true, false },
        { new MainCommand.RestoreSlotDyes(Plate, PlateSlot.Body, 1), true, false },
        { new MainCommand.LoadActive(), false, true },
        { new MainCommand.ClearOutfitItems(Plate, 1), false, true },
        { new MainCommand.SaveOutfit(Plate), false, true },
        { new MainCommand.RefreshCollection(), false, true },
        { new MainCommand.CloseEditor(), false, true },
        { new MainCommand.SetFilter("test"), false, true },
        { new MainCommand.SetPlateSearch("test"), false, true },
        { new MainCommand.RenameActive("test"), false, true },
        { new MainCommand.RenamePlate(Plate, "test"), false, true },
        { new MainCommand.DuplicateActive(), false, true },
        { new MainCommand.AssignIcon(Plate, 1), false, true },
        { new MainCommand.BeginDelete(Plate), false, true },
        { new MainCommand.ConfirmDelete(Plate), false, true },
        { new MainCommand.DeletePlate(Plate), false, true },
        { new MainCommand.ArmOrDelete(Plate), false, true },
        { new MainCommand.DisarmDelete(), false, true },
        { new MainCommand.ToggleCollectionSource(CollectionSource.Dresser), false, true },
    };

    /// <summary>Verifies command preview intent is explicit and native windows always gate it.</summary>
    [Theory]
    [MemberData(nameof(Commands))]
    public void CommandPreviewIntentIsExplicitAndNativeWindowsAlwaysGateIt(object value, bool shouldPreview, bool report)
    {
        var command = (MainCommand)value;

        Assert.Equal(shouldPreview, CommandPreviewPolicy.ShouldPreview(command, true, false, Plate, Plate));
        Assert.Equal(report, CommandPreviewPolicy.ReportSuccess(command));
        Assert.False(CommandPreviewPolicy.ShouldPreview(command, false, false, Plate, null));
        Assert.False(CommandPreviewPolicy.ShouldPreview(command, true, true, Plate, null));
        Assert.True(CommandPreviewPolicy.ShouldPreview(command, true, false, null, Plate));
    }
}

