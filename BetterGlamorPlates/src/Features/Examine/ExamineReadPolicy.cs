using BetterGlamorPlates.Adapters;
using BetterGlamorPlates.Features.Diagnostics;
using BetterGlamorPlates.Features.Main;

namespace BetterGlamorPlates.Features.Examine;

internal sealed record ExamineReadFacts(bool Active, bool Buddy, uint FetchState, uint PreviewState,
    uint Entity, bool PlayerPresent, uint RequestedEntity, bool EquipmentMatches);
internal readonly record struct ExamineEquipmentFact(uint ItemId, uint GlamourId, uint DisplayedItemId, uint DisplayedGlamourId, bool Present = true);
internal readonly record struct ExamineOverlayFact(uint Layer, int Index, bool Valid, bool Anchor, bool Visible,
    byte Alpha, bool Window, string Name, WindowBounds Bounds);

/// <summary>Evaluates copied native facts without dereferencing game memory.</summary>
internal static class ExamineReadPolicy
{
    internal static string? UnavailableReason(ExamineReadFacts facts)
        => !facts.Active ? "Waiting for Examine data."
            : facts.Buddy ? "Only player glamours can be saved."
            : facts.FetchState != 0 ? "Examine is loading or could not load the character."
            : facts.PreviewState != 6 ? "Waiting for the Examine character preview."
            : facts.Entity is 0 or 0xE0000000 || !facts.PlayerPresent ? "The examined player's identity is not available."
            : facts.RequestedEntity is not (0 or 0xE0000000) && facts.RequestedEntity != facts.Entity ? "The examined character is changing."
            : !facts.EquipmentMatches ? "Waiting for the examined equipment." : null;

    internal static void ObserveDiagnostics(ExamineView? view, ExamineReadFacts? facts, DiagnosticJournal journal)
    {
        journal.Check(DiagnosticKey.ExamineVisible, 1, view is null ? 0 : 1);
        journal.Check(DiagnosticKey.AgentPresent, 1, facts is null ? 0 : 1);
        journal.Check(DiagnosticKey.AgentActive, 1, facts is null ? null : facts.Active ? 1 : 0);
        journal.Check(DiagnosticKey.FetchState, 0, facts?.FetchState);
        journal.Check(DiagnosticKey.PlayerOnly, 1, facts is null ? null : facts.Buddy ? 0 : 1);
        journal.Check(DiagnosticKey.PreviewState, 6, facts?.PreviewState);
        journal.Check(DiagnosticKey.PlayerIdentity, 1, view is null ? null : string.IsNullOrWhiteSpace(view.Name) ? 0 : 1);
        journal.Check(DiagnosticKey.RequestedIdentity, 1, facts is null ? null
            : facts.RequestedEntity is 0 or 0xE0000000 || facts.RequestedEntity == facts.Entity ? 1 : 0);
        journal.Check(DiagnosticKey.EquipmentMatches, 1, facts is null ? null : facts.EquipmentMatches ? 1 : 0);
    }

    internal static void ObserveExpected(ExamineView? view, uint expectedEntity, string expectedName, DiagnosticJournal? journal)
    {
        journal?.Check(DiagnosticKey.ExpectedIdentity, 1, view is null ? null : view.EntityId == expectedEntity ? 1 : 0);
        journal?.Check(DiagnosticKey.ExpectedName, 1, view is null ? null : view.Name == expectedName ? 1 : 0);
    }

    internal static bool CanCapture(ExamineView? view, uint expectedEntity, string expectedName)
        => view is not null && !view.OverlayOccluded && view.UnavailableReason is null
            && view.EntityId == expectedEntity && view.Name == expectedName;

    internal static bool EquipmentMatches(IReadOnlyList<ExamineEquipmentFact> items, DiagnosticJournal? journal = null)
    {
        if (items.Count != 13) return false;
        var hasEquipment = false;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (!item.Present || !ExaminedOutfitPolicy.MatchesEquipment(item.ItemId, item.GlamourId, item.DisplayedItemId, item.DisplayedGlamourId))
            {
                journal?.Check(DiagnosticKey.ItemId, item.DisplayedItemId % 1_000_000, item.Present ? item.ItemId % 1_000_000 : null, index);
                journal?.Check(DiagnosticKey.GlamourId, item.DisplayedGlamourId, item.Present ? item.GlamourId : null, index);
                return false;
            }
            hasEquipment |= item.DisplayedItemId != 0;
        }
        return hasEquipment;
    }

    internal static bool IsOccluded(WindowBounds bounds, uint anchorLayer, int anchorIndex, IReadOnlyList<ExamineOverlayFact>? windows)
    {
        if (windows is null || anchorIndex < 0) return true;
        var position = ExamineTabPlacement.Calculate(bounds, bounds);
        if (position is not { } location) return true;
        var size = ExamineTabPlacement.ButtonSize(bounds);
        var icon = new WindowBounds(location.X, location.Y, size.X, size.Y);
        var visited = 0;
        foreach (var candidate in windows)
        {
            if (++visited > 1024 || !candidate.Valid) return true;
            if (candidate.Anchor || !candidate.Visible || candidate.Alpha == 0) continue;
            if (!candidate.Window && candidate.Name is not
                ("_Tooltip" or "_ItemDetail" or "ItemDetail" or "_ActionDetail" or "ActionDetail")) continue;
            if (ExamineTabPlacement.IsCovered(anchorLayer, anchorIndex, candidate.Layer, candidate.Index, icon, candidate.Bounds)) return true;
        }
        return false;
    }
}




