namespace SpringCleaning;

// Holds only presentation timing and scope. It never stores or supplies ownership evidence.
internal sealed class PresentationRefreshPolicy
{
    private ulong character;
    private long discontinuity;
    private bool hadAcceptedSnapshot;
    private DateTime? unavailableSince;

    public bool ClearDisplay { get; private set; }

    public bool RetainDisplay(DateTime now, ulong contentId, long epoch,
        bool hasSnapshot, bool refreshing, bool failed)
    {
        ClearDisplay = false;
        if (contentId != character || epoch != discontinuity || contentId == 0 || failed)
        {
            ClearDisplay = true;
            character = contentId;
            discontinuity = epoch;
            hadAcceptedSnapshot = false;
            unavailableSince = null;
        }

        if (contentId == 0 || failed)
            return false;

        if (hasSnapshot)
        {
            hadAcceptedSnapshot = true;
            unavailableSince = null;
            return false;
        }

        if (!hadAcceptedSnapshot || !refreshing)
        {
            ClearDisplay = true;
            return false;
        }

        unavailableSince ??= now;
        var retain = now >= unavailableSince.Value
            && now - unavailableSince.Value < TimeSpan.FromMilliseconds(500);
        ClearDisplay = !retain;
        return retain;
    }
}
