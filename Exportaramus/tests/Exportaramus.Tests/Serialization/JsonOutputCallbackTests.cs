using System.Text.Json;
using Exportaramus.Shared.Serialization;

namespace Exportaramus.Tests.Serialization;

public sealed class JsonOutputCallbackTests
{
    [Fact]
    public void DisposedCallbackWriterCannotReturnSuccessfulEmptyOutput()
    {
        using var baseline = JsonDocument.Parse(JsonOutput.Build(writer =>
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ready", true);
            writer.WriteEndObject();
        }));
        Assert.True(baseline.RootElement.GetProperty("ready").GetBoolean());
        Assert.Throws<ObjectDisposedException>(() => JsonOutput.Build(writer => writer.Dispose()));
        Assert.Throws<ObjectDisposedException>(() => JsonOutput.Text(writer => writer.Dispose()));
        Assert.Throws<ObjectDisposedException>(() => JsonOutput.Fingerprint(writer => writer.Dispose()));
    }
}
