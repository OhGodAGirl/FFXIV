using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class IncompleteCollectionGuidanceTests
{
    [Fact]
    public void PartialOutfitDetailsDescribeALowerBoundInsteadOfAnExactTotal()
    {
        var data = SnapshotFixture.Create();
        var bundle = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        Assert.NotEqual("—", View(bundle).Rows.Single(row => row.Key == "outfits").Count);
        var partial = bundle with
        {
            Data = bundle.Data with
            {
                Collections = bundle.Data.Collections.Remove("outfits")
            },
            ConfirmedPartialCounts = ImmutableDictionary<string, int>.Empty.Add("outfits", 3)
        };
        var row = View(partial).Rows.Single(row => row.Key == "outfits");
        Assert.Equal("3+", row.Count);
        Assert.StartsWith("At least 3", row.Detail);
        Assert.Contains("not a complete total", row.Detail);
    }

    [Fact]
    public void ACollectionWithoutAnySourceObservationExplainsHowToCaptureIt()
    {
        var data = SnapshotFixture.Create();
        var bundle = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        Assert.NotEqual("—", View(bundle).Rows.Single(row => row.Key == "mounts").Count);
        var missing = bundle with
        {
            Data = bundle.Data with
            {
                Collections = bundle.Data.Collections.Remove("mounts"),
                SourceHealth = bundle.Data.SourceHealth.Remove("mounts")
            }
        };
        var row = View(missing).Rows.Single(row => row.Key == "mounts");
        Assert.Equal("Waiting", row.Count);
        Assert.Contains("Sync Now", row.Detail);
        Assert.Contains("window", row.Detail);
    }

    private static MainViewState View(AcceptedSnapshotBundle bundle) =>
        MainSnapshotPresenter.Prepare(bundle, true, null, false, false, new GuidedExportController().View, SnapshotFixture.Now, true);
}
