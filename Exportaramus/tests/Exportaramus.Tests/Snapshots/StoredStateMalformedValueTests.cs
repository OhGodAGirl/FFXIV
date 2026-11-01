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

        // Assert: reject invalid saved data without stopping startup.
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

        // Assert: reject malformed evidence without a null-reference error.
        Assert.IsType<InvalidDataException>(failure);
    }
}
