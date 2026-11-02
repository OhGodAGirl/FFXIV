using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.Diagnostics;
using Exportaramus.Features.Main;
using Exportaramus.Tests.Snapshots;

namespace Exportaramus.Tests.Main;

public sealed class ActiveDiagnosticProjectionTests
{
    [Fact]
    public void RunningDiagnosticsRetainTheClientBuildAndEveryRegistration()
    {
        var data = SnapshotFixture.Create();
        var bundle = SnapshotBundlePreparation.Prepare(StateAndPublicationTests.Bundle(data, 1), null, true);
        var input = MainSnapshotPresenter.DiagnosticInput(bundle, "1.0.0", "10.0.0", data.State.ProviderSessionId,
            false, false, 0, new(0, 0, 0), new(), SnapshotFixture.Now);
        var report = DiagnosticReportPreparer.Prepare(input, SnapshotFixture.Now);
        Assert.Equal(data.Catalog.GameDataVersion, report.Runtime.GameDataVersion);
        Assert.Equal(data.Catalog.GameDataVersion, report.Runtime.FfxivClientVersion);
        Assert.Equal("Running", report.Lifecycle.State);
        Assert.Equal(new DiagnosticRegistrations(true, true, true, true, true, true, true, true), report.Lifecycle.Registrations);
    }
}
