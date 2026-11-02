namespace BetterGlamorPlates.Bootstrap;

/// <summary>Applies close/reset and native-editor opening transitions once per observation.</summary>
internal sealed class EditorLifecycle(Action reset, Action cancelPreview, Action show, Action hide)
{
    private bool wasOpen;

    internal bool Update(bool windowOpen, bool editorOpen, bool dyeWindowOpen)
    {
        if (!windowOpen || !editorOpen) reset();
        if (dyeWindowOpen || !windowOpen) cancelPreview();
        if (!editorOpen)
        {
            cancelPreview();
            wasOpen = false;
            hide();
            return false;
        }
        if (!wasOpen)
        {
            wasOpen = true;
            show();
        }
        return true;
    }
}
