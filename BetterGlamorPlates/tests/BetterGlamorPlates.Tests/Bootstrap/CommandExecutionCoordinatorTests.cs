using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.GlamourPlates;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies effects ordered around a real preview coordinator and diagnostic journal.</summary>
public sealed class CommandExecutionCoordinatorTests
{
    /// <summary>A new command resets previous operation checks and identifies its own phase and time.</summary>
    [Fact]
    public void NewCommandStartsIndependentDiagnosticEvidence()
    {
        using var fixture = new Fixture();
        fixture.Diagnostics.Begin(DiagnosticPhase.ExamineSave, DateTimeOffset.UnixEpoch.AddSeconds(-1));
        fixture.Diagnostics.Check(DiagnosticKey.LocalOwner, 1, 0);
        fixture.Diagnostics.Confirm();
        fixture.Diagnostics.Finish(DiagnosticStop.Rejected, DateTimeOffset.UnixEpoch.AddSeconds(-1));

        fixture.Coordinator.Execute(new MainCommand.RefreshCollection());

        Assert.Equal(DiagnosticPhase.LocalOperation, fixture.Diagnostics.Current.Phase);
        Assert.Equal(DateTimeOffset.UnixEpoch, fixture.Diagnostics.Current.ObservedAtUtc);
        Assert.Equal(DiagnosticStop.Completed, fixture.Diagnostics.Current.Stop);
        Assert.Empty(fixture.Diagnostics.Current.Checks);
        Assert.Equal(0, fixture.Diagnostics.Current.ConfirmedActions);
    }
    /// <summary>Native writes release any retained preview before entering the controller.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWriteCancelsPreviewBeforeExecution(bool clear)
    {
        using var fixture = new Fixture();
        MainCommand command = clear ? new MainCommand.ClearOutfitItems(Guid.NewGuid(), 1) : new MainCommand.LoadActive();

        fixture.Coordinator.Execute(command);

        Assert.Equal(["release", "execute"], fixture.Effects);
        Assert.Equal(DiagnosticStop.Completed, fixture.Diagnostics.Current.Stop);
    }

    /// <summary>Selection requests preview immediately while dye edits suppress redundant success messages.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandPreviewUsesPostExecutionContext(bool dye)
    {
        using var fixture = new Fixture();
        MainCommand command = dye ? new MainCommand.UseStoredDyes() : new MainCommand.SelectSlot(PlateSlot.Body);
        fixture.Coordinator.Execute(command);
        Assert.Equal(["execute", "preview"], fixture.Effects);
        Assert.Equal(dye ? [] : [true], fixture.Reports);
        Assert.Equal(DiagnosticStop.Completed, fixture.Diagnostics.Current.Stop);
    }

    /// <summary>Controller diagnostics and a waiting preview are not overwritten with completion.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteOrRejectedOperationIsNotMarkedComplete(bool rejected)
    {
        using var fixture = new Fixture();
        if (rejected) fixture.Execute = _ => fixture.Diagnostics.Finish(DiagnosticStop.Rejected, DateTimeOffset.UnixEpoch);
        else fixture.Attempt = OutfitPreviewAttempt.NotReady;
        fixture.Coordinator.Execute(new MainCommand.SelectSlot(PlateSlot.Body));
        Assert.Equal(rejected ? DiagnosticStop.Rejected : DiagnosticStop.None, fixture.Diagnostics.Current.Stop);
    }

    /// <summary>Controller exceptions are reported once and prevent refresh or preview follow-up.</summary>
    [Fact]
    public void FailureIsReportedAndRethrownWithoutFollowup()
    {
        using var fixture = new Fixture();
        var failure = new IOException("persistence");
        fixture.Execute = _ => throw failure;
        var command = new MainCommand.RefreshCollection();
        Assert.Same(failure, Assert.Throws<IOException>(() => fixture.Coordinator.Execute(command)));
        Assert.Same(command, Assert.Single(fixture.Failures));
        Assert.Equal(["execute"], fixture.Effects);
    }

    /// <summary>Only an explicit refresh command requests provider work after execution.</summary>
    [Fact]
    public void RefreshRunsAfterControllerAndOtherCommandsDoNotRefresh()
    {
        using var fixture = new Fixture();
        fixture.Coordinator.Execute(new MainCommand.RefreshCollection());
        Assert.Equal(["execute", "refresh"], fixture.Effects);
        fixture.Effects.Clear();
        fixture.Context = fixture.Context with { EditorOpen = false };
        fixture.Coordinator.Execute(new MainCommand.CreatePlate());
        Assert.Equal(["execute"], fixture.Effects);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly CommandExecutionCoordinator Coordinator;
        internal readonly DiagnosticJournal Diagnostics = new();
        internal readonly List<string> Effects = [];
        internal readonly List<bool> Reports = [];
        internal readonly List<MainCommand> Failures = [];
        internal readonly PreviewCoordinator Preview;
        internal Action<MainCommand> Execute = _ => { };
        internal OutfitPreviewAttempt Attempt = OutfitPreviewAttempt.Applied;
        internal PreviewContext Context = new(true, true, false, 42,
            MainViewState.Empty with { SelectedPlateId = Guid.NewGuid() });

        internal Fixture()
        {
            Preview = new(() => Context, () => 0, (_, _) => { Effects.Add("preview"); return Attempt; },
                () => Effects.Add("release"), Reports.Add, () => { }, _ => { });
            Coordinator = new(() => Context, command => { Effects.Add("execute"); Execute(command); },
                Preview, Diagnostics, () => DateTimeOffset.UnixEpoch, Failures.Add, () => Effects.Add("refresh"));
        }

        public void Dispose() => Preview.Dispose();
    }
}
