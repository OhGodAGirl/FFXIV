using System.Runtime.CompilerServices;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class ClientCleanupBoundaryTests
{
    private sealed class RecordingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    [Fact]
    public async Task DisposalDoesNotDispatchCleanupToTheCallingContext()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new FakeTransport();
        var client = new CollectionDataIpcClient((_, _) => transport, (_, _) => completion.Task);
        _ = client.TickAsync(ContractFixture.Character, ContractFixture.Now);
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        Task cleanup;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            cleanup = client.DisposeAsync().AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        completion.SetResult();
        await cleanup;
        Assert.Equal(0, context.Posts);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(CollectionDataAvailability.Disposed, client.View.Availability);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CollectionDataIpcClient Client, WeakReference Payload) QueueWithdrawal()
    {
        var transport = new FakeTransport();
        var client = new CollectionDataIpcClient((_, _) => transport);
        var payload = new string(' ', 4096);
        var reference = new WeakReference(payload);
        client.Withdrawn(payload);
        return (client, reference);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public async Task DisposalReleasesQueuedWithdrawalPayloads()
    {
        var (client, payload) = QueueWithdrawal();
        Collect();
        Assert.True(payload.IsAlive);
        await client.DisposeAsync();
        Collect();
        Assert.False(payload.IsAlive);
        GC.KeepAlive(client);
    }
}
