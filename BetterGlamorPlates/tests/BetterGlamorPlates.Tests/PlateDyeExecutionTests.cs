using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests;

/// <summary>Exercises production native-boundary orchestration using deterministic observations.</summary>
public sealed class PlateDyeExecutionTests
{
    private static readonly PlateLoadStep Step = new(PlateSlot.Body, new() { ItemId = 100 }, new(100, CollectionSource.Dresser, 12, 0, 0));
    private static readonly DyeSupply Supply = new(5, 200, 3, 4, 2);
    private static readonly PlateDyeChange Change = new(PlateSlot.Body, 0, 5, Supply);

    /// <summary>Verifies applies only target slot changes in channel order.</summary>
    [Fact]
    public void AppliesOnlyTargetSlotChangesInChannelOrder()
    {
        // Arrange
        var second = Change with { Channel = 1, DyeId = 6 };
        var other = Change with { Slot = PlateSlot.Head };
        var port = new Port();
        var journal = new DiagnosticJournal();
        // Act
        var result = PlateDyeExecution.Apply(Step, new([other, Change, second], []), port, journal);
        // Assert
        Assert.True(result);
        Assert.Equal([Change, second], port.Applied);
        Assert.Equal(2, port.Readbacks);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PendingDye0 && c.Expected == 5 && c.Observed == 5);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PendingDye1 && c.Expected == 6 && c.Observed == 6);
    }

    /// <summary>Verifies no dye changes performs no native calls.</summary>
    [Fact]
    public void NoDyeChangesPerformsNoNativeCalls()
    {
        var port = new Port();
        Assert.True(PlateDyeExecution.Apply(Step, new([], []), port, new()));
        Assert.Equal(0, port.ContextChecks);
        Assert.Equal(0, port.SupplyReads);
        Assert.Empty(port.Applied);
    }

    /// <summary>Verifies invalid inventory never submits dye.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("unloaded")]
    [InlineData("past-end")]
    [InlineData("negative-size")]
    [InlineData("different-item")]
    [InlineData("insufficient")]
    public void InvalidInventoryNeverSubmitsDye(string kind)
    {
        var port = new Port();
        port.SupplyState = kind switch
        {
            "missing" => null,
            "unloaded" => port.SupplyState! with { Loaded = false },
            "past-end" => port.SupplyState! with { Size = Supply.InventorySlot },
            "negative-size" => port.SupplyState! with { Size = -1 },
            "different-item" => port.SupplyState! with { ItemId = 201 },
            _ => port.SupplyState! with { Quantity = 1 },
        };
        Assert.False(Apply(port));
        Assert.Equal(1, port.SupplyReads);
        Assert.Empty(port.Applied);
        Assert.Equal(0, port.Readbacks);
    }

    /// <summary>Verifies changed target item stops before inventory access.</summary>
    [Fact]
    public void ChangedTargetItemStopsBeforeInventoryAccess()
    {
        var port = new Port { ItemId = 101 };
        Assert.False(Apply(port));
        Assert.Equal(0, port.SupplyReads);
        Assert.Empty(port.Applied);
    }

    /// <summary>Verifies context loss stops before reading or replaying native result.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void ContextLossStopsBeforeReadingOrReplayingNativeResult(int check, int writes)
    {
        var port = new Port { LoseContextAt = check };
        Assert.False(Apply(port));
        Assert.Equal(writes, port.Applied.Count);
        Assert.Equal(0, port.Readbacks);
    }

    /// <summary>Verifies ambiguous readback stops without submitting second channel or retry.</summary>
    [Theory]
    [InlineData("dye")]
    [InlineData("inventory-flag")]
    [InlineData("item-flag")]
    [InlineData("item")]
    [InlineData("container")]
    [InlineData("slot")]
    public void AmbiguousReadbackStopsWithoutSubmittingSecondChannelOrRetry(string mismatch)
    {
        var port = new Port { Mismatch = mismatch };
        var journal = new DiagnosticJournal();
        var result = PlateDyeExecution.Apply(Step, new([Change, Change with { Channel = 1 }], []), port, journal);
        Assert.False(result);
        Assert.Single(port.Applied);
        Assert.Equal(1, port.Readbacks);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.DyeAllocation && c.Observed == 0);
    }

    /// <summary>Verifies submission exception is not swallowed or retried.</summary>
    [Fact]
    public void SubmissionExceptionIsNotSwallowedOrRetried()
    {
        var port = new Port { ThrowOnApply = true };
        Assert.Throws<InvalidOperationException>(() => Apply(port));
        Assert.Single(port.Applied);
        Assert.Equal(0, port.Readbacks);
    }

    private static bool Apply(Port port) => PlateDyeExecution.Apply(Step, new([Change], []), port, new());
    private sealed class Port : IPlateDyePort
    {
        internal NativeDyeSupplyState? SupplyState = new(true, 5, 200, 2);
        internal uint ItemId = 100;
        internal int ContextChecks, SupplyReads, Readbacks, LoseContextAt = int.MaxValue;
        internal string? Mismatch;
        internal bool ThrowOnApply;
        internal readonly List<PlateDyeChange> Applied = [];
        public bool ContextMatches() => ++ContextChecks < LoseContextAt;
        public uint ReadItemId(PlateSlot slot) => ItemId;
        public NativeDyeSupplyState? ReadSupply(DyeSupply supply) { SupplyReads++; return SupplyState; }
        public void Apply(PlateDyeChange change)
        {
            Applied.Add(change);
            if (ThrowOnApply) throw new InvalidOperationException("native dye failed");
        }
        public NativeDyeReadback ReadDye(PlateDyeChange change)
        {
            Readbacks++;
            return new(Mismatch == "dye" ? (byte)0 : change.DyeId, Mismatch != "inventory-flag", Mismatch == "item-flag",
                Mismatch == "item" ? 0 : change.Supply.ItemId,
                Mismatch == "container" ? 0 : change.Supply.Container,
                Mismatch == "slot" ? (ushort)0 : change.Supply.InventorySlot);
        }
    }
}
