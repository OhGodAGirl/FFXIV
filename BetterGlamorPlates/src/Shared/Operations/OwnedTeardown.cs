namespace BetterGlamorPlates.Shared.Operations;

/// <summary>
/// Releases successful registrations in reverse order and remains idempotent
/// after partial initialization.
/// </summary>
internal sealed class OwnedTeardown : IDisposable
{
    private readonly List<Action> releaseActions = [];
    private readonly Action<Exception> reportError;
    private bool disposed;

    internal OwnedTeardown(Action<Exception> reportError)
    {
        this.reportError = reportError;
    }

    internal void Add(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        ObjectDisposedException.ThrowIf(disposed, this);

        releaseActions.Add(release);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            foreach (var release in releaseActions.AsEnumerable().Reverse())
            {
                try
                {
                    release();
                }
                catch (Exception exception)
                {
                    reportError(exception);
                }
            }

            releaseActions.Clear();
        }
    }
}
