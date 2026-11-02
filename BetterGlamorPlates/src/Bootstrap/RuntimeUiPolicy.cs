namespace BetterGlamorPlates.Bootstrap;

internal static class RuntimeUiPolicy
{
    internal static ulong CharacterId(bool loggedIn, Func<bool> playerLoaded, Func<ulong> contentId)
        => loggedIn && playerLoaded() ? contentId() : 0;

    internal static bool IsDiagnosticsCommand(string arguments)
        => arguments.Trim().Equals("diagnostics", StringComparison.OrdinalIgnoreCase);

    internal static bool ExamineChanged(uint? previousEntity, uint? currentEntity)
        => previousEntity != currentEntity;

    internal static void ShowWhenAvailable(bool editorOpen, Action show, Action refresh)
    {
        if (!editorOpen) return;
        show();
        refresh();
    }
}

/// <summary>Limits support-context capture independently of frame rate.</summary>
internal sealed class DiagnosticRefreshSchedule
{
    private DateTimeOffset next;

    internal bool Take(DateTimeOffset now)
    {
        if (now < next) return false;
        next = now.AddMilliseconds(500);
        return true;
    }
}
