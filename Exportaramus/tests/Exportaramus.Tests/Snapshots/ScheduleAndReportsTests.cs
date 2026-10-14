using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Publication;
using Exportaramus.Features.CollectionData.State;
using Exportaramus.Features.Snapshots;
namespace Exportaramus.Tests.Snapshots;

public sealed class ScheduleAndReportsTests
{
    [Fact]
    public void LoginAndIndependentDueTimes()
    {
        var schedule = new SnapshotSchedulePolicy(); schedule.Login(0); schedule.Tick(14.99); Assert.Equal(CaptureRequest.None, schedule.Take());
        schedule.Tick(15); Assert.Equal(CaptureRequest.AutomaticFull, schedule.Take()); schedule.Accepted(true, 15);
        schedule.Tick(314); Assert.Equal(CaptureRequest.None, schedule.Take()); schedule.Tick(315); Assert.Equal(CaptureRequest.AutomaticLightweight, schedule.Take());
        schedule.Accepted(false, 315); schedule.Tick(1815); Assert.Equal(CaptureRequest.AutomaticFull, schedule.Take());
    }
    [Fact]
    public void RetryBackoffAndManualPriority()
    {
        var schedule = new SnapshotSchedulePolicy(); var request = CaptureRequest.AutomaticFull;
        foreach (var (now, delay) in new[] { (15d, 30d), (45d, 120d), (165d, 300d), (465d, 300d) })
        {
            schedule.Failed(request, now, false); schedule.Tick(now + delay - 1); Assert.Equal(CaptureRequest.None, schedule.Take());
            schedule.Tick(now + delay); Assert.Equal(request, schedule.Take());
        }
        schedule.Request(CaptureRequest.ManualFull); schedule.Request(CaptureRequest.AutomaticLightweight); Assert.Equal(CaptureRequest.ManualFull, schedule.Take());
        schedule.Reset(); schedule.Failed(CaptureRequest.ManualFull, 0, true); schedule.Tick(10000); Assert.Equal(CaptureRequest.None, schedule.Take());
    }
    [Fact]
    public void IdentityGapIsBoundedAndNotLogout()
    {
        var identity = new SnapshotWithdrawalPolicy(); Assert.Null(identity.Observe(123, false, false, 0));
        Assert.Null(identity.Observe(0, false, true, 1)); Assert.Null(identity.Observe(0, false, true, 60.9));
        Assert.Equal("IdentityUnavailable", identity.Observe(0, false, true, 61)); Assert.Equal(0ul, identity.ActiveContentId);
        identity.Observe(123, false, false, 62); Assert.Equal("CharacterChanged", identity.Observe(124, false, false, 63));
        Assert.Equal("LoggedOut", identity.Observe(0, true, false, 64));
        identity.Observe(123, false, false, 65); identity.Observe(0, false, true, 66); Assert.Null(identity.Observe(123, false, false, 67));
        Assert.Equal("IdentityUnavailable", identity.Observe(0, false, false, 68));
    }
    [Theory]
    [InlineData("123", 1, " Test ", true)]
    [InlineData("0", 1, "Test", false)]
    [InlineData("0123", 1, "Test", false)]
    [InlineData("18446744073709551616", 1, "Test", false)]
    [InlineData("123", 0, "Test", false)]
    [InlineData("123", 1, "\n", false)]
    [InlineData("123", 1, "Te\u0001st", false)]
    public void ReporterNormalization(string id, uint item, string reporter, bool valid) => Assert.Equal(valid, ReportOwnershipEvidencePolicy.TryNormalize(id, item, reporter, out _, out _));
    [Fact]
    public void ReporterNfcAndUtf8Bounds()
    {
        Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 100, " e\u0301 ", out _, out var result)); Assert.Equal("é", result);
        Assert.True(ReportOwnershipEvidencePolicy.TryNormalize("123", 100, new string('a', 64), out _, out _));
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 100, new string('a', 65), out _, out _));
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 100, new string('a', 257), out _, out _));
        Assert.False(ReportOwnershipEvidencePolicy.TryNormalize("123", 100, "\ud800", out _, out _));
    }
    [Fact]
    public void ReportAdmissionAndFailureStates()
    {
        var queue = new ReportedEvidenceQueue(); Assert.Equal("Unavailable", queue.TryEnqueue("123", 100, "Test", 0, SnapshotFixture.Now));
        queue.SetScope(123, [100], []); Assert.Equal("WrongCharacter", queue.TryEnqueue("124", 100, "Test", 0, SnapshotFixture.Now));
        Assert.Equal("InvalidRequest", queue.TryEnqueue("123", 101, "Test", 0, SnapshotFixture.Now));
        Assert.Equal("Accepted", queue.TryEnqueue("123", 100, "Test", 0, SnapshotFixture.Now));
        Assert.Equal("Accepted", queue.TryEnqueue("123", 100, "Test", 0, SnapshotFixture.Now.AddMinutes(1))); Assert.Equal(1, queue.Count);
        Assert.Equal(SnapshotFixture.Now.AddMinutes(1), Assert.Single(queue.TakeBatch()).ReportedAtUtc); Assert.Equal(0, queue.Count);
        queue.PersistenceFailed(); Assert.Equal("Unavailable", queue.TryEnqueue("123", 100, "Test", 0, SnapshotFixture.Now));
        queue.Stop(); queue.Stop(); Assert.Equal("ProviderStopping", queue.TryEnqueue("123", 100, "Test", 0, SnapshotFixture.Now));
    }
    [Fact]
    public void TokenBucketsEnforceBurstAndRefill()
    {
        var limiter = new ReporterRateLimiter();
        for (var i = 0; i < 10; i++) Assert.True(limiter.TryAccept("A", 0));
        Assert.False(limiter.TryAccept("A", 0));
        for (var i = 0; i < 10; i++) Assert.True(limiter.TryAccept("B", 0));
        Assert.False(limiter.TryAccept("C", 0)); Assert.True(limiter.TryAccept("A", 2));
        limiter.Reset(); Assert.True(limiter.TryAccept("A", 0));
    }
    [Fact]
    public void RetainedCapacityAllowsRefreshButNeverEviction()
    {
        var records = Enumerable.Range(1, 10000).Select(i => new ReportedEvidence((uint)i, "Test", SnapshotFixture.Now));
        var queue = new ReportedEvidenceQueue(); queue.SetScope(123, Enumerable.Range(1, 10001).Select(i => (uint)i).ToImmutableHashSet(), records);
        Assert.Equal("CapacityReached", queue.TryEnqueue("123", 10001, "Test", 0, SnapshotFixture.Now));
        Assert.Equal("Accepted", queue.TryEnqueue("123", 1, "Test", 0, SnapshotFixture.Now));
    }
}
