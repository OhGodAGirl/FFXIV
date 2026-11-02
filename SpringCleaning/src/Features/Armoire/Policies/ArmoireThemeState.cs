using System.Numerics;

namespace SpringCleaning;

internal sealed class ArmoireThemeState
{
    private long nextRead;
    internal Vector4 Surface { get; private set; } = new(0.27f, 0.275f, 0.27f, 1f);
    internal bool Light { get; private set; }

    internal void Update(long milliseconds, Func<uint?> read)
    {
        if (milliseconds < nextRead)
            return;
        nextRead = milliseconds + 1000;
        uint? theme = null;
        try { theme = read(); }
        catch { /* Retain the prior palette when the host cannot supply a theme. */ }
        if (theme is null)
            return;
        Light = theme is 1 or 4 or 7;
        Surface = theme switch
        {
            1 => new(0.94f, 0.81f, 0.65f, 1f),
            2 => new(0.22f, 0.10f, 0.76f, 1f),
            3 => new(0.22f, 0.32f, 0.55f, 1f),
            4 => new(0.78f, 0.78f, 0.80f, 1f),
            5 => new(0.27f, 0.46f, 0.16f, 1f),
            6 => new(0.30f, 0.31f, 0.32f, 1f),
            7 => new(0.93f, 0.63f, 0.85f, 1f),
            _ => new(0.27f, 0.275f, 0.27f, 1f),
        };
    }
}
