namespace EZDelivery.Bootstrap;

/// <summary>Teardown attempts every owned release even when one host operation fails.</summary>
public static class CleanupActions
{
    /// <summary>Attempts every cleanup action and reports all failures together.</summary>
    public static void Run(params Action[] actions)
    {
        List<Exception>? failures = null;
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("EZ Delivery cleanup failed.", failures);
    }
}
