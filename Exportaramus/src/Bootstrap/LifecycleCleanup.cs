namespace Exportaramus.Bootstrap;

public static class LifecycleCleanup
{
    /// <summary>Start all shutdown tasks and wait for them before releasing host resources.</summary>
    /// <remarks>Shutdown tasks must not wait for the framework thread.</remarks>
    public static void Complete(params Func<ValueTask>[] shutdown)
    {
        var workers = new List<Task>(shutdown.Length);
        foreach (var operation in shutdown)
        {
            try
            {
                workers.Add(operation().AsTask());
            }
            catch (Exception) { /* A startup failure must not skip another owned worker. */ }
        }
        try
        {
            Task.WhenAll(workers).GetAwaiter().GetResult();
        }
        catch (Exception) { /* WhenAll observes every failure after every worker has finished. */ }
    }

    public static void Run(params Action[] cleanup)
    {
        foreach (var action in cleanup)
        {
            try
            {
                action();
            }
            catch (Exception) { /* Continue releasing independently owned host resources. */ }
        }
    }
}
