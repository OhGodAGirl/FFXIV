namespace BetterGlamorPlates.Bootstrap;

/// <summary>Polls optional theme configuration at a bounded interval and publishes changes only.</summary>
internal sealed class ThemeObserver(Func<uint?> read, Action<uint> publish)
{
    private long nextRead;
    private uint? observed;

    internal void Update(long now)
    {
        if (now < nextRead) return;
        nextRead = now + 1000;
        try
        {
            if (read() is { } theme && observed != theme)
            {
                publish(theme);
                observed = theme;
            }
        }
        catch (Exception)
        {
            // Optional presentation facts retain their last value and retry at the next interval.
        }
    }
}
