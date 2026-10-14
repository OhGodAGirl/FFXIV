using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class FingerprintCanonicalVectorTests
{
    [Fact]
    public void PermanentUnlockFingerprintMatchesTheTypedOrdinalUtf8Vector()
    {
        var data = SnapshotFixture.Create();
        const string canonical = """
            {"capabilityState":"Supported","completeness":"Complete","ids":[1,{"id":1,"owned":true,"ownershipEvidence":{"facts":[{"dataType":"PermanentUnlock","sourceKey":"mounts","sourceStatus":"Fresh","state":"Present","subjectId":1}],"ruleCode":"DirectPermanentUnlock"}}],"items":[],"reasonCode":"None","sourceStatus":"Fresh"}
            """;
        Assert.Equal(Hash(canonical), CollectionFingerprint.Sources(data)["mounts"]);
    }

    [Fact]
    public void PresenceAndReportVectorsPreserveTheirIndependentTypedValues()
    {
        var data = SnapshotFixture.Create();
        data = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            ReportedEvidence = data.State.ReportedEvidence with { Items = [new(100, "Consumer", SnapshotFixture.Now)] }
        });
        const string presence = """
            {"capabilityState":"Supported","completeness":"Complete","ids":[],"items":[{"id":100,"quantity":2},{"id":1675,"quantity":2},{"id":2642,"quantity":2},{"id":2965,"quantity":2},{"id":3747,"quantity":2}],"reasonCode":"None","sourceStatus":"Fresh"}
            """;
        const string reports = """
            {"capabilityState":"Supported","completeness":"Complete","ids":[],"items":[{"id":100,"reporter":"Consumer"}],"reasonCode":"None","sourceStatus":"Fresh"}
            """;
        var fingerprints = CollectionFingerprint.Sources(data);
        Assert.Equal(Hash(presence), fingerprints["inventory"]);
        Assert.Equal(Hash(reports), fingerprints["reportedOwnership"]);
    }

    [Fact]
    public void QuestDataOnlyVectorIncludesProgressAndExcludesEvidenceHealth()
    {
        var data = SnapshotFixture.Create();
        const string canonical = """
            {"ids":[1,{"id":1,"owned":true,"ownershipEvidence":{"facts":[{"dataType":"PermanentUnlock","sourceKey":"quests","state":"Present","subjectId":1}],"ruleCode":"QuestCompletion"},"sequence":255,"status":"Completed"}],"items":[]}
            """;
        Assert.Equal(Hash(canonical), CollectionFingerprint.Sources(data, false)["quests"]);
    }

    [Fact]
    public void CombinedFingerprintUsesOrdinalObjectKeysRegardlessOfDictionaryConstruction()
    {
        var sources = CollectionFingerprint.Sources(SnapshotFixture.Create());
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in sources) sorted.Add(pair.Key, pair.Value);
        var expected = Hash(JsonSerializer.Serialize(sorted));
        Assert.Equal(expected, CollectionFingerprint.Combined(sources));
        Assert.Equal(expected, CollectionFingerprint.Combined(sources.Reverse().ToImmutableDictionary()));
    }

    private static string Hash(string canonical) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}
