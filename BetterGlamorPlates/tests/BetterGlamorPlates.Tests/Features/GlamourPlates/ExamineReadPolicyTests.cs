using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Examine;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Tests.Features.GlamourPlates;

/// <summary>Checks copied Examine facts independently from native memory.</summary>
public sealed class ExamineReadPolicyTests
{
    private static readonly WindowBounds Bounds = new(0, 0, 620, 960);
    private static readonly ExamineReadFacts Ready = new(true, false, 0, 6, 123, true, 123, true);

    /// <summary>Diagnostics retain unavailable values and every copied readiness signal.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("ready")]
    [InlineData("failed")]
    [InlineData("zero")]
    [InlineData("sentinel")]
    public void DiagnosticsReportFactsWithoutInventingIdentity(string scenario)
    {
        var journal = new DiagnosticJournal();
        ExamineView? view = scenario == "missing" ? null : new(123, scenario == "failed" ? " " : "Player", Bounds);
        ExamineReadFacts? facts = scenario == "missing" ? null : scenario == "failed"
            ? Ready with { Active = false, Buddy = true, FetchState = 7, PreviewState = 2, RequestedEntity = 456, EquipmentMatches = false }
            : Ready with { RequestedEntity = scenario == "zero" ? 0 : scenario == "sentinel" ? 0xE0000000 : 123 };

        ExamineReadPolicy.ObserveDiagnostics(view, facts, journal);

        long? Observed(DiagnosticKey key) => Assert.Single(journal.Current.Checks, c => c.Key == key).Observed;
        Assert.Equal(scenario == "missing" ? 0 : 1, Observed(DiagnosticKey.ExamineVisible));
        Assert.Equal(scenario == "missing" ? 0 : 1, Observed(DiagnosticKey.AgentPresent));
        Assert.Equal(facts is null ? null : facts.Active ? 1 : 0, Observed(DiagnosticKey.AgentActive));
        Assert.Equal(facts is null ? null : (long?)facts.FetchState, Observed(DiagnosticKey.FetchState));
        Assert.Equal(facts is null ? null : facts.Buddy ? 0 : 1, Observed(DiagnosticKey.PlayerOnly));
        Assert.Equal(facts is null ? null : (long?)facts.PreviewState, Observed(DiagnosticKey.PreviewState));
        Assert.Equal(scenario == "missing" ? (long?)null : scenario == "failed" ? 0 : 1, Observed(DiagnosticKey.PlayerIdentity));
        Assert.Equal(scenario == "missing" ? (long?)null : scenario == "failed" ? 0 : 1, Observed(DiagnosticKey.RequestedIdentity));
        Assert.Equal(scenario == "missing" ? (long?)null : scenario == "failed" ? 0 : 1, Observed(DiagnosticKey.EquipmentMatches));
    }

    /// <summary>Expected identity diagnostics distinguish missing, changed and matching views.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("matching")]
    [InlineData("changed")]
    public void ExpectedDiagnosticsPreserveMissingAndMismatch(string scenario)
    {
        var journal = new DiagnosticJournal();
        ExamineView? view = scenario == "missing" ? null : new(scenario == "changed" ? 456u : 123u,
            scenario == "changed" ? "Other" : "Player", Bounds);

        ExamineReadPolicy.ObserveExpected(view, 123, "Player", journal);
        ExamineReadPolicy.ObserveExpected(view, 123, "Player", null);

        Assert.Equal(2, journal.Current.Checks.Length);
        Assert.All(journal.Current.Checks, check => Assert.Equal(scenario == "missing" ? (long?)null : scenario == "matching" ? 1 : 0, check.Observed));
    }

    /// <summary>Two occupied matching slots remain a nonempty outfit, rather than cancelling each other.</summary>
    [Fact]
    public void MultipleEquippedSlotsRemainAccepted()
    {
        var items = Enumerable.Repeat(new ExamineEquipmentFact(0, 0, 0, 0), 13).ToArray();
        items[0] = new(10, 0, 10, 0);
        items[12] = new(20, 0, 20, 0);
        Assert.True(ExamineReadPolicy.EquipmentMatches(items));
    }

