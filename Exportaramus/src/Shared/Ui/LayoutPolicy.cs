namespace Exportaramus.Shared.Ui;

public static class LayoutPolicy
{
    public static LayoutPlan At(float contentWidth) => new(contentWidth >= 720,
        contentWidth >= 700 ? 3 : contentWidth >= 460 ? 2 : 1,
        (contentWidth - 8) * .54f, (contentWidth - 8) * .46f);
    public static bool WideDiagnosticActions(float contentWidth) => contentWidth >= 590;
    public static float PressOffset(bool reduceMotion, bool held) => !reduceMotion && held ? 1.5f : 0;
}
