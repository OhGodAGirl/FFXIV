using System.Text;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Snapshots;
using Xunit.Abstractions;

namespace Exportaramus.Tests.Snapshots;

[Collection(Exportaramus.Tests.Performance.OfflinePerformanceCollection.Name)]
public sealed class AtomicStorageContractBoundaryTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(AppContext.BaseDirectory, "atomic-contract-fixtures", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-06T00:00:00Z");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvertisedOversizeFileIsRejectedWithoutReadingItsPayload(bool evidence)
    {
        var stream = new OversizeStream();
        var storage = new AtomicSnapshotStorage(root, new Files { Read = _ => stream });
        var path = evidence ? storage.EvidencePath(1) : storage.CharacterPath(1);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{}");

        await Assert.ThrowsAsync<InvalidDataException>(() => storage.ReadAsync(1, evidence, default));

        Assert.Equal(0, stream.ReadCalls);
        Assert.True(stream.WasDisposed);
        Assert.Equal("{}", await File.ReadAllTextAsync(path));
        Assert.Equal("Failed", storage.Diagnostic.LastRead.Status);
    }

    [Theory]
    [InlineData(0, 0xd800)]
    [InlineData(1, 0xd800)]
    [InlineData(2, 0xd800)]
    [InlineData(0, 0xdc00)]
    [InlineData(1, 0xdc00)]
    [InlineData(2, 0xdc00)]
    public async Task MalformedUtf16CannotReplaceAnyAcceptedSnapshot(int target, int surrogate)
    {
        var storage = new AtomicSnapshotStorage(root);
        await storage.CommitAsync(1, "{\"compact\":1}", "{\"rich\":1}", "{\"reports\":1}", true, true, Now, default);
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var inputs = new[] { "{\"compact\":2}", "{\"rich\":2}", "{\"reports\":2}" };
        inputs[target] = "{\"label\":\"" + (char)surrogate + "\"}";

        await Assert.ThrowsAsync<EncoderFallbackException>(() => storage.CommitAsync(1, inputs[0], inputs[1], inputs[2], true, true, Now.AddSeconds(1), default));

        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var file in before) Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key));
        Assert.Equal("Failed", storage.Diagnostic.LastWrite.Status);
        Assert.Equal("None", storage.RecoveryState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageAsyncTelemetryIncludesWorkOnAnotherThread(bool writing)
    {
        const int payloadBytes = 1024 * 1024;
        var gate = new TaskCompletionSource();
        var measurements = new DiagnosticMeasurements();
        var files = new Files { Gate = gate.Task };
        var storage = new AtomicSnapshotStorage(root, files, measurements);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.CharacterPath(1))!);
        File.WriteAllText(storage.CharacterPath(1), "{}");
        byte[]? payload = null;
        Exception? failure = null;
        double allocated = 0;
        var allocator = new Thread(() => payload = new byte[payloadBytes]);
        var owner = new Thread(() =>
        {
            Task? operation = null;
            try
            {
                operation = writing
                    ? storage.CommitAsync(1, "{\"revision\":2}", null, null, false, false, Now, default)
                    : storage.ReadAsync(1, false, default);
                Assert.False(operation.IsCompleted, "The storage operation must suspend at the controlled real I/O boundary.");
                allocator.Start();
                Assert.True(allocator.Join(TimeSpan.FromSeconds(3)));
                gate.SetResult();
                Assert.True(operation.Wait(TimeSpan.FromSeconds(3)), "Storage must complete after its I/O boundary is released.");
                operation.GetAwaiter().GetResult();
                var metric = measurements.Snapshot()[writing ? "FileWrite" : "FileRead"];
                Assert.Equal(1, metric.SampleCount);
                allocated = metric.AllocatedBytes.Maximum;
                Assert.True(allocated >= payloadBytes, $"Storage telemetry lost other-thread work: {allocated} < {payloadBytes} bytes.");
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                gate.TrySetResult();
                if (operation is not null)
                {
                    try { if (!operation.Wait(TimeSpan.FromSeconds(3))) failure ??= new TimeoutException("Storage cleanup did not complete."); }
                    catch (Exception exception) { failure ??= exception; }
                }
                if (allocator.ThreadState != System.Threading.ThreadState.Unstarted && !allocator.Join(TimeSpan.FromSeconds(3)))
                    failure ??= new TimeoutException("Allocation worker did not stop.");
            }
        });
        owner.Start();
        Assert.True(owner.Join(TimeSpan.FromSeconds(12)), "The bounded storage owner did not stop.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal(writing ? "{\"revision\":2}" : "{}", File.ReadAllText(storage.CharacterPath(1)));
        output.WriteLine($"Known retained allocation on another thread: {payloadBytes}; actual {(writing ? "FileWrite" : "FileRead")} sample: {allocated} bytes.");
        GC.KeepAlive(payload);
    }

    private sealed class Files : IAtomicFileOperations
    {
        public Func<string, Stream>? Read { get; init; }
        public Task? Gate { get; init; }
        public Stream OpenRead(string path) => Read?.Invoke(path) ?? new GatedReadStream(File.ReadAllBytes(path), Gate!);
        public FileStream OpenWrite(string path, FileMode mode, int bufferSize, FileOptions options) =>
            Gate is not null && options.HasFlag(FileOptions.Asynchronous)
                ? new GatedWriteStream(path, mode, bufferSize, options, Gate)
                : new FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string? backup) => File.Replace(source, destination, backup);
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class OversizeStream() : MemoryStream("{}"u8.ToArray())
    {
        public override long Length => 4194305;
        public int ReadCalls { get; private set; }
        public bool WasDisposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { ReadCalls++; return base.ReadAsync(buffer, cancellationToken); }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed class GatedReadStream(byte[] bytes, Task gate) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await gate.ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); return Read(buffer.Span); }
    }

    private sealed class GatedWriteStream(string path, FileMode mode, int bufferSize, FileOptions options, Task gate)
        : FileStream(path, mode, FileAccess.Write, FileShare.None, bufferSize, options)
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { await gate.ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); Write(buffer.Span); }
        public override Task FlushAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