    /// <summary>Hidden or transparent actual windows do not cover the icon; a higher-layer window does.</summary>
    [Theory]
    [InlineData(false, 255, 1u, 0, false)]
    [InlineData(true, 0, 1u, 0, false)]
    [InlineData(true, 255, 2u, 2, true)]
    public void WindowVisibilityAndHigherLayersAreIndependent(bool visible, int alpha, uint layer, int index, bool expected)
    {
        var window = new ExamineOverlayFact(layer, index, true, false, visible, (byte)alpha, true, "Window", Bounds);
        Assert.Equal(expected, ExamineReadPolicy.IsOccluded(Bounds, 1, 1, [window]));
    }

    /// <summary>The first entry in a valid native layer is a valid anchor.</summary>
    [Fact]
    public void ZeroAnchorIndexIsValid()
    {
        Assert.False(ExamineReadPolicy.IsOccluded(Bounds, 1, 0, []));
    }

    /// <summary>Each readiness failure has its exact stable explanation.</summary>
    [Theory]
    [InlineData("inactive", "Waiting for Examine data.")]
    [InlineData("buddy", "Only player glamours can be saved.")]
    [InlineData("fetch", "Examine is loading or could not load the character.")]
    [InlineData("preview", "Waiting for the Examine character preview.")]
    [InlineData("zero", "The examined player's identity is not available.")]
    [InlineData("sentinel", "The examined player's identity is not available.")]
    [InlineData("missing", "The examined player's identity is not available.")]
    [InlineData("changing", "The examined character is changing.")]
    [InlineData("equipment", "Waiting for the examined equipment.")]
    public void ReadinessRejectsIncompleteFacts(string scenario, string expected)
    {
        var facts = scenario switch
        {
            "inactive" => Ready with { Active = false, Buddy = true },
            "buddy" => Ready with { Buddy = true, FetchState = 1 },
            "fetch" => Ready with { FetchState = 1, PreviewState = 5 },
            "preview" => Ready with { PreviewState = 5, Entity = 0 },
            "zero" => Ready with { Entity = 0 },
            "sentinel" => Ready with { Entity = 0xE0000000 },
            "missing" => Ready with { PlayerPresent = false },
            "changing" => Ready with { RequestedEntity = 456, EquipmentMatches = false },
            _ => Ready with { EquipmentMatches = false },
        };
        var reason = ExamineReadPolicy.UnavailableReason(facts);
        Assert.Equal(expected, reason);
    }

    /// <summary>Unset or matching request identity permits an otherwise ready player.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(0xE0000000u)]
    [InlineData(123u)]
    public void ReadyFactsAllowCapture(uint requested)
    {
        var facts = Ready with { RequestedEntity = requested };
        Assert.Null(ExamineReadPolicy.UnavailableReason(facts));
    }

    /// <summary>Capture requires the same visible ready identity before and after copying.</summary>
    [Theory]
    [InlineData("ready", true)]
    [InlineData("null", false)]
    [InlineData("covered", false)]
    [InlineData("unavailable", false)]
    [InlineData("identity", false)]
    [InlineData("name", false)]
    public void CaptureRejectsEveryStaleOrUnavailableView(string scenario, bool expected)
    {
        ExamineView? view = new(123, "Player", Bounds);
        view = scenario switch
        {
            "null" => null,
            "covered" => view with { OverlayOccluded = true },
            "unavailable" => view with { UnavailableReason = "Loading" },
            "identity" => view with { EntityId = 456 },
            "name" => view with { Name = "Other" },
            _ => view,
        };
        Assert.Equal(expected, ExamineReadPolicy.CanCapture(view, 123, "Player"));
    }

