using System.Text.Json;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests.Features.Diagnostics;

/// <summary>Diagnostics keep unavailable evidence distinct from completed observations.</summary>
public sealed class DiagnosticJournalBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Only fresh and stale observed sources are complete in the support report.</summary>
    [Fact]
    public void SourceCompletenessPreservesUnknownStatuses()
    {
        foreach (var status in Enum.GetValues<CollectionSourceStatus>())
        {
            var projected = DiagnosticJournal.Source(new(status, Now, [], null));

            Assert.Equal(status, projected.Status);
            Assert.Equal(Now, projected.ObservedAtUtc);
            Assert.Equal(status is CollectionSourceStatus.Fresh or CollectionSourceStatus.Stale
                ? (bool?)true : null, projected.Complete);
        }
    }

    /// <summary>A default immutable array is normalized before JSON serialization.</summary>
    [Fact]
    public void DefaultChecksSerializeAsEmptyArray()
    {
        var source = DiagnosticJournal.Source(null);
        var context = new DiagnosticContext(Now, false, false, false,
            CollectionDataFailure.None, null, source, source)
        { ExamineChecks = default };
        var journal = new DiagnosticJournal { Context = context };
        journal.Begin(DiagnosticPhase.ExamineSave, Now);
        journal.Finish(DiagnosticStop.Rejected, Now);

        using var json = JsonDocument.Parse(journal.ToJson(Now, context));

        Assert.Equal(0, json.RootElement.GetProperty("Context").GetProperty("ExamineChecks").GetArrayLength());
        Assert.Empty(journal.LastFailure!.Context!.ExamineChecks);
    }

    /// <summary>Cleanup cannot overwrite a terminal failure's stop reason or evidence time.</summary>
    [Theory]
    [InlineData((int)DiagnosticStop.Exception)]
    [InlineData((int)DiagnosticStop.Cancelled)]
    [InlineData((int)DiagnosticStop.CharacterChanged)]
    public void TerminalFailuresSurviveLaterCompletion(int reason)
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.LoadApply, Now);
        journal.Finish((DiagnosticStop)reason, Now);
        var failure = journal.LastFailure;

        journal.Finish(DiagnosticStop.Completed, Now.AddSeconds(1));

        Assert.Same(failure, journal.LastFailure);
        Assert.Equal((DiagnosticStop)reason, journal.Current.Stop);
        Assert.Equal(Now, journal.Current.ObservedAtUtc);
    }

    /// <summary>An observation without a stop outcome must not become a failure.</summary>
    [Fact]
    public void UnfinishedObservationDoesNotCreateFailure()
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.Capture, Now);

        journal.Finish(DiagnosticStop.None, Now.AddSeconds(1));

        Assert.Null(journal.LastFailure);
        Assert.Equal(DiagnosticStop.None, journal.Current.Stop);
    }

    /// <summary>Support output identifies the plugin and distinguishes pass, failure and unavailable checks.</summary>
    [Fact]
    public void CheckStatesAndReportIdentityAreExplicit()
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.LoadValidation, Now);
        Assert.True(journal.Check(DiagnosticKey.DresserLoaded, 1, 1));
        Assert.False(journal.Check(DiagnosticKey.ArmoireLoaded, 1, 0));
        Assert.False(journal.Check(DiagnosticKey.AgentPresent, 1, null));

        var report = journal.ToJson(Now, null);
        using var json = JsonDocument.Parse(report);

        Assert.Equal("BetterGlamorPlates", json.RootElement.GetProperty("Plugin").GetString());
        Assert.Contains("\n", report);
        Assert.Equal(new[] { DiagnosticStatus.Pass, DiagnosticStatus.Failure, DiagnosticStatus.NotAvailable },
            journal.Current.Checks.Select(check => check.Status));
        journal.Begin(DiagnosticPhase.Preview, Now);
        journal.Check(DiagnosticKey.NativePlateUnchanged, 1, 1);
        Assert.Single(journal.Current.Checks);
    }
}
