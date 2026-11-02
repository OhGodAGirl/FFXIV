using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests;

/// <summary>Exercises production native-boundary orchestration using deterministic observations.</summary>
public sealed class PlateCaptureExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly NativeCaptureItem Empty = new(0, 0, 0, 0, 0, false, false, false, false);

    /// <summary>Verifies captures all twelve slots with dyes without sharing mutable native state.</summary>
    [Fact]
    public void CapturesAllTwelveSlotsWithDyesWithoutSharingMutableNativeState()
    {
        // Arrange
        var id = Guid.NewGuid();
        var reads = new List<int>();
        var journal = new DiagnosticJournal();
        // Act
        var result = PlateCaptureExecution.Capture("Captured", Now.UtcDateTime, id, () => true, index =>
        {
            reads.Add(index);
            return index % 2 == 0 ? Empty : Empty with { ItemId = (uint)(100 + index), Dye0 = 4, Dye1 = 9 };
        }, journal, () => Now);
        // Assert
        Assert.Equal(PlateCaptureOutcome.Captured, result.Outcome);
        Assert.Equal(DiagnosticPhase.Capture, journal.Current.Phase);
        var plate = Assert.IsType<SavedPlate>(result.Plate);
        Assert.Equal(id, plate.Id);
        Assert.Equal("Captured", plate.Name);
        Assert.Equal(Now.UtcDateTime, plate.UpdatedAtUtc);
        Assert.Equal(Enumerable.Range(0, 12), reads);
        for (var index = 0; index < 12; index++)
        {
            if (index % 2 == 0) Assert.Null(plate.Items[index]);
            else
            {
                Assert.Equal((uint)(100 + index), plate.Items[index]!.ItemId);
                Assert.Equal((byte)4, plate.Items[index]!.Stain0Id);
                Assert.Equal((byte)9, plate.Items[index]!.Stain1Id);
            }
        }
    }

    /// <summary>Verifies closed editor never reads native slots.</summary>
    [Fact]
    public void ClosedEditorNeverReadsNativeSlots()
    {
        var journal = new DiagnosticJournal();
        var result = PlateCaptureExecution.Capture("Plate", Now.UtcDateTime, Guid.NewGuid(), () => false,
            _ => throw new InvalidOperationException("must not read"), journal, () => Now);
        Assert.Equal(PlateCaptureOutcome.EditorNotReady, result.Outcome);
        Assert.Null(result.Plate);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
    }

    /// <summary>Verifies any pending dye flag rejects capture even when stain is zero.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnyPendingDyeFlagRejectsCaptureEvenWhenStainIsZero(int pending)
    {
        var item = (Empty with { ItemId = 100 }) with
        {
            Pending0 = pending == 0 ? (byte)1 : (byte)0,
            Pending1 = pending == 1 ? (byte)2 : (byte)0,
            Inventory0 = pending == 2,
            Inventory1 = pending == 3,
            Item0 = pending == 4,
            Item1 = pending == 5,
        };
        var reads = 0;
        var journal = new DiagnosticJournal();
        var result = PlateCaptureExecution.Capture("Plate", Now.UtcDateTime, Guid.NewGuid(), () => true,
            _ => { reads++; return item; }, journal, () => Now);
        Assert.Equal(PlateCaptureOutcome.PendingDyes, result.Outcome);
        Assert.Null(result.Plate);
        Assert.Equal(1, reads);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Equal(6, journal.Current.Checks.Length);
        Assert.Equal(item.Inventory0 ? 1L : 0L, journal.Current.Checks.Single(c => c.Key == DiagnosticKey.InventoryDye0).Observed);
        Assert.Equal(item.Inventory1 ? 1L : 0L, journal.Current.Checks.Single(c => c.Key == DiagnosticKey.InventoryDye1).Observed);
        Assert.Equal(item.Item0 ? 1L : 0L, journal.Current.Checks.Single(c => c.Key == DiagnosticKey.ItemDye0).Observed);
        Assert.Equal(item.Item1 ? 1L : 0L, journal.Current.Checks.Single(c => c.Key == DiagnosticKey.ItemDye1).Observed);
        Assert.Contains(journal.Current.Checks, c => c.Observed > 0 && c.Slot == 0);
    }

    /// <summary>Verifies empty slots ignore stale dye fields.</summary>
    [Fact]
    public void EmptySlotsIgnoreStaleDyeFields()
    {
        var result = PlateCaptureExecution.Capture("Plate", Now.UtcDateTime, Guid.NewGuid(), () => true,
            _ => Empty with { Inventory0 = true }, new(), () => Now);
        Assert.Equal(PlateCaptureOutcome.Captured, result.Outcome);
        Assert.All(result.Plate!.Items, Assert.Null);
    }
}
