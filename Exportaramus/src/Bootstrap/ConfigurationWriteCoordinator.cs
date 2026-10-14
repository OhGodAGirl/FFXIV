namespace Exportaramus.Bootstrap;

public sealed class ConfigurationWriteCoordinator(Func<bool, Task> write) : IAsyncDisposable
{
    private Task? worker;
    private bool? pending;
    private bool disposed;
    public TaskStatus? WorkerStatus => worker?.Status;
    public void Request(bool value) { if (!disposed) pending = value; }
    public void Update()
    {
        if (disposed || worker is { IsCompleted: false }) return;
        if (worker is not null)
        {
            var completed = worker; worker = null;
            completed.GetAwaiter().GetResult();
        }
        var value = pending.GetValueOrDefault();
        if (!pending.HasValue) return;
        pending = null;
        worker = write(value);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { if (worker is not null) await worker.ConfigureAwait(false); } catch (Exception) { }
        var value = pending.GetValueOrDefault();
        if (pending.HasValue)
        {
            pending = null;
            try { await write(value).ConfigureAwait(false); } catch (Exception) { }
        }
    }
}
