namespace SpringCleaning;

/// <summary>
/// Owns runtime registrations whose teardown must run once in reverse order.
/// </summary>
internal sealed class RuntimeRegistrationSet
{
    private readonly List<Action> teardowns = [];
    private bool disposed;

    public int Count => teardowns.Count;

    public void Add(Action register, Action teardown)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(teardown);
        ObjectDisposedException.ThrowIf(disposed, this);

        register();
        teardowns.Add(teardown);
    }

    public IReadOnlyList<Exception> Dispose()
    {
        if (disposed)
            return Array.Empty<Exception>();

        disposed = true;
        List<Exception> errors = [];
        foreach (var teardown in teardowns.AsEnumerable().Reverse())
        {
            try
            {
                teardown();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        teardowns.Clear();
        return errors;
    }
}
