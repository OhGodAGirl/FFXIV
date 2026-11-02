namespace SpringCleaning.Tests.Features.Migration;

public sealed class PreviewRefreshPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, false, -1, true)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, false, 1, false)]
    [InlineData(false, false, -1, false)]
    [InlineData(true, true, -1, false)]
    public void RefreshRequiresVisibleIdlePreviewAtOrAfterDeadline(
        bool visible,
        bool active,
        int deadlineOffsetMilliseconds,
        bool expected)
    {
        var result = PreviewRefreshPolicy.ShouldRefresh(
            Now,
            Now.AddMilliseconds(deadlineOffsetMilliseconds),
            visible,
            active);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NextRefreshUsesTheNamedNonnegativeInterval()
    {
        Assert.Equal(
            Now.AddMilliseconds(500),
            PreviewRefreshPolicy.GetNextRefresh(Now, TimeSpan.FromMilliseconds(500)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewRefreshPolicy.GetNextRefresh(Now, TimeSpan.FromMilliseconds(-1)));
    }
}
