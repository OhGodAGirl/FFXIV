using System.Security.Cryptography;
using System.Text;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Snapshots;

public sealed class StoredSnapshotDiagnosticTests
{
    [Theory]
    [InlineData("root")]
    [InlineData("1.0")]
    [InlineData("1.1")]
    public void InvalidStoredSnapshotExplainsTheRejectedState(string version)
    {
        var data = SnapshotFixture.Create();
        string Serialize(long revision) => version == "1.1"
            ? CollectionDataSerializer.CompactV11(data, revision)
            : CollectionDataSerializer.Compact(data, revision);
        Assert.NotNull(CollectionStateReader.Read(Serialize(1), 123, data.State.ProviderSessionId, SnapshotFixture.Now));
        var invalid = version == "root" ? "[]" : Serialize(0);
        var error = Assert.Throws<InvalidDataException>(() => CollectionStateReader.Read(invalid, 123, data.State.ProviderSessionId, SnapshotFixture.Now));
        Assert.Contains("Stored collection state", error.Message, StringComparison.Ordinal);
        Assert.Contains("invalid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateStoredEvidenceExplainsTheConflictingProperties()
    {
        Assert.Empty(CollectionStateReader.ReadReports("{\"contentId\":\"123\",\"items\":[]}", 123));
        var error = Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(
            "{\"contentId\":\"123\",\"contentId\":\"123\",\"items\":[]}", 123));
        Assert.Contains("duplicate properties", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DresserFingerprintRetainsItsNamedPhysicalEntryField()
    {
        const string canonical = """
            {"capabilityState":"Supported","completeness":"Complete","ids":[],"items":[{"id":100},{"id":1675},{"id":2642},{"id":2965},{"id":3747}],"dresserEntries":null,"reasonCode":"None","sourceStatus":"Fresh"}
            """;
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.Equal(expected, CollectionFingerprint.Sources(SnapshotFixture.Create())["glamourDresser"]);
    }
}
