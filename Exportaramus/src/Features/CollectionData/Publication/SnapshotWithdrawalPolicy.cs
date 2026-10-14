namespace Exportaramus.Features.CollectionData.Publication;

public sealed class SnapshotWithdrawalPolicy
{
    private double? gapStarted;
    public ulong ActiveContentId { get; private set; }
    public string? Observe(ulong contentId, bool confirmedLogout, bool zoning, double now)
    {
        if (ActiveContentId == 0) { if (!confirmedLogout) ActiveContentId = contentId; return null; }
        string? reason = confirmedLogout ? "LoggedOut" : contentId != 0 && contentId != ActiveContentId ? "CharacterChanged" : null;
        if (reason is null && contentId == ActiveContentId) { gapStarted = null; return null; }
        if (reason is null)
        {
            gapStarted ??= now;
            if (!zoning || now - gapStarted.Value >= 60) reason = "IdentityUnavailable";
        }
        if (reason is null) return null;
        ActiveContentId = reason == "CharacterChanged" ? contentId : 0;
        gapStarted = null; return reason;
    }
}
