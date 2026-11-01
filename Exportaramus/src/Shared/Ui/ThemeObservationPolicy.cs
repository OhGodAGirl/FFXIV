namespace Exportaramus.Shared.Ui;

/// <summary>Limits theme polling and keeps the last colors if a read fails.</summary>
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

        }
    }
}
