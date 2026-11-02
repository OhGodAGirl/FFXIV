using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests;

/// <summary>Exercises production native-boundary orchestration using deterministic observations.</summary>
public sealed class PlateApplyExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies clear all clears every occupied native slot and restores selection.</summary>
    [Fact]
    public void ClearAllClearsEveryOccupiedNativeSlotAndRestoresSelection()
    {
        // Arrange: local empty slots must clear actual native contents, including accessories.
        var port = new Port();
        Array.Fill(port.Items, 42u);
        var journal = new DiagnosticJournal();
        // Act
        var result = Apply(EmptyPlan(), port, journal);
        // Assert
        Assert.Equal(12, result.Cleared);
        Assert.Equal(0, result.Loaded);
        Assert.Empty(result.Failures);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => (PlateSlot)i), port.Clears);
        Assert.All(port.Items, item => Assert.Equal(0u, item));
        Assert.Equal(7u, port.SelectedSlot);
        Assert.True(port.HasChanges);
        Assert.Equal(12, journal.Current.ConfirmedActions);
        Assert.Equal(DiagnosticStop.Completed, journal.Current.Stop);
        Assert.Equal(DiagnosticPhase.LoadApply, journal.Current.Phase);
        Assert.Equal(Now, journal.Current.ObservedAtUtc);
    }

    /// <summary>Verifies empty native slots do not submit redundant clear or lose existing changes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyNativeSlotsDoNotSubmitRedundantClearOrLoseExistingChanges(bool priorChanges)
    {
        var port = new Port { HasChanges = priorChanges };
        var result = Apply(EmptyPlan(), port);
        Assert.Empty(port.Clears);
        Assert.Empty(port.Loads);
        Assert.Equal(0, result.Cleared);
        Assert.Equal(priorChanges, port.HasChanges);
    }

    /// <summary>Verifies invalid plan or closed editor rejects before preparation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidPlanOrClosedEditorRejectsBeforePreparation(bool ready)
    {
        var port = new Port { Ready = ready };
        var result = Apply(ready ? new([], []) : EmptyPlan(), port);
        Assert.Equal(PlateApplyFailureKind.EditorNotReady, Assert.Single(result.Failures).Kind);
        Assert.Equal(0, port.Preparations);
        Assert.Equal(0, port.Captures);
        Assert.Empty(port.Clears);
    }

    /// <summary>Verifies pending dyes reject whole plan without native changes.</summary>
    [Fact]
    public void PendingDyesRejectWholePlanWithoutNativeChanges()
    {
        var port = new Port { DyePlan = new([], [new(PlateApplyFailureKind.PendingDyes, PlateSlot.Body)]) };
        var journal = new DiagnosticJournal();
        var result = Apply(EmptyPlan(), port, journal);
        Assert.Same(port.DyePlan.Failures, result.Failures);
        Assert.Equal(1, port.Preparations);
        Assert.Equal(0, port.Captures);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
    }

    /// <summary>Verifies partial load skips missing occupied slot without clearing or dyeing it.</summary>
    [Fact]
    public void PartialLoadSkipsMissingOccupiedSlotWithoutClearingOrDyeingIt()
    {
        var port = new Port();
        port.Items[1] = 777;
        var plan = LoadedPlan();
        var steps = plan.Steps.ToArray();
        steps[1] = new(PlateSlot.OffHand, new() { ItemId = 999 }, null) { IsSkipped = true };
        plan = new(steps, [new(PlateLoadIssueKind.ExactSourceUnavailable, PlateSlot.OffHand, 999)]) { AllowMissingItems = true };
        var result = Apply(plan, port);
        Assert.Equal(1, result.Loaded);
        Assert.Empty(result.Failures);
        Assert.Equal(777u, port.Items[1]);
        Assert.Equal([PlateSlot.MainHand], port.Loads);
        Assert.Equal([PlateSlot.MainHand], port.Dyes);
        Assert.Empty(port.Clears);
        Assert.Equal(7u, port.SelectedSlot);
    }

    /// <summary>Verifies ambiguous readback stops once without retry.</summary>
    [Theory]
    [InlineData("clear", (int)PlateApplyFailureKind.ClearRejected)]
    [InlineData("load", (int)PlateApplyFailureKind.ItemRejected)]
    [InlineData("dye", (int)PlateApplyFailureKind.DyeRejected)]
    public void AmbiguousReadbackStopsOnceWithoutRetry(string failure, int expected)
    {
        var port = new Port { RejectClear = failure == "clear", MatchItem = failure != "load", MatchDye = failure != "dye" };
        Array.Fill(port.Items, 99u);
        var result = Apply(failure == "clear" ? EmptyPlan() : LoadedPlan(), port);
        var issue = Assert.Single(result.Failures);
        Assert.Equal((PlateApplyFailureKind)expected, issue.Kind);
        Assert.Equal(PlateSlot.MainHand, issue.Slot);
        Assert.Equal(failure == "clear" ? 0u : 100u, issue.ItemId);
        Assert.Equal(0, result.Loaded);
        Assert.Equal(0, result.Cleared);
        Assert.Equal(1, port.Clears.Count + port.Loads.Count);
        Assert.Equal(7u, port.SelectedSlot);
        Assert.False(port.HasChanges);
    }

    /// <summary>Verifies context loss prevents further writes and does not restore into another plate.</summary>
    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(2, false, 1)]
    [InlineData(2, true, 1)]
    public void ContextLossPreventsFurtherWritesAndDoesNotRestoreIntoAnotherPlate(int lostAt, bool load, int attempts)
    {
        var port = new Port { LoseAtContextCheck = lostAt };
        Array.Fill(port.Items, 42u);
        var result = Apply(load ? LoadedPlan() : EmptyPlan(), port);
        Assert.Equal(PlateApplyFailureKind.EditorClosed, Assert.Single(result.Failures).Kind);
        Assert.Equal(attempts, port.Loads.Count + port.Clears.Count);
        Assert.Equal(load ? 0u : 7u, port.SelectedSlot);
        Assert.False(port.HasChanges);
    }

    /// <summary>Verifies context loss after confirmed action reports partial result.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextLossAfterConfirmedActionReportsPartialResult(bool load)
    {
        var port = new Port { LoseAtContextCheck = 3 };
        Array.Fill(port.Items, 42u);
        var result = Apply(load ? LoadedPlan() : EmptyPlan(), port);
        Assert.Equal(1, result.Loaded + result.Cleared);
        Assert.Equal([PlateApplyFailureKind.EditorClosed, PlateApplyFailureKind.EditorClosedAfterPartial], result.Failures.Select(f => f.Kind));
        Assert.Equal(1, port.Clears.Count + port.Loads.Count);
        Assert.False(port.HasChanges);
    }

    /// <summary>Verifies context loss only at completion reports partial and rejected diagnostic.</summary>
    [Fact]
    public void ContextLossOnlyAtCompletionReportsPartialAndRejectedDiagnostic()
    {
        var port = new Port { LoseAtContextCheck = 15 };
        var journal = new DiagnosticJournal();
        var result = Apply(LoadedPlan(), port, journal);
        Assert.Equal(1, result.Loaded);
        Assert.Equal(PlateApplyFailureKind.EditorClosedAfterPartial, Assert.Single(result.Failures).Kind);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
    }

    /// <summary>Verifies exception propagates without retry and restores selection only in original context.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionPropagatesWithoutRetryAndRestoresSelectionOnlyInOriginalContext(bool contextLost)
    {
        var port = new Port { ThrowOnLoad = true, LoseOnThrow = contextLost };
        var journal = new DiagnosticJournal();
        Assert.Throws<InvalidOperationException>(() => Apply(LoadedPlan(), port, journal));
        Assert.Single(port.Loads);
        Assert.Empty(port.Dyes);
        Assert.Equal(contextLost ? 0u : 7u, port.SelectedSlot);
        Assert.Equal(DiagnosticStop.Exception, journal.Current.Stop);
    }

    /// <summary>Verifies null plan rejects before port interaction.</summary>
    [Fact]
    public void NullPlanRejectsBeforePortInteraction()
    {
        var port = new Port();
        Assert.Throws<ArgumentNullException>(() => Apply(null!, port));
        Assert.Equal(0, port.Preparations);
    }

    /// <summary>One confirmed loaded item marks native changes and records plan and context evidence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedLoadPreservesChangeStateAndObservedContext(bool priorChanges)
    {
        var port = new Port { HasChanges = priorChanges, ResetChangesOnLoad = true };
        var journal = new DiagnosticJournal();
        var result = Apply(LoadedPlan(), port, journal);
        Assert.Equal(1, result.Loaded);
        Assert.True(port.HasChanges);
        Assert.Equal(1, port.Captures);
        Assert.Equal(1, journal.Current.ConfirmedActions);
        Assert.Equal(DiagnosticPhase.LoadApply, journal.Current.Phase);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PlanExecutable && c.Expected == 1 && c.Observed == 1);
        Assert.Equal(Enumerable.Repeat(true, 13).Concat([false, false]), port.ContextObservations);
    }

    /// <summary>Invalid plan evidence distinguishes rejected structure from native unavailability.</summary>
    [Fact]
    public void InvalidPlanRecordsFailedExecutableCheck()
    {
        var journal = new DiagnosticJournal();
        var result = Apply(new([], []), new(), journal);
        Assert.Equal(PlateApplyFailureKind.EditorNotReady, Assert.Single(result.Failures).Kind);
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Contains(journal.Current.Checks, c => c.Key == DiagnosticKey.PlanExecutable && c.Observed == 0);
    }

    /// <summary>Clear readback observes the original native context before and after the write.</summary>
    [Fact]
    public void ClearContextChecksAreObservableAndCleanupIsQuiet()
    {
        var port = new Port();
        port.Items[0] = 1;
        Apply(EmptyPlan(), port);
        Assert.Equal(Enumerable.Repeat(true, 13).Concat([false, false]), port.ContextObservations);
    }

    /// <summary>A cleanup exception must not erase the previously recorded native rejection.</summary>
    [Fact]
    public void RejectedReadbackIsRecordedBeforeSelectionCleanup()
    {
        var port = new Port { RejectClear = true, ThrowOnRestore = true };
        port.Items[0] = 1;
        var journal = new DiagnosticJournal();
        Assert.Throws<InvalidOperationException>(() => Apply(EmptyPlan(), port, journal));
        Assert.Equal(DiagnosticStop.Rejected, journal.Current.Stop);
        Assert.Single(port.Clears);
    }

    private static PlateApplyResult Apply(PlateLoadPlan plan, Port port, DiagnosticJournal? journal = null)
        => PlateApplyExecution.Apply(plan, port, journal ?? new(), () => Now);
    private static PlateLoadPlan EmptyPlan() => new(Enumerable.Range(0, 12).Select(i => new PlateLoadStep((PlateSlot)i, null, null)).ToArray(), []);
    private static PlateLoadPlan LoadedPlan()
    {
        var steps = EmptyPlan().Steps.ToArray();
        steps[0] = new(PlateSlot.MainHand, new() { ItemId = 100 }, new(100, CollectionSource.Dresser, 12, 0, 0));
        return new(steps, []);
    }
    private sealed class Port : IPlateApplyPort
    {
        internal bool Ready = true, RejectClear, MatchItem = true, MatchDye = true, ThrowOnLoad, LoseOnThrow;
        internal bool ResetChangesOnLoad, ThrowOnRestore;
        internal int Preparations, Captures, LoseAtContextCheck = int.MaxValue;
        private int contextChecks;
        internal readonly uint[] Items = new uint[12];
        internal readonly List<PlateSlot> Clears = [], Loads = [], Dyes = [];
        internal PlateDyePlan DyePlan = new([], []);
        private uint selectedSlot = 7;
        public uint SelectedSlot
        {
            get => selectedSlot;
            set
            {
                if (ThrowOnRestore && value == 7) throw new InvalidOperationException("selection restore failed");
                selectedSlot = value;
            }
        }
        public bool HasChanges { get; set; }
        public bool ObserveReady() => Ready;
        public PlateDyePlan PrepareDyes(PlateLoadPlan plan) { Preparations++; return DyePlan; }
        public void CaptureContext() => Captures++;
        internal readonly List<bool> ContextObservations = [];
        public bool ContextMatches(bool observe)
        {
            ContextObservations.Add(observe);
            return ++contextChecks < LoseAtContextCheck;
        }
        public uint ReadItemId(PlateSlot slot) => Items[(int)slot];
        public void Clear(PlateSlot slot) { Clears.Add(slot); if (!RejectClear) Items[(int)slot] = 0; }
        public void Load(PlateLoadStep step)
        {
            Loads.Add(step.Slot);
            if (ResetChangesOnLoad) HasChanges = false;
            if (ThrowOnLoad)
            {
                if (LoseOnThrow) LoseAtContextCheck = 0;
                throw new InvalidOperationException("native submission failed");
            }
            Items[(int)step.Slot] = step.Source!.ItemId;
        }
        public bool ObserveLoadedItem(PlateLoadStep step) => MatchItem;
        public bool ApplyDyes(PlateLoadStep step, PlateDyePlan plan) { Dyes.Add(step.Slot); return MatchDye; }
    }
}
