using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Main;

public sealed class MissingCollectionGuidanceTests
{
    [Fact]
    public void ACollectionWithoutAnObservationExplainsItsMissingCount()
    {
        var definition = ("mounts", "Mounts", "Mount");
        var observed = MainPresenter.Row(definition, new(1, false, null, RowEvidenceKind.PermanentUnlock, Detail: "Captured observation."));
        Assert.Equal("1", observed.Count);
        Assert.Equal("Captured observation.", observed.Detail);
        var missing = MainPresenter.Row(definition, null);
        Assert.Equal("\u2014", missing.Count);
        Assert.Contains("captured", missing.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(observed.Detail, missing.Detail);
    }
}
