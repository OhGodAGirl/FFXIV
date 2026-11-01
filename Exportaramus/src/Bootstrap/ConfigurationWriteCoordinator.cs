namespace Exportaramus.Bootstrap;

public sealed class ConfigurationWriteCoordinator(Func<bool, Task> write, TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private Task? worker;
    private bool activeValue;
    private bool? pending;
    private DateTimeOffset retryAt;
    private bool disposed;
    public TaskStatus? WorkerStatus => worker?.Status;
    public void Request(bool value)
    {
        if (!disposed) pending = value;
    }
    public void Update()
    {
        if (disposed || worker is { IsCompleted: false }) return;
        if (worker is not null)
        {
            var completed = worker;
            worker = null;
            try { completed.GetAwaiter().GetResult(); }
            catch
            {
                pending ??= activeValue;
                retryAt = clock.GetUtcNow().AddSeconds(30);
                throw;
            }
        }
        if (!pending.HasValue || clock.GetUtcNow() < retryAt) return;
        activeValue = pending.Value;
        pending = null;
        try { worker = write(activeValue); }
        catch
        {
            pending ??= activeValue;
            retryAt = clock.GetUtcNow().AddSeconds(30);
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (worker is not null) await worker.ConfigureAwait(false);
        }
        catch (Exception) { pending ??= activeValue; }
        if (pending is { } value)
        {
            pending = null;
            try { await write(value).ConfigureAwait(false); }
            catch (Exception) { }
        }
    }
}
