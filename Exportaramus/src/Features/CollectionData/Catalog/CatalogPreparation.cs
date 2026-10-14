namespace Exportaramus.Features.CollectionData.Catalog;

// Framework-owned lifecycle; only immutable projections enter the worker.
public sealed class CatalogPreparation(Func<ICatalogProjectionBuilder> createBuilder,
    Func<CatalogProjection, CancellationToken, Task<CollectionCatalog>> finalize) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private ICatalogProjectionBuilder? builder;
    private Task<CollectionCatalog>? finalizer;
    private bool disposed;
    public CollectionCatalog? Catalog { get; private set; }
    public Task ShutdownTask { get; private set; } = Task.CompletedTask;
    public TaskStatus? WorkerStatus => finalizer?.Status;

    public bool Prepare(string language, string version)
    {
        if (disposed) return false;
        var cached = Catalog;
        if (cached is not null && cached.ClientLanguage == language && cached.GameDataVersion == version) return true;
        Catalog = null;
        var running = finalizer;
        if (running is not null)
        {
            if (!running.IsCompleted) return false;
            finalizer = null;
            var prepared = running.GetAwaiter().GetResult();
            if (prepared.ClientLanguage == language && prepared.GameDataVersion == version) { Catalog = prepared; return true; }
        }
        builder ??= createBuilder();
        try { if (!builder.Advance(2048)) return false; }
        catch { builder.Dispose(); builder = null; throw; }
        var projection = builder.Projection!;
        builder.Dispose(); builder = null;
        finalizer = finalize(projection, lifetime.Token);
        return false;
    }

    public static Task<CollectionCatalog> FinalizeInBackground(CatalogProjection projection, CancellationToken token) =>
        Task.Run(() => FinalizeProjection(projection, token, CollectionCatalogBuilderPolicy.Build), token);

    public static CollectionCatalog FinalizeProjection(CatalogProjection projection, CancellationToken token,
        Func<CatalogProjection, CollectionCatalog> build)
    {
        token.ThrowIfCancellationRequested();
        var result = build(projection);
        token.ThrowIfCancellationRequested();
        return result;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        lifetime.Cancel(); builder?.Dispose(); builder = null;
        var task = finalizer; finalizer = null; Catalog = null;
        ShutdownTask = ObserveAsync(task, lifetime);
    }
    private static async Task ObserveAsync(Task? task, CancellationTokenSource cancellation)
    {
        try { if (task is not null) await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Teardown observes every owned failure before releasing its token. */ }
        finally { cancellation.Dispose(); }
    }
}
