using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class PromptCharacterRetryTests
{
    [Fact]
    public void CharacterChangeResetsRetryHistoryBeforeIdenticalFutureInputs()
    {
        var used = new PromptCapturePolicy();
        used.SetCharacter(123, 0);
        used.Hint("minions", 123, 0);
        var fresh = new PromptCapturePolicy();
        fresh.SetCharacter(123, 0);
        fresh.Hint("minions", 123, 0);
        for (var i = 0; i < 4; i++) _ = used.Take(double.NaN);
        used.SetCharacter(456, 0);
        fresh.SetCharacter(456, 0);
        _ = used.Take(double.NaN);
        _ = fresh.Take(double.NaN);
        Assert.Equal(fresh.Take(SnapshotSchedulePolicy.LoginDelay).ToArray(), used.Take(SnapshotSchedulePolicy.LoginDelay).ToArray());
    }
}
