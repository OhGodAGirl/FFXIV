namespace Exportaramus.ConsumerKit.Tests;

internal sealed class DeferredWorker
{
    private readonly Queue<(Action Action, CancellationToken Token, TaskCompletionSource Completion)> jobs = new();
    public int Count => jobs.Count;

    public Task Start(Action action, CancellationToken token)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Enqueue((action, token, completion));
        return completion.Task;
    }

    public void CompleteNext()
    {
        var job = jobs.Dequeue();
        if (job.Token.IsCancellationRequested) job.Completion.SetCanceled(job.Token);
        else
        {
            try
            {
                job.Action();
                job.Completion.SetResult();
            }
            catch (Exception exception) { job.Completion.SetException(exception); }
        }
    }
}
