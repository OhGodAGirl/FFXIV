namespace CollectionShopper;

internal sealed class ThemeObservation(Func<uint?> read, Action<uint> apply)
{
    internal void Update()
    {
        try
        {
            if (read() is { } theme)
                apply(theme);
        }
        catch (Exception) { }
    }
}
