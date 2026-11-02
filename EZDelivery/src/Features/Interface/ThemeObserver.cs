namespace EZDelivery.Features.Interface;

/// <summary>Bounded framework polling; read failures preserve the last successful theme.</summary>
internal sealed class ThemeObserver
{
    private long nextRead;
    public uint Current { get; private set; }

    public void Update(long milliseconds, Func<uint?> read)
    {
        if (milliseconds < nextRead) return;
        nextRead = milliseconds + 500;
        try
        {
            if (read() is { } theme) Current = theme <= 7 ? theme : 0;
        }
        catch { /* Retain prior theme on host read failure. */ }
    }
}
