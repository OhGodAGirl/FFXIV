using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Protects existing dye reservations and capability flags before native dye application.</summary>
public sealed class NativeDyeStatePolicyTests
{
    /// <summary>Both enabled capabilities retain shared bits when caller-supplied masks overlap.</summary>
    [Theory]
    [InlineData(3u, 2u, 3u)]
    [InlineData(2u, 2u, 2u)]
    public void OverlappingCapabilityMasksRetainTheirUnion(uint first, uint second, uint expected)
    {
        // Arrange
        const uint unrelated = 0x80000000u;

        // Act
        var actual = NativeDyeStatePolicy.ChannelFlags(unrelated, 2, first, second);

        // Assert
        Assert.Equal(unrelated | expected, actual);
    }

    /// <summary>Every slot is checked once; a clean plate produces no rejection or warning.</summary>
    [Fact]
    public void CleanPlateChecksExactlyTwelveSlots()
    {
        // Arrange
        var reads = new List<int>();
        var journal = new DiagnosticJournal();

        // Act
        var result = NativeDyeStatePolicy.RejectPending(index =>
        {
            Assert.InRange(index, 0, 11);
            Assert.True(reads.Count < 12);
            reads.Add(index);
            return false;
        }, journal);

        // Assert
        Assert.Null(result);
        Assert.Equal(Enumerable.Range(0, 12), reads);
        Assert.Empty(journal.Current.Checks);
    }

    /// <summary>The first allocation rejects further preparation, including at the last native slot.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(11)]
    public void PendingSlotRejectsWithoutReadingLaterSlots(int pendingSlot)
    {
        // Arrange
        var reads = new List<int>();
        var journal = new DiagnosticJournal();

        // Act
        var result = NativeDyeStatePolicy.RejectPending(index =>
        {
            Assert.InRange(index, 0, pendingSlot);
            Assert.True(reads.Count <= pendingSlot);
            reads.Add(index);
            return index == pendingSlot;
        }, journal);

        // Assert
        var plan = Assert.IsType<PlateDyePlan>(result);
        Assert.Empty(plan.Changes);
        var failure = Assert.Single(plan.Failures);
        Assert.Equal(PlateApplyFailureKind.PendingDyes, failure.Kind);
        Assert.Equal((PlateSlot)pendingSlot, failure.Slot);
        Assert.Equal(Enumerable.Range(0, pendingSlot + 1), reads);
        var check = Assert.Single(journal.Current.Checks);
        Assert.Equal(DiagnosticKey.PendingDye, check.Key);
        Assert.Equal(0, check.Expected);
        Assert.Equal(1, check.Observed);
        Assert.Equal(pendingSlot, check.Slot);
    }

    /// <summary>Changing dye support replaces stale capability bits while preserving every unrelated bit.</summary>
    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 2u)]
    [InlineData(2, 10u)]
    [InlineData(255, 10u)]
    public void CapabilityFlagsPreserveUnrelatedState(byte channels, uint expectedCapabilities)
    {
        // Arrange: all byte-sized source combinations plus high unrelated flags.
        for (uint previous = 0; previous <= 255; previous++)
        {
            var flags = previous | 0x80010000u;

            // Act
            var actual = NativeDyeStatePolicy.ChannelFlags(flags, channels, 2, 8);

            // Assert
            Assert.Equal(expectedCapabilities, actual & 10u);
            Assert.Equal(flags & ~10u, actual & ~10u);
        }
    }
}
