using BetterGlamorPlates.Features.Main;
using BetterGlamorPlates.Shared.Operations;

namespace BetterGlamorPlates.Bootstrap;

/// <summary>Owns queued command context, duplicate native-write prevention and cancellation.</summary>
internal sealed class CommandDispatcher(
    OwnedOperationRegistry operations,
    Func<Action, CancellationToken, Task> schedule,
    Func<ulong> currentCharacter,
    Action<MainCommand> execute,
    Action<bool> setLoadQueued,
    Action reportLoadCancelled,
    Func<CancellationTokenSource> createCancellation) : IDisposable
{
    private int loadQueued;
    private CancellationTokenSource? loadCancellation;
    private bool disposed;

    internal void CancelLoad() => loadCancellation?.Cancel();

    public void Dispose()
    {
        disposed = true;
        // Active work owns disposal in its finally block after cancellation has been observed.
        CancelLoad();
    }

    internal void Queue(MainCommand command)
    {
        if (disposed) return;
        var isLoad = command is MainCommand.LoadActive or MainCommand.ClearOutfitItems;
        if (isLoad && Interlocked.CompareExchange(ref loadQueued, 1, 0) != 0)
            return;
        if (isLoad)
        {
            loadCancellation = createCancellation();
            setLoadQueued(true);
        }
        var requestedCharacter = currentCharacter();
        if (!operations.TryStart(token => ExecuteAsync(command, requestedCharacter, isLoad, token)) && isLoad)
            CompleteLoad();
    }

    private async Task ExecuteAsync(MainCommand command, ulong requestedCharacter, bool isLoad, CancellationToken shutdown)
    {
        using var linked = isLoad
            ? CancellationTokenSource.CreateLinkedTokenSource(shutdown, loadCancellation!.Token) : null;
        var token = linked?.Token ?? shutdown;
        try
        {
            await schedule(() =>
            {
                if (!token.IsCancellationRequested && requestedCharacter != 0 && requestedCharacter == currentCharacter())
                    execute(command);
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (isLoad && !shutdown.IsCancellationRequested)
                await schedule(reportLoadCancelled, shutdown);
        }
        finally
        {
            if (isLoad)
                CompleteLoad();
        }
    }

    private void CompleteLoad()
    {
        setLoadQueued(false);
        loadCancellation!.Dispose();
        loadCancellation = null;
        Interlocked.Exchange(ref loadQueued, 0);
    }
}