    /// <summary>Incomplete, empty, mismatched or absent equipment never becomes accepted evidence.</summary>
    [Theory]
    [InlineData("short", false)]
    [InlineData("long", false)]
    [InlineData("empty", false)]
    [InlineData("missing", false)]
    [InlineData("mismatch", false)]
    [InlineData("ready", true)]
    public void EquipmentRequiresThirteenMatchingFactsAndSomeEquipment(string scenario, bool expected)
    {
        var items = Enumerable.Repeat(new ExamineEquipmentFact(0, 0, 0, 0), scenario == "short" ? 12 : scenario == "long" ? 14 : 13).ToArray();
        if (scenario != "empty") items[0] = new(10, 0, 10, 0);
        if (scenario == "missing") items[12] = new(0, 0, 20, 30, false);
        if (scenario == "mismatch") items[12] = new(40, 50, 20, 30);
        var journal = new DiagnosticJournal();
        journal.Begin(DiagnosticPhase.ExamineSave, DateTimeOffset.UnixEpoch);

        var result = ExamineReadPolicy.EquipmentMatches(items, journal);
        var withoutDiagnostics = ExamineReadPolicy.EquipmentMatches(items);

        Assert.Equal(expected, result);
        Assert.Equal(expected, withoutDiagnostics);
        if (scenario is "missing" or "mismatch")
        {
            var check = Assert.Single(journal.Current.Checks, c => c.Key == DiagnosticKey.ItemId);
            Assert.Equal(12, check.Slot);
            Assert.Equal(20, check.Expected);
            Assert.Equal(scenario == "missing" ? (long?)null : 40, check.Observed);
            var glamour = Assert.Single(journal.Current.Checks, c => c.Key == DiagnosticKey.GlamourId);
            Assert.Equal(30, glamour.Expected);
            Assert.Equal(scenario == "missing" ? (long?)null : 50, glamour.Observed);
        }
    }

    /// <summary>Native windows and recognized detail popups cover the save icon; unrelated overlays do not.</summary>
    [Theory]
    [InlineData("window", true)]
    [InlineData("_Tooltip", true)]
    [InlineData("_ItemDetail", true)]
    [InlineData("ItemDetail", true)]
    [InlineData("_ActionDetail", true)]
    [InlineData("ActionDetail", true)]
    [InlineData("other", false)]
    [InlineData("anchor", false)]
    [InlineData("hidden", false)]
    [InlineData("transparent", false)]
    [InlineData("behind", false)]
    [InlineData("higher", true)]
    [InlineData("away", false)]
    [InlineData("invalid", true)]
    public void OcclusionUsesVisibilityStackAndWindowKind(string scenario, bool expected)
    {
        var window = new ExamineOverlayFact(scenario == "higher" ? 2u : 1u, scenario == "behind" ? 2 : 0,
            scenario != "invalid", scenario == "anchor", scenario != "hidden", scenario == "transparent" ? (byte)0 : (byte)255,
            scenario is "window" or "higher" or "away", scenario, scenario == "away" ? new(1000, 1000, 20, 20) : Bounds);
        Assert.Equal(expected, ExamineReadPolicy.IsOccluded(Bounds, 1, 1, [window]));
    }

    /// <summary>Invalid stack facts fail closed, and scan count is explicitly bounded.</summary>
    [Fact]
    public void OcclusionRejectsMissingGeometryOrUnboundedStacks()
    {
        var invisible = new ExamineOverlayFact(1, 0, true, false, false, 255, false, "", Bounds);
        Assert.True(ExamineReadPolicy.IsOccluded(Bounds, 1, 1, null));
        Assert.True(ExamineReadPolicy.IsOccluded(Bounds, 1, -1, []));
        Assert.True(ExamineReadPolicy.IsOccluded(new(0, 0, 0, 0), 1, 1, []));
        Assert.False(ExamineReadPolicy.IsOccluded(Bounds, 1, 1, Enumerable.Repeat(invisible, 1024).ToArray()));
        Assert.True(ExamineReadPolicy.IsOccluded(Bounds, 1, 1, Enumerable.Repeat(invisible, 1025).ToArray()));
    }
}


