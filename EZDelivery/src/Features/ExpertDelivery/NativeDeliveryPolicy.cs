namespace EZDelivery.Features.ExpertDelivery;

/// <summary>Decisions over copied native observations; this policy never reads game memory.</summary>
public static class NativeDeliveryPolicy
{
    /// <summary>Requires matching generation, agent, callback, selected row, layout and item name.</summary>
    public static bool RewardCorrelated(bool generationMatches, bool agentMatches, bool callbackMatches,
        bool rowMatches, bool layoutMatches, bool itemNameMatches)
        => generationMatches && agentMatches && callbackMatches && rowMatches && layoutMatches && itemNameMatches;

    /// <summary>Checks the expected reward callback kind and receiver class.</summary>
    public static bool RewardCallbackMatches(bool present, ulong kind, bool receiverClassMatches)
        => present && kind == 2 && receiverClassMatches;

    /// <summary>Requires the reward bonus fingerprint to remain unchanged.</summary>
    public static bool BuffsUnchanged(ulong selected, ulong current) => selected == current;

    /// <summary>Adds the observed seal bonus to the base reward and rejects overflow or zero.</summary>
    public static uint? RewardAmount(uint baseReward, uint bonus)
    {
        var final = (ulong)baseReward + bonus;
        return final is > 0 and <= uint.MaxValue ? (uint)final : null;
    }
}
