namespace CollectionShopper;

internal static class UiLayoutPolicy
{
    internal static float TooltipWidth(float fontSize, float viewportWidth, float horizontalPadding)
        => Math.Max(1f, Math.Min(fontSize * 26f, viewportWidth - horizontalPadding * 2f - 20f));
}
