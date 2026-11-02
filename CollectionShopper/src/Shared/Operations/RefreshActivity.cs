namespace CollectionShopper;

/// <summary>
/// Owns the visible lifetime of a refresh independently of the work scheduler.
/// Every accepted refresh must pass through <see cref="RunAsync"/>, which
/// releases the activity flag for success, failure, and cancellation.
/// </summary>
internal sealed class RefreshActivity
{
    private int active;

    internal bool IsActive => Volatile.Read(ref active) != 0;

    internal bool TryBegin()
        => Interlocked.CompareExchange(ref active, 1, 0) == 0;

    internal void AbortBegin()
        => Volatile.Write(ref active, 0);

    internal async Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            // Stryker disable once Boolean: synchronization-context capture is not part of this helper's observable contract.
            await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref active, 0);
        }
    }

}
