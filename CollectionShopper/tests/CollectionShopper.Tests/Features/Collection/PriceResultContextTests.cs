using System.Net;
using System.Text;

namespace CollectionShopper.Tests;

public sealed class PriceResultContextTests
{
    [Fact]
    public async Task PendingRetryDoesNotPostToTheCallingContext()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new FfxivCollectClient(new HttpClient(new RetryHandler()),
            (_, token) => release.Task.WaitAsync(token));
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        Task<MarketPriceRefreshResult> operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = client.GetPriceResultsAsync("Example", [1], CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.False(operation.IsCompleted);
        release.SetResult();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([1u], result.FailedItemIds);
        Assert.Equal(0, context.Posts);
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        private int count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(++count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") });
    }

    [Fact]
    public async Task PendingHttpAndBatchContinuationsDoNotPostToTheCallingContext()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new FfxivCollectClient(new HttpClient(new DelayedHandler(release.Task, true)));
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = client.GetPriceResultsAsync("Example", new uint[] { 1 }, CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.False(operation.IsCompleted);
        release.SetResult();
        await operation;
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task PendingSemaphoreCancellationDoesNotPostToTheCallingContext()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var client = new FfxivCollectClient(new HttpClient(new DelayedHandler(release.Task, true)));
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = client.GetPriceResultsAsync("Example", Enumerable.Range(1, 501).Select(id => (uint)id).ToArray(), cancellation.Token);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        Assert.False(operation.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, context.Posts);
    }

    private sealed class ObservedContext : SynchronizationContext
    {
        private int posts;
        internal int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private sealed class DelayedHandler(Task release, bool prices) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await release.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(prices ? "{\"results\":[]}" : "{\"count\":0,\"results\":[]}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
