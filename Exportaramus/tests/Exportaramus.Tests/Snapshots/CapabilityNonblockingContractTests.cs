using System.Diagnostics.Tracing;
using Exportaramus.Features.CollectionData.Capture;
using Exportaramus.Features.CollectionData.Catalog;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class CapabilityNonblockingContractTests
{
    [Fact]
    public void PendingCapabilitiesNeverSynchronouslyWaitOnTheOwnedWorker()
    {
        using var hold = new Fixture();
        hold.Update();
        using var listener = new WaitListener(hold.completion.Task.Id, hold.Complete);
        hold.Update();
        Assert.False(listener.WaitObserved);
        Assert.False(hold.completion.Task.IsCompleted);
        hold.Complete();
        hold.Update();
        Assert.NotEmpty(hold.Controller.Publication.GetCapabilities());
    }

    private sealed class WaitListener : EventListener
    {
        private readonly int target;
        private readonly Action release;
        public bool WaitObserved
        {
            get; private set;
        }
        public WaitListener(int target, Action release)
        {
            this.target = target;
            this.release = release;
        }
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Threading.Tasks.TplEventSource")
                EnableEvents(source, EventLevel.Verbose, EventKeywords.All);
        }
        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (args.EventName != "TaskWaitBegin" || args.PayloadNames is null || args.Payload is null) return;
            for (var i = 0; i < args.PayloadNames.Count; i++)
                if (args.PayloadNames[i] == "TaskID" && args.Payload[i] is int id && id == target)
                {
                    WaitObserved = true;
                    release();
                }
        }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(AppContext.BaseDirectory, "capability-worker", Guid.NewGuid().ToString("N"));
        internal readonly TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Func<string>? work;
        public int QueueCount
        {
            get; private set;
        }
        public SnapshotController Controller
        {
            get;
        }
        public Fixture()
        {
            Controller = new(new Port(), new(root), null, prepareCapabilities: factory =>
            {
                QueueCount++;
                work = factory;
                return completion.Task;
            });
        }
        public void Update() => Controller.Update(0, "", true, false, 0, SnapshotFixture.Now);
        public void Complete()
        {
            if (!completion.Task.IsCompleted)
                completion.SetResult(work!());
        }
        public void Fail()
        {
            if (!completion.Task.IsCompleted)
                completion.SetException(new IOException("Controlled owned worker failure."));
        }
        public void Dispose()
        {
            Complete();
            Controller.Dispose();
            Controller.ShutdownTask.GetAwaiter().GetResult();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Port : ISnapshotCapturePort
    {
        public CollectionCatalog? Catalog { get; } = SnapshotFixture.Create().Catalog;
        public bool PrepareCatalog() => true;
        public void Begin(bool full, ulong id, long generation, DateTimeOffset now) => throw new InvalidOperationException();
        public bool Advance() => throw new InvalidOperationException();
        public RawCaptureBatch? TakeResult() => throw new InvalidOperationException();
        public void ResetSession()
        {
        }
        public void Dispose()
        {
        }
    }
}


