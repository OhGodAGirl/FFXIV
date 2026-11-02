using System.Collections.Immutable;
using Exportaramus.Features.GuidedExport;
using Exportaramus.Features.Main;

namespace Exportaramus.Tests.Main;

public sealed class RetainerPresentationDefaultsTests
{
    [Fact]
    public void ANewCollectionViewHasNoRetainerOperationOrWarning()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new MainPresentationInput(1, MainStatus.Current, true, true, false, true, 0, now, now,
            "", "Ready", UiTone.Success, [], ImmutableDictionary<string, CollectionRowObservation>.Empty,
            new GuidedExportController().View);
        var view = MainPresenter.Prepare(input, now);
        Assert.True(view.CanSync);
        Assert.False(view.RetainersActive);
        Assert.Empty(view.RetainersMessage);
    }
}
