namespace CollectionShopper;

/// <summary>Publishes optional refresh results only while their owning session remains current.</summary>
internal static class RefreshPublication
{
    internal static async Task RunAsync<T>(Func<CancellationToken, Task<T>> fetch,
        Func<Action, CancellationToken, Task> dispatch, Func<bool> canPublish, Action<T> publish,
        Action<Exception> reportFailure, Action publishFailure, CancellationToken token)
    {
        try
        {
            var result = await fetch(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await dispatch(() =>
            {
                if (canPublish())
                    publish(result);
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            reportFailure(ex);
            await dispatch(() =>
            {
                if (canPublish())
                    publishFailure();
            }, token).ConfigureAwait(false);
        }
    }
}
