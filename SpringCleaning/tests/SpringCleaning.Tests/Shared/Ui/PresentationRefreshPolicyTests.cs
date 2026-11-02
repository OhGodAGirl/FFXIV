namespace SpringCleaning.Tests;

public sealed class PresentationRefreshPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RetainsOnlyAfterAnAcceptedSnapshotAndForLessThanHalfASecond()
    {
        var policy = new PresentationRefreshPolicy();
        Assert.False(policy.RetainDisplay(Now, 42, 1, false, true, false));
        Assert.True(policy.ClearDisplay);
        Assert.False(policy.RetainDisplay(Now, 42, 1, true, false, false));
        Assert.True(policy.RetainDisplay(Now.AddSeconds(1), 42, 1, false, true, false));
        Assert.False(policy.ClearDisplay);
        Assert.True(policy.RetainDisplay(Now.AddMilliseconds(1499), 42, 1, false, true, false));
        Assert.False(policy.RetainDisplay(Now.AddMilliseconds(1500), 42, 1, false, true, false));
        Assert.True(policy.ClearDisplay);
    }

    [Theory]
    [InlineData(0UL, 1L, true, false)]
    [InlineData(43UL, 1L, true, false)]
    [InlineData(42UL, 2L, true, false)]
    [InlineData(42UL, 1L, true, true)]
    [InlineData(42UL, 1L, false, false)]
    public void LogoutCharacterWithdrawalFailureAndStoppedRefreshClearDisplay(
        ulong character, long epoch, bool refreshing, bool failed)
    {
        var policy = new PresentationRefreshPolicy();
        policy.RetainDisplay(Now, 42, 1, true, false, false);
        Assert.False(policy.RetainDisplay(Now.AddMilliseconds(10), character, epoch, false, refreshing, failed));
        Assert.True(policy.ClearDisplay);
    }

    [Fact]
    public void NewSessionClearsOldModelsEvenWhenItsSnapshotAlreadyArrived()
    {
        var policy = new PresentationRefreshPolicy();
        policy.RetainDisplay(Now, 42, 1, true, false, false);
        Assert.False(policy.RetainDisplay(Now.AddMilliseconds(10), 42, 2, true, false, false));
        Assert.True(policy.ClearDisplay);
    }

    [Fact]
    public void SuccessfulRereadStartsANewIndependentGapBudget()
    {
        var policy = new PresentationRefreshPolicy();
        policy.RetainDisplay(Now, 42, 1, true, false, false);
        policy.RetainDisplay(Now.AddSeconds(1), 42, 1, false, true, false);
        policy.RetainDisplay(Now.AddSeconds(2), 42, 1, true, false, false);
        Assert.True(policy.RetainDisplay(Now.AddSeconds(3), 42, 1, false, true, false));
        Assert.False(policy.RetainDisplay(Now.AddMilliseconds(3500), 42, 1, false, true, false));
    }

    [Fact]
    public void ClockMovingBackwardsDoesNotExtendTheGracePeriod()
    {
        var policy = new PresentationRefreshPolicy();
        policy.RetainDisplay(Now, 42, 1, true, false, false);
        policy.RetainDisplay(Now.AddSeconds(1), 42, 1, false, true, false);
        Assert.False(policy.RetainDisplay(Now, 42, 1, false, true, false));
        Assert.True(policy.ClearDisplay);
    }

    [Fact]
    public void AnAcceptedSnapshotInTheSameScopeDoesNotClearItsDisplay()
    {
        var policy = new PresentationRefreshPolicy();
        policy.RetainDisplay(Now, 42, 1, true, false, false);
        policy.RetainDisplay(Now.AddSeconds(1), 42, 1, true, false, false);
        Assert.False(policy.ClearDisplay);
    }

    [Fact]
    public void AFailedObservationCannotPrimePresentationRetention()
    {
        var policy = new PresentationRefreshPolicy();
        Assert.False(policy.RetainDisplay(Now, 42, 1, true, false, true));
        Assert.False(policy.RetainDisplay(Now.AddSeconds(1), 42, 1, false, true, false));
        Assert.True(policy.ClearDisplay);
    }
}
