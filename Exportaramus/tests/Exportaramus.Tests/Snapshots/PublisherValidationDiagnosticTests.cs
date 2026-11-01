using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PublisherValidationDiagnosticTests
{
    [Fact]
    public void InvalidRevisionIdentifiesTheCompactPayload()
    {
        var publisher = Publisher();
        var data = SnapshotFixture.Create();
        Assert.NotNull(publisher.Prepare(data, "Test", 1, null, false, false, default));
        var error = Assert.Throws<InvalidDataException>(() => publisher.Prepare(data, "Test", 0, null, false, false, default));
        Assert.Contains("Compact snapshot validation failed", error.Message, StringComparison.Ordinal);
        Assert.Contains("ContractBindingFailed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidDisplayNameIdentifiesTheLegacyRichPayload()
    {
        var publisher = Publisher();
        var data = SnapshotFixture.Create();
        Assert.NotNull(publisher.Prepare(data, "Test", 1, null, false, false, default));
        var error = Assert.Throws<InvalidDataException>(() => publisher.Prepare(data, "", 1, null, false, false, default));
        Assert.Contains("Rich snapshot validation failed", error.Message, StringComparison.Ordinal);
    }

    private static CollectionDataPublisher Publisher() => new(new AtomicSnapshotStorage(
        Path.Combine(AppContext.BaseDirectory, "publisher-validation-tests", Guid.NewGuid().ToString("N"))), null);
}
