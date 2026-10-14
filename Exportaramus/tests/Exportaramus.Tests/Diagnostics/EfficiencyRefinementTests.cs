using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Shared.Serialization;

namespace Exportaramus.Tests.Diagnostics;

public sealed class EfficiencyRefinementTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(32768)]
    public void TextAndFingerprintConsumeExactlyTheOwnedCanonicalBytes(int length)
    {
        var value = new string('界', length) + "\"<&\n😀";
        Action<Utf8JsonWriter> write = writer =>
        {
            writer.WriteStartObject(); writer.WriteString("a", value);
            writer.WriteStartArray("z"); writer.WriteNumberValue(ulong.MaxValue); writer.WriteEndArray(); writer.WriteEndObject();
        };
        var bytes = JsonOutput.Build(write);
        var expected = "{\"a\":\"" + string.Concat(Enumerable.Repeat("\\u754C", length)) + "\\u0022\\u003C\\u0026\\n\\uD83D\\uDE00\",\"z\":[18446744073709551615]}";

        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        Assert.Equal(expected, JsonOutput.Text(write));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), JsonOutput.Fingerprint(write));
        var independentlyOwned = JsonOutput.Build(write);
        bytes[0] = 0;
        Assert.Equal((byte)'{', independentlyOwned[0]);
    }

    [Fact]
    public void EmptyAndFailedWritesPreserveConsumerBehavior()
    {
        Assert.Empty(JsonOutput.Build(_ => { })); Assert.Empty(JsonOutput.Text(_ => { }));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), JsonOutput.Fingerprint(_ => { }));
        var failure = new OperationCanceledException();
        Action<Utf8JsonWriter> cancel = writer => { writer.WriteStartObject(); throw failure; };
        Assert.Same(failure, Assert.Throws<OperationCanceledException>(() => JsonOutput.Build(cancel)));
        Assert.Same(failure, Assert.Throws<OperationCanceledException>(() => JsonOutput.Text(cancel)));
        Assert.Same(failure, Assert.Throws<OperationCanceledException>(() => JsonOutput.Fingerprint(cancel)));
        Action<Utf8JsonWriter> invalid = writer => { writer.WriteStartObject(); writer.WriteNumberValue(1); };
        Assert.Throws<InvalidOperationException>(() => JsonOutput.Build(invalid));
        Assert.Throws<InvalidOperationException>(() => JsonOutput.Text(invalid));
        Assert.Throws<InvalidOperationException>(() => JsonOutput.Fingerprint(invalid));
        Assert.Equal("[]", JsonOutput.Text(writer => { writer.WriteStartArray(); writer.WriteEndArray(); }));
    }

    [Fact]
    public void StableMetadataIsReusedWhileObservedRuntimeValuesChange()
    {
        var dependencies = ImmutableDictionary<string, string>.Empty.Add("Dalamud", "15.0.3.2");
        var metadata = new DiagnosticRuntimeMetadata("10.0.1", "Windows 10.0", dependencies);
        var first = metadata.Apply(new() { DalamudApiVersion = "15", FfxivClientVersion = "first", GameDataVersion = "one", ClientLanguage = "English" });
        var second = metadata.Apply(first with { FfxivClientVersion = "second", GameDataVersion = "two", ClientLanguage = "Japanese" });

        Assert.Same(dependencies, first.Dependencies); Assert.Same(first.Dependencies, second.Dependencies);
        Assert.Same(first.DotnetRuntimeVersion, second.DotnetRuntimeVersion); Assert.Equal("10.0.1", second.DotnetRuntimeVersion);
        Assert.Same(first.OsVersion, second.OsVersion); Assert.Equal("Windows 10.0", second.OsVersion);
        Assert.Equal("second", second.FfxivClientVersion); Assert.Equal("two", second.GameDataVersion); Assert.Equal("Japanese", second.ClientLanguage);
        Assert.Equal("15", second.DalamudApiVersion); Assert.Equal("first", first.FfxivClientVersion);
        var reloaded = new DiagnosticRuntimeMetadata("10.0.2", "Windows 11.0", dependencies.Add("Lumina", "7.6.0.0")).Apply(second);
        Assert.Equal("10.0.2", reloaded.DotnetRuntimeVersion); Assert.Equal("Windows 11.0", reloaded.OsVersion);
        Assert.Equal(2, reloaded.Dependencies.Count); Assert.Single(first.Dependencies);
    }

    [Fact]
    public async Task CapturesRemainBoundedAndIndependentAcrossConcurrentResetAndRecording()
    {
        var measurements = new DiagnosticMeasurements();
        for (var i = 0; i < 300; i++) measurements.Record("Draw", i, i * 2);
        measurements.Record("draw", 7, 14);
        var before = measurements.Snapshot();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Task.Run(async () =>
        {
            await start.Task;
            measurements.ResetMeasurements();
            for (var i = 0; i < 300; i++) measurements.Record("Draw", 1000 + i, 2000 + i);
        });
        try
        {
            start.SetResult();
            for (var i = 0; i < 100; i++)
            {
                var captured = measurements.Snapshot();
                if (captured.TryGetValue("Draw", out var metric))
                {
                    Assert.InRange(metric.SampleCount, 1, 256);
                    Assert.True(metric.DurationMilliseconds.Maximum <= 299 || metric.DurationMilliseconds.P50 >= 1000);
                    Assert.True(metric.DurationMilliseconds.P50 <= metric.DurationMilliseconds.P95);
                    Assert.True(metric.DurationMilliseconds.P95 <= metric.DurationMilliseconds.Maximum);
                }
            }
        }
        finally { start.TrySetResult(); await worker.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(256, before["Draw"].SampleCount);
        Assert.Equal(new DiagnosticPercentiles(171, 287, 299), before["Draw"].DurationMilliseconds);
        Assert.Equal(new DiagnosticPercentiles(342, 574, 598), before["Draw"].AllocatedBytes);
        Assert.Contains("draw", before.Keys);
        var after = Assert.Single(measurements.Snapshot());
        Assert.Equal("Draw", after.Key); Assert.Equal(new DiagnosticPercentiles(1171, 1287, 1299), after.Value.DurationMilliseconds);
    }

}
