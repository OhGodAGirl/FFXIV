using System.Collections.Immutable;
using Exportaramus.Features.CollectionData.Contracts;

namespace Exportaramus.Features.Snapshots;

/// <summary>Framework-thread scheduling only. A hint never establishes an observed fact.</summary>
public sealed class PromptCapturePolicy
{
    public const double CoalesceDelay = 0.25;
    public const double FallbackInterval = 1.5;
    public const double RetryWindow = 15;
    private static readonly ImmutableArray<string> Sources = [.. CollectionKeys.Unlocks, .. CollectionKeys.Presence];
    private readonly double[] due = new double[Sources.Length];
    private readonly int[] attempts = new int[Sources.Length];
    private readonly double[] expires = new double[Sources.Length];
    private ulong character;
    private double readyAt;
    private double fallbackDue;
    private int fallbackIndex;
    private double questDue;

    public void SetCharacter(ulong contentId, double now)
    {
        if (character == contentId) return;
        character = contentId;
        Array.Fill(due, double.PositiveInfinity);
        Array.Clear(attempts);
        readyAt = now + SnapshotSchedulePolicy.LoginDelay;
        fallbackDue = readyAt;
        fallbackIndex = 0;
        questDue = now + SnapshotSchedulePolicy.PresenceInterval;
    }

    public void Hint(string source, ulong contentId, double now)
    {
        if (character == 0 || contentId != character) return;
        var index = Sources.IndexOf(source);
        if (index < 0) return;
        due[index] = Math.Min(due[index], now + CoalesceDelay);
        expires[index] = now + RetryWindow;
        attempts[index] = 0;
    }

    public ImmutableArray<string> Take(double now)
    {
        if (character == 0 || now < readyAt) return [];
        ImmutableArray<string>.Builder? result = null;
        for (var i = 0; i < Sources.Length; i++)
        {
            if (now < due[i]) continue;
            if (now > expires[i]) { due[i] = double.PositiveInfinity; continue; }
            (result ??= ImmutableArray.CreateBuilder<string>()).Add(Sources[i]);
            due[i] = ++attempts[i] switch { 1 => now + 1, 2 => now + 3, 3 => now + 8, _ => double.PositiveInfinity };
        }
        if (now >= fallbackDue)
        {
            var source = Sources[fallbackIndex];
            fallbackIndex = (fallbackIndex + 1) % Sources.Length;
            fallbackDue = now + FallbackInterval;
            if (source != "quests" && (result is null || !result.Contains(source))) (result ??= ImmutableArray.CreateBuilder<string>()).Add(source);
        }
        if (now >= questDue)
        {
            questDue = now + SnapshotSchedulePolicy.PresenceInterval;
            if (result is null || !result.Contains("quests")) (result ??= ImmutableArray.CreateBuilder<string>()).Add("quests");
        }
        return result?.ToImmutable() ?? [];
    }
}
