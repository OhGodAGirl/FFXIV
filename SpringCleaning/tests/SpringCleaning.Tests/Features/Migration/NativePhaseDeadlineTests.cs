namespace SpringCleaning.Tests;

public sealed class NativePhaseDeadlineTests
{
    [Theory]
    [InlineData((int)MigrationPhase.ClosingDresser)]
    [InlineData((int)MigrationPhase.OpeningArmoire)]
    [InlineData((int)MigrationPhase.StoringDresserOutfit)]
    public void RetriesDoNotExtendDeadlineAndNewRunResetsIt(int phaseValue)
    {
        var phase = (MigrationPhase)phaseValue;
        var timer = new NativePhaseDeadline();
        var now = DateTime.UtcNow;
        Assert.False(timer.Expired(phase, now));
        Assert.False(timer.Expired(phase, now.AddSeconds(14.999)));
        Assert.True(timer.Expired(phase, now.AddSeconds(15)));
        timer.Reset();
        Assert.False(timer.Expired(phase, now.AddSeconds(20)));
        Assert.True(timer.Expired(phase, now.AddSeconds(35)));
        Assert.False(timer.Expired(MigrationPhase.Cancelled, now.AddSeconds(36)));
        Assert.False(timer.Expired(phase, now.AddSeconds(37)));
    }
}
