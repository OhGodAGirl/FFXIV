using System.Collections.Immutable;
using System.Text.Json;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Shared.CollectionData;

namespace BetterGlamorPlates.Tests.Features.Diagnostics;

/// <summary>Support evidence must remain bounded, anonymous and independent of later cleanup.</summary>
public sealed class DiagnosticJournalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 1, 0, 0, TimeSpan.Zero);

    /// <summary>Reports identify the executing assembly, not a candidate path or a disk hash.</summary>
    [Fact]
    public void SerializesLoadedBuildAndUnknownState()
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.ExamineSave, Now);
        Assert.False(journal.Check(DiagnosticKey.FetchState, 0, null));
        using var json = JsonDocument.Parse(journal.ToJson(Now.ToOffset(TimeSpan.FromHours(3)), null));
        Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(typeof(DiagnosticJournal).Assembly.ManifestModule.ModuleVersionId,
            json.RootElement.GetProperty("LoadedModuleVersionId").GetGuid());
        Assert.Equal(typeof(DiagnosticJournal).Assembly.GetName().Version!.ToString(4),
            json.RootElement.GetProperty("Version").GetString());
        Assert.Equal(TimeSpan.Zero, json.RootElement.GetProperty("CapturedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal("NotAvailable", json.RootElement.GetProperty("Current").GetProperty("Checks")[0].GetProperty("Status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Context").ValueKind);
    }

    /// <summary>Cleanup and successful later operations cannot erase or retimestamp the failure.</summary>
    [Fact]
    public void RetainsFailureBeforeCleanupAndLaterSuccess()
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.LoadApply, Now);
        journal.Confirm();
        journal.Check(DiagnosticKey.Dye1, 7, 0, 3);
        journal.Finish(DiagnosticStop.Rejected, Now);
        var failure = journal.LastFailure;
        journal.Check(DiagnosticKey.Dye1, 7, 7, 3);
        journal.Finish(DiagnosticStop.Exception, Now.AddSeconds(1));
        journal.Begin(DiagnosticPhase.Preview, Now.AddSeconds(2));
        journal.Finish(DiagnosticStop.Completed, Now.AddSeconds(2));
        Assert.Same(failure, journal.LastFailure);
        Assert.Equal(Now, failure!.ObservedAtUtc);
        Assert.Equal(1, failure.ConfirmedActions);
        Assert.Equal(0, Assert.Single(failure.Checks).Observed);
        Assert.Empty(journal.Current.Checks);
        Assert.Equal(DiagnosticStop.Completed, journal.Current.Stop);
    }

    /// <summary>Provider text and inventory contents are deliberately omitted rather than scrubbed by guessing.</summary>
    [Fact]
    public void SourceProjectionExcludesPrivateTextAndItemLists()
    {
        const string privateText = @"Player Name C:\Users\private secret-token";
        var source = new CollectionSourceSnapshot(CollectionSourceStatus.Stale, Now, [1234567], privateText);
        var context = new DiagnosticContext(Now, false, true, true, CollectionDataFailure.None, Now,
            DiagnosticJournal.Source(source), DiagnosticJournal.Source(null));
        var journal = new DiagnosticJournal { Context = context };
        journal.Begin(DiagnosticPhase.ExamineSave, Now);
        journal.Finish(DiagnosticStop.Rejected, Now);
        var report = journal.ToJson(Now, context);
        Assert.DoesNotContain("Player Name", report);
        Assert.DoesNotContain("private", report);
        Assert.DoesNotContain("secret-token", report);
        Assert.DoesNotContain("1234567", report);
        Assert.True(context.Dresser.Complete);
        Assert.Null(context.Armoire.Complete);
        journal.Context = context with { ProviderSnapshotAvailable = false };
        Assert.True(journal.LastFailure!.Context!.ProviderSnapshotAvailable);
    }

    /// <summary>A long operation retains its last failure inside a fixed report budget.</summary>
    [Fact]
    public void BoundsChecksAndPreservesNewestFailure()
    {
        var journal = new DiagnosticJournal();
        var source = DiagnosticJournal.Source(null);
        journal.Context = new DiagnosticContext(Now, false, false, false, CollectionDataFailure.None, null, source, source)
        {
            ExamineChecks = Enumerable.Repeat(new DiagnosticCheck(DiagnosticKey.NativePlateUnchanged,
                DiagnosticStatus.NotAvailable, long.MaxValue, long.MinValue, 11), 1000).ToImmutableArray(),
        };
        journal.Begin(DiagnosticPhase.LoadApply, Now);
        for (var i = 0; i < 1000; i++)
        {
            journal.Check(DiagnosticKey.NativePlateUnchanged, long.MaxValue, long.MinValue, 11);
            journal.Confirm();
        }
        journal.Check(DiagnosticKey.Dye0, 1, 2, 3);
        journal.Finish(DiagnosticStop.Rejected, Now);
        Assert.Equal(DiagnosticJournal.MaximumChecks, journal.Current.Checks.Length);
        Assert.Equal(DiagnosticKey.Dye0, journal.Current.Checks[^1].Key);
        Assert.Equal(100, journal.Current.ConfirmedActions);
        Assert.Equal(16, journal.LastFailure!.Context!.ExamineChecks.Length);
        Assert.True(journal.ToJson(Now, journal.Context).Length < 65536);
    }

    /// <summary>Copying produces evidence only and does not change the journal.</summary>
    [Fact]
    public void CopyIsObservationOnly()
    {
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.LoadApply, Now);
        journal.Check(DiagnosticKey.ItemId, 42, 42);
        var before = journal.Current;
        Assert.Equal(journal.ToJson(Now, null), journal.ToJson(Now, null));
        Assert.Same(before, journal.Current);
        Assert.Null(journal.LastFailure);
    }
}
