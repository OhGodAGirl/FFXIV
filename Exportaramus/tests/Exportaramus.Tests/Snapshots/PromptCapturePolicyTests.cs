using Exportaramus.Features.Snapshots;

namespace Exportaramus.Tests.Snapshots;

public sealed class PromptCapturePolicyTests
{
    [Fact]
    public void SimultaneousHintsAndFallbackProduceEachSourceOnlyOnce()
    {
        var policy = new PromptCapturePolicy(); policy.SetCharacter(123, 0);
        policy.Hint("mounts", 123, 14.75); policy.Hint("orchestrions", 123, 14.75);
        Assert.Equal(2, policy.Take(15).Length);
        policy.Hint("quests", 123, 299.75); policy.Hint("mounts", 123, 299.75);
        var together = policy.Take(300);
        Assert.Equal(1, together.Count(key => key == "quests"));
        Assert.Contains("mounts", together);
        Assert.Contains("quests", policy.Take(600));
        policy.Take(899.9);
        Assert.Equal(new[] { "quests" }, policy.Take(900));
    }

    [Fact]
    public void BusyWorkCannotLeaveAnOldHintRetryQueuedIndefinitely()
    {
        var policy = new PromptCapturePolicy(); policy.SetCharacter(123, 0); policy.Take(15);
        policy.Hint("orchestrions", 123, 20);
        Assert.DoesNotContain("orchestrions", policy.Take(36));
    }
    [Fact]
    public void BurstsCoalesceWithoutPostponingTheFirstReadAndDelayedStateGetsBoundedRetries()
    {
        var policy = new PromptCapturePolicy(); policy.SetCharacter(123, 0);
        policy.Take(15); // The independent fallback visits mounts.
        for (var i = 0; i < 1000; i++) policy.Hint("orchestrions", 123, 20 + i / 10000.0);
        Assert.DoesNotContain("orchestrions", policy.Take(20.249));
        Assert.Equal(1, policy.Take(20.25).Count(key => key == "orchestrions"));
        Assert.DoesNotContain("orchestrions", policy.Take(21.249));
        Assert.Contains("orchestrions", policy.Take(21.25));
        Assert.Contains("orchestrions", policy.Take(24.25));
        Assert.Contains("orchestrions", policy.Take(32.25));
        Assert.DoesNotContain("orchestrions", policy.Take(33));
    }

    [Fact]
    public void AbsentWrongAndChangedIdentityCannotCarryPendingHints()
    {
        var policy = new PromptCapturePolicy(); policy.Hint("orchestrions", 123, 0);
        Assert.Empty(policy.Take(100));
        policy.SetCharacter(123, 0); policy.Take(15);
        policy.Hint("orchestrions", 124, 20); policy.Hint("reportedOwnership", 123, 20);
        Assert.DoesNotContain("orchestrions", policy.Take(20.25));
        policy.Hint("orchestrions", 123, 21); policy.SetCharacter(124, 21);
        Assert.Empty(policy.Take(35.99));
        Assert.Equal(new[] { "mounts" }, policy.Take(36));
        policy.SetCharacter(0, 37); Assert.Empty(policy.Take(1000));
    }

    [Fact]
    public void MissingSignalsHaveBoundedRoundRobinRecoveryWithoutFrequentQuestScans()
    {
        var policy = new PromptCapturePolicy(); policy.SetCharacter(123, 0);
        var seen = new HashSet<string>();
        for (var i = 0; i < 27; i++)
        {
            var due = policy.Take(15 + i * 1.5);
            Assert.InRange(due.Length, 0, 1);
            seen.UnionWith(due);
        }
        Assert.Equal(26, seen.Count);
        Assert.Contains("orchestrions", seen); Assert.Contains("inventory", seen);
        Assert.DoesNotContain("quests", seen);
        Assert.Contains("quests", policy.Take(300));
        policy.Hint("quests", 123, 301);
        Assert.Contains("quests", policy.Take(301.25));
    }
}
