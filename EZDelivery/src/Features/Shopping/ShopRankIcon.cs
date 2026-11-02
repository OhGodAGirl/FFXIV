namespace EZDelivery.Features.Shopping;

/// <summary>Exchange rank glyph variant 2, verified against the supported game binary.</summary>
public static class ShopRankIcon
{
    /// <summary>Returns the company-specific icon ID for a required rank.</summary>
    public static uint Expected(byte company, byte rank) => company switch
    {
        1 => 83600u + rank,
        2 => 83650u + rank,
        3 => 83700u + rank,
        _ => 0,
    };
}
