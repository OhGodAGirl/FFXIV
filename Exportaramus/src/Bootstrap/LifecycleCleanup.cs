namespace Exportaramus.Bootstrap;

public static class LifecycleCleanup
{
    public static void Run(params Action[] cleanup)
    {
        foreach (var action in cleanup)
        {
            try { action(); } catch (Exception) { /* Continue releasing independently owned host resources. */ }
        }
    }
}
