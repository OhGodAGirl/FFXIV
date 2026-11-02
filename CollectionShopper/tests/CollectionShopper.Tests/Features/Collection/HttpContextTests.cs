using System.Net;
using System.Text;

namespace CollectionShopper.Tests;

public sealed class HttpContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingHttpAndBatchContinuationsDoNotPostToTheCallingContext(bool prices)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new FfxivCollectClient(new HttpClient(new DelayedHandler(release.Task, prices)));
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = prices
                ? client.GetPricesAsync("Example", new uint[] { 1 }, CancellationToken.None)
                : client.GetCollectionsAsync(CancellationToken.None);
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
            operation = client.GetPricesAsync("Example", Enumerable.Range(1, 501).Select(id => (uint)id).ToArray(), cancellation.Token);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PendingContentAndJsonReadsDoNotPostToTheCallingContext(bool prices, bool delayJsonRead)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new FfxivCollectClient(new HttpClient(new ContentHandler(release.Task, prices, delayJsonRead)));
        var context = new ObservedContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = prices
                ? client.GetPricesAsync("Example", new uint[] { 1 }, CancellationToken.None)
                : client.GetCollectionsAsync(CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        Assert.False(operation.IsCompleted);
        release.SetResult();
        await operation;
        Assert.Equal(0, context.Posts);
    }

    private sealed class ContentHandler(Task release, bool prices, bool delayJsonRead) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetBytes(prices ? "{\"results\":[]}" : "{\"count\":0,\"results\":[]}");
            var content = new PreparedStreamContent(payload, release, delayJsonRead);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class PreparedStreamContent : HttpContent
    {
        private readonly byte[] payload;
        private readonly Task release;
        private readonly bool delayJsonRead;

        internal PreparedStreamContent(byte[] payload, Task release, bool delayJsonRead)
        {
            this.payload = payload;
            this.release = release;
            this.delayJsonRead = delayJsonRead;
            // HttpContent may already own a stream task before HttpClient buffers its body.
            PreparedStream = ReadAsStreamAsync();
        }

        internal Task<Stream> PreparedStream { get; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            stream.Write(payload);
            return Task.CompletedTask;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = payload.Length;
            return true;
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            if (delayJsonRead)
                return new DelayedReadStream(payload, release);
            await release.ConfigureAwait(false);
            return new MemoryStream(payload);
        }
    }

    private sealed class DelayedReadStream(byte[] payload, Task release) : MemoryStream(payload)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await release.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
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
