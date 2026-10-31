using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Snapshots;

public sealed class StoredStateMalformedValueTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":false}")]
    public void MalformedCompactRootUsesTheRecoverableStoredDataFailure(string payload)
    {
        // Arrange.
        var session = Guid.NewGuid();

        // Act.
        var failure = Record.Exception(() => CollectionStateReader.Read(payload, 123, session, SnapshotFixture.Now));

        // Assert: startup restoration recognizes this as invalid saved data and can continue.
        Assert.IsType<InvalidDataException>(failure);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("\"invalidZ\"")]
    public void MalformedEvidenceTimestampUsesTheRecoverableStoredDataFailure(string timestamp)
    {
        // Arrange.
        var payload = "{\"contentId\":\"123\",\"items\":[{\"itemId\":1,\"reporter\":\"Consumer\",\"reportedAtUtc\":" + timestamp + "}]}";

        // Act.
        var failure = Record.Exception(() => CollectionStateReader.ReadReports(payload, 123));

        // Assert: malformed evidence cannot escape as a null-reference failure.
        Assert.IsType<InvalidDataException>(failure);
    }
}
