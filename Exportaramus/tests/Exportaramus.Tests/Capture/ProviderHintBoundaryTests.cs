using Exportaramus.Features.CollectionData.Capture;

namespace Exportaramus.Tests.Capture;

public sealed class ProviderHintBoundaryTests
{
    [Fact]
    public void OverflowKeepsAlreadyObservedContainerHints()
    {
        using var hints = new PromptCaptureHintBuffer();
        var observed = new List<string>();
        hints.Inventory(Enumerable.Repeat("Inventory1", 256));
        hints.Drain(observed.Add);
        Assert.Equal(new[] { "inventory" }, observed);

        observed.Clear();
        hints.Inventory(Enumerable.Repeat("Inventory1", 257));
        hints.Drain(observed.Add);
        Assert.Contains("inventory", observed);
        Assert.Equal(24, observed.Distinct(StringComparer.Ordinal).Count());
    }
}
