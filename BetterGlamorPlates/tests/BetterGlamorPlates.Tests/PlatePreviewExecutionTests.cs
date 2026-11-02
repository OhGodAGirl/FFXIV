using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests;

/// <summary>Exercises production native-boundary orchestration using deterministic observations.</summary>
public sealed class PlatePreviewExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    /// <summary>Verifies valid preview sets only occupied slots and preserves mode.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ValidPreviewSetsOnlyOccupiedSlotsAndPreservesMode(int mode)
    {
        // Arrange
        var port = new Port();
        var journal = new DiagnosticJournal();
        var slots = Slots();
        slots[3] = slots[3] with { ItemId = 123, Stain0Id = 4, Stain1Id = 5 };
        // Act
        var result = Preview(slots, port, journal, (PlateDyeMode)mode);
        // Assert
        Assert.Equal(OutfitPreviewAttempt.Applied, result);
        Assert.Equal(["begin", "item", "active", "context", "unchanged"], port.Calls);
        Assert.Equal((slots[3], (PlateDyeMode)mode), Assert.Single(port.Items));
        Assert.Equal(DiagnosticStop.Completed, journal.Current.Stop);
        Assert.Equal(DiagnosticPhase.Preview, journal.Current.Phase);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PreviewInitialized && c.Observed == 1);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.SlotCount && c.Observed == 12);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.UniqueSlots && c.Observed == 12);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.NativePlateUnchanged && c.Observed == 1);
        Assert.Equal(1, journal.Current.ConfirmedActions);
    }

    /// <summary>Verifies invalid preview rejects before any write.</summary>
    [Theory]
    [InlineData("count")]
    [InlineData("duplicate")]
    [InlineData("mode")]
    [InlineData("dye-window")]
    [InlineData("negative-slot")]
    [InlineData("large-slot")]
    [InlineData("wrong-item-slot")]
    public void InvalidPreviewRejectsBeforeAnyWrite(string invalid)
    {
        var slots = Slots();
        var port = new Port { DyeWindowOpen = invalid == "dye-window", Compatible = invalid != "wrong-item-slot" };
        if (invalid == "count") slots = slots[..11];
        if (invalid == "duplicate") slots[1] = slots[0];
        if (invalid == "negative-slot") slots[0] = slots[0] with { Slot = (PlateSlot)(-1) };
        if (invalid == "large-slot") slots[0] = slots[0] with { Slot = (PlateSlot)12 };
        if (invalid == "wrong-item-slot") slots[0] = slots[0] with { ItemId = 123 };
        var journal = new DiagnosticJournal();
        var result = Preview(slots, port, journal, invalid == "mode" ? (PlateDyeMode)99 : PlateDyeMode.Saved);
        Assert.Equal(OutfitPreviewAttempt.Rejected, result);
        Assert.Empty(port.Calls);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.SlotCount && c.Observed == slots.Length);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.UniqueSlots && c.Observed == slots.Select(s => s.Slot).Distinct().Count());
        if (invalid is "negative-slot" or "large-slot" or "wrong-item-slot")
            Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.SlotCompatible && c.Observed == 0);
    }

    /// <summary>Verifies initialization is retryable only while logged in and editor open.</summary>
    [Theory]
    [InlineData("ready", true, true, true)]
    [InlineData("initialized", true, true, true)]
    [InlineData("state", true, true, true)]
    [InlineData("ready", false, true, false)]
    [InlineData("ready", true, false, false)]
    public void InitializationIsRetryableOnlyWhileLoggedInAndEditorOpen(string waiting, bool loggedIn, bool open, bool retry)
    {
        var port = new Port
        {
            Ready = waiting != "ready",
            Initialized = waiting != "initialized",
            State = waiting == "state" ? 5u : 6u,
            LoggedIn = loggedIn,
            EditorOpen = open
        };
        var journal = new DiagnosticJournal();
        var result = Preview(Slots(), port, journal);
        Assert.Equal(retry ? OutfitPreviewAttempt.NotReady : OutfitPreviewAttempt.Rejected, result);
        Assert.Equal(retry ? DiagnosticStop.None : DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PreviewInitialized && c.Observed == (port.Initialized == true ? 1 : 0));
        Assert.Empty(port.Calls);
    }

    /// <summary>Verifies rejects post write ambiguity without replaying preview.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsPostWriteAmbiguityWithoutReplayingPreview(bool sameContext)
    {
        var port = new Port { SameContext = sameContext, Unchanged = false };
        var journal = new DiagnosticJournal();
        var result = Preview(Slots(), port, journal);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        if (sameContext)
            Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.NativePlateUnchanged && c.Observed == 0);
        Assert.Equal(OutfitPreviewAttempt.Rejected, result);
        Assert.Equal(1, port.Calls.Count(c => c == "begin"));
        Assert.Equal(sameContext ? 1 : 0, port.Calls.Count(c => c == "release"));
        Assert.Equal(sameContext ? 1 : 0, port.Calls.Count(c => c == "unchanged"));
    }

    /// <summary>Missing native agent observations remain unknown in diagnostics.</summary>
    [Fact]
    public void MissingAgentPreservesUnknownDiagnosticValues()
    {
        var port = new Port { Ready = false, Initialized = null, State = null, LoggedIn = false };
        var journal = new DiagnosticJournal();
        var result = Preview(Slots(), port, journal);
        Assert.Equal(OutfitPreviewAttempt.Rejected, result);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PreviewInitialized && c.Observed == null);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PreviewState && c.Observed == null);
        Assert.Empty(port.Calls);
    }

    /// <summary>Empty slots need no item compatibility lookup.</summary>
    [Fact]
    public void EmptySlotsDoNotRequireItemFacts()
    {
        var port = new Port { Compatible = false };
        var result = Preview(Slots(), port);
        Assert.Equal(OutfitPreviewAttempt.Applied, result);
        Assert.Empty(port.Items);
    }

    private static OutfitPreviewAttempt Preview(SavedSlotView[] slots, Port port, DiagnosticJournal? journal = null, PlateDyeMode mode = PlateDyeMode.Saved)
        => PlatePreviewExecution.Preview(slots, mode, port, journal ?? new(), () => Now);
    private static SavedSlotView[] Slots() => Enumerable.Range(0, 12).Select(i => new SavedSlotView((PlateSlot)i, "", "", false, 0, default, null)).ToArray();
    private sealed class Port : IPlatePreviewPort
    {
        internal bool Ready = true, Compatible = true, SameContext = true, Unchanged = true;
        public bool? Initialized { get; set; } = true;
        public uint? State { get; set; } = 6;
        public bool DyeWindowOpen { get; set; }
        public bool LoggedIn { get; set; } = true;
        public bool EditorOpen { get; set; } = true;
        internal readonly List<string> Calls = [];
        internal readonly List<(SavedSlotView, PlateDyeMode)> Items = [];
        public bool ObserveReady() => Ready;
        public bool MatchesSlot(uint itemId, PlateSlot slot) => Compatible;
        public void Begin() => Calls.Add("begin");
        public void SetItem(SavedSlotView slot, PlateDyeMode mode) { Calls.Add("item"); Items.Add((slot, mode)); }
        public void MarkActive() => Calls.Add("active");
        public bool ContextMatches() { Calls.Add("context"); return SameContext; }
        public bool PlateUnchanged() { Calls.Add("unchanged"); return Unchanged; }
        public void Release() => Calls.Add("release");
    }
}
