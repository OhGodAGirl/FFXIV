using System.Text.Json;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;

namespace Exportaramus.Tests.Snapshots;

public sealed class ChangedNoticeFailureRecoveryTests
{
    [Fact]
    public void RetainedNoticeIncludesHealthWhenObservedSourceEnumerationFails()
    {
        var data = SnapshotFixture.Create();
        var before = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        var changed = CollectionDataAssembler.Assemble(data.Catalog, data.State with
        {
            Unlocks = data.State.Unlocks.SetItem("mounts", data.State.Unlocks["mounts"] with
            {
                UnlockedIds = []
            })
        });
        var after = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(changed, 2), before, true);
        var notices = new ChangedNoticeCoalescer();
        Assert.Throws<IOException>(() => notices.Add(after, before, FailingSources()));
        using var notice = JsonDocument.Parse(Assert.IsType<string>(notices.Take()));
        Assert.Contains("mounts", notice.RootElement.GetProperty("changedSources").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("SourceHealth", notice.RootElement.GetProperty("changedDataTypes").EnumerateArray().Select(item => item.GetString()));
        Assert.Null(notices.Take());
    }

    private static IEnumerable<string> FailingSources()
    {
        yield return "inventory";
        throw new IOException("Observed source enumeration failed.");
    }
}
