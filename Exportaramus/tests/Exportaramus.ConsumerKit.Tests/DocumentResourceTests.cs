using System.Buffers;
using System.Diagnostics.Tracing;
using Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.ConsumerKit.Tests;

public sealed class DocumentResourceTests
{
    [Theory]
    [InlineData("{\"a\":1,\"a\":2}", false, CollectionDataValidationCode.InvalidJson)]
    [InlineData("{\"schemaVersion\":\"1..2\"}", true, CollectionDataValidationCode.MalformedSchemaVersion)]
    public void RejectedDocumentsReturnTheirRentedBuffers(string payload, bool versioned, CollectionDataValidationCode code)
    {
        // Initialize JSON metadata before observing only this synchronous operation.
        using (var warmup = CollectionDataSemanticValidator.ParseDocument("{}", 4096, false).Value) { }
        using var observer = new BufferObserver();
        observer.Begin();
        var result = CollectionDataSemanticValidator.ParseDocument(payload, 4096, versioned);
        observer.End();
        Assert.False(result.Success);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Value);
        observer.AssertBalanced();
    }

    private sealed class BufferObserver : EventListener
    {
        private readonly (int Buffer, bool Rented)[] events = new (int, bool)[128];
        private int owner;
        private int count;
        private bool active;
        private bool overflow;

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Buffers.ArrayPoolEventSource") EnableEvents(source, EventLevel.Verbose);
        }

        public void Begin()
        {
            var warm = ArrayPool<byte>.Shared.Rent(1);
            ArrayPool<byte>.Shared.Return(warm);
            owner = Environment.CurrentManagedThreadId;
            active = true;
        }

        public void End() => active = false;

        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            if (!active || Environment.CurrentManagedThreadId != owner || args.Payload is null
                || args.EventName is not ("BufferRented" or "BufferReturned")) return;
            if (count == events.Length) { overflow = true; return; }
            events[count++] = ((int)args.Payload[0]!, args.EventName == "BufferRented");
        }

        public void AssertBalanced()
        {
            Assert.False(overflow);
            Assert.Contains(events.Take(count), e => e.Rented);
            var outstanding = new Dictionary<int, int>();
            foreach (var item in events.Take(count))
            {
                outstanding.TryGetValue(item.Buffer, out var balance);
                outstanding[item.Buffer] = balance + (item.Rented ? 1 : -1);
            }
            Assert.All(outstanding, item => Assert.Equal(0, item.Value));
        }
    }
}
