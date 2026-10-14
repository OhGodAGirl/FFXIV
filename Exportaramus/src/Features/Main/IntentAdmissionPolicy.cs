namespace Exportaramus.Features.Main;

public static class IntentAdmissionPolicy
{
    public const int MainCapacity = 16;
    public const int DiagnosticsCapacity = 8;
    public static bool TryEnqueue<T>(Queue<T> queue, T intent, int capacity)
    {
        int pendingCount = queue.Count;
        if (pendingCount >= capacity) return false;
        queue.Enqueue(intent); return true;
    }
}
