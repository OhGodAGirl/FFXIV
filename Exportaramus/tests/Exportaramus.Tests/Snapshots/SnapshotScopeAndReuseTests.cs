using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class SnapshotScopeAndReuseTests
{
    [Fact]
    public void WrongCharacterRestoreIdentifiesTheScopeMismatch()
    {
        var state = SnapshotFixture.Create().State;
        var session = Guid.NewGuid();
        Assert.Equal(123ul, CollectionStatePolicy.Restore(state, 123, session, SnapshotFixture.Now).ContentId);
        var error = Assert.Throws<InvalidDataException>(() => CollectionStatePolicy.Restore(state, 456, session, SnapshotFixture.Now));
        Assert.Contains("Stored character", error.Message, StringComparison.Ordinal);
        Assert.Contains("active scope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyUnavailableCapabilitiesReuseThePreparedPayload()
    {
        var bundle = StateAndPublicationTests.Bundle(SnapshotFixture.Create(), 1);
        var prepared = SnapshotBundlePreparation.Prepare(bundle, null, false);
        Assert.NotEmpty(prepared.CapabilitiesJson);
        Assert.Equal(CapabilityDocumentWriter.Build(bundle.Data, false), prepared.UnavailableReportsCapabilitiesJson);
        Assert.Same(prepared.CapabilitiesJson, prepared.UnavailableReportsCapabilitiesJson);
    }
}
