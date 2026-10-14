namespace Exportaramus.Shared.Ui;

/// <summary>Bounds theme sampling and preserves prepared colors when observation fails.</summary>
public sealed class ThemeObservationPolicy(Func<uint?> read, Action<uint> apply)
{
    private double nextRead;

    public void Update(double seconds)
    {
        if (seconds < nextRead)
        {
            return;
        }

        nextRead = seconds + 1;
        try
        {
            if (read() is { } theme)
            {
                apply(theme);
            }
        }
        catch (Exception)
        {
            // A failed observation leaves the last prepared palette unchanged.
        }
    }
}
