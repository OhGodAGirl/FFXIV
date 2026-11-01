using System.Text.Json.Nodes;
using Exportaramus.Features.CollectionData.Publication;

namespace Exportaramus.Tests.Snapshots;

public sealed class StoredEvidenceFailureTests
{
    [Theory]
    [InlineData("size", "Stored evidence exceeds its size limit.")]
    [InlineData("scope", "Stored evidence has the wrong scope.")]
    [InlineData("count", "Stored evidence exceeds its record limit.")]
    [InlineData("reporter", "Stored evidence is invalid.")]
    [InlineData("timestamp", "Stored evidence timestamp is invalid.")]
    public void InvalidStoredEvidenceRetainsTheFailureReason(string kind, string expected)
    {
        const string valid = "{\"contentId\":\"123\",\"items\":[{\"itemId\":1,\"reporter\":\"Reporter\",\"reportedAtUtc\":\"2026-09-06T12:00:00Z\"}]}";
        Assert.Single(CollectionStateReader.ReadReports(valid, 123));
        var document = JsonNode.Parse(valid)!;
        switch (kind)
        {
            case "scope": document["contentId"] = "456"; break;
            case "count": document["items"] = new JsonArray(Enumerable.Range(0, 10001).Select(_ => (JsonNode)new JsonObject()).ToArray()); break;
            case "reporter": document["items"]![0]!["reporter"] = ""; break;
            case "timestamp": document["items"]![0]!["reportedAtUtc"] = "not-a-timestamp"; break;
        }
        var json = kind == "size" ? new string(' ', 4194305) : document.ToJsonString();
        var error = Assert.Throws<InvalidDataException>(() => CollectionStateReader.ReadReports(json, 123));
        Assert.Equal(expected, error.Message);
    }
}
