using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.Main;

/// <summary>Verifies the baseline interactions preserve local intent and action-time boundaries.</summary>
public sealed class BaselineControllerTests
{
    /// <summary>Accessory selection survives a trip to another equipment category.</summary>
    [Fact]
    public void RemembersIndependentRingAndRejectsInvalidSlot()
    {
        var controller = Create(new FakePlateEditor(), new FakeNativeCollectionValidator());
        controller.Execute(new MainCommand.SelectSlot(PlateSlot.RightRing), DateTime.UnixEpoch, Guid.Empty);
        controller.Execute(new MainCommand.SelectSlot(PlateSlot.Body), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(PlateSlot.RightRing, controller.View.SelectedAccessory);
        Assert.Equal(PlateSlot.Body, controller.View.SelectedSlot);
        controller.Execute(new MainCommand.SelectSlot((PlateSlot)12), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(PlateSlot.Body, controller.View.SelectedSlot);
        Assert.Equal(12, controller.View.Slots.Count);
    }

    /// <summary>A closed native editor cannot cause a source scan or native apply.</summary>
    [Fact]
    public void ClosedEditorRejectsLoadBeforeScanning()
    {
        var editor = new FakePlateEditor { Open = false };
        var source = new FakeNativeCollectionValidator();
        var controller = Create(editor, source);
        controller.Execute(new MainCommand.LoadActive(), DateTime.UnixEpoch, Guid.Empty);
        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, editor.ApplyCount);
        Assert.Contains("No slots were changed", controller.View.Status, StringComparison.Ordinal);
    }

    /// <summary>Unknown ownership is presented truthfully even with no visible items.</summary>
    [Fact]
    public void UnknownCollectionIsNotReportedAsEmptyOwnership()
    {
        var controller = Create(new FakePlateEditor(), new FakeNativeCollectionValidator());
        Assert.Empty(controller.View.VisibleItems);
        Assert.Contains("unavailable", controller.View.EmptyCollectionMessage, StringComparison.Ordinal);
        Assert.True(controller.View.CollectionNeedsAttention);
    }

    private static MainController Create(FakePlateEditor editor, FakeNativeCollectionValidator source)
        => new(new FakePlateLibraryStore(PlateLibrary.Create("Example", DateTime.UnixEpoch, Guid.NewGuid())),
            source, new FakeSharedCollectionSnapshotSource(), editor, new FakeItemFacts());

    /// <summary>Pending native dyes cannot create a local plate with an invented appearance.</summary>
    [Fact]
    public void PendingDyesExplainWhyCaptureWasRejected()
    {
        var controller = Create(new FakePlateEditor { CaptureResult = new(PlateCaptureOutcome.PendingDyes, null) },
            new FakeNativeCollectionValidator());
        controller.Execute(new MainCommand.CaptureCurrentPlate(), DateTime.UnixEpoch, Guid.NewGuid());
        Assert.Single(controller.View.SavedPlates);
        Assert.Contains("pending dye", controller.View.Status, StringComparison.Ordinal);
    }

    /// <summary>Unexpected native failure must leave a visible partial-result warning.</summary>
    [Fact]
    public void OperationFailureNeverReportsSuccess()
    {
        var controller = Create(new FakePlateEditor(), new FakeNativeCollectionValidator());
        controller.ReportOperationFailure(new MainCommand.LoadActive());
        Assert.Contains("partially changed", controller.View.Status, StringComparison.Ordinal);
        controller.ReportOperationFailure(new MainCommand.CreatePlate());
        Assert.Contains("may not have been saved", controller.View.Status, StringComparison.Ordinal);
        controller.Dispose();
        var prior = controller.View;
        controller.ReportOperationFailure(new MainCommand.LoadActive());
        Assert.Same(prior, controller.View);
    }
}
