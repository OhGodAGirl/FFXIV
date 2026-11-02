namespace SpringCleaning.Tests;

public sealed class NativeWindowFocusPolicyTests
{
    [Theory]
    [InlineData(100, 100, 0, "Inventory", 0, 0, true)]
    [InlineData(100, 0, 100, "Inventory", 0, 0, true)]
    [InlineData(100, 0, 0, "Cabinet", 0, 0, true)]
    [InlineData(100, 0, 0, "Inventory", 100, 0, true)]
    [InlineData(100, 0, 0, "Inventory", 0, 100, true)]
    [InlineData(100, 200, 200, "Inventory", 300, 300, false)]
    [InlineData(0, 0, 0, "Cabinet", 0, 0, false)]
    public void AnchorFocusAcceptsLifecycleAndLiveManagerEvidence(
        long anchorAddress,
        long trackedAddress,
        long trackedBaseAddress,
        string trackedName,
        long rawFocusedAddress,
        long resolvedFocusedAddress,
        bool expected)
    {
        Assert.Equal(
            expected,
            NativeWindowFocusPolicy.IsAnchorFocused(
                (nint)anchorAddress,
                "Cabinet",
                (nint)trackedAddress,
                (nint)trackedBaseAddress,
                trackedName,
                (nint)rawFocusedAddress,
                (nint)resolvedFocusedAddress));
    }
}
