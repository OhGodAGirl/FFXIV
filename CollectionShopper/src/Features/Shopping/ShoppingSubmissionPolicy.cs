namespace CollectionShopper;

internal readonly record struct ShoppingSubmissionContext(
    bool Active, bool Open, bool ProxyAvailable, uint SearchItemId, uint ListingCount, int Capacity);

/// <summary>Validates the final observed row before invoking either native purchase operation.</summary>
internal static class ShoppingSubmissionPolicy
{
    internal static bool Submit(
        ShoppingSubmissionContext context,
        PurchaseListingCandidate expected,
        Func<int, PurchaseListingCandidate> read,
        Func<bool> prepare,
        Func<bool> send,
        out IReadOnlyList<DiagnosticPredicate> checks)
    {
        var inRange = expected.SourceIndex >= 0 && (uint)expected.SourceIndex < context.ListingCount
            && expected.SourceIndex < context.Capacity;
        var predicates = new List<DiagnosticPredicate>
        {
            new("Adapter active", context.Active, "True", context.Active.ToString()),
            new("Market Board open", context.Open, "True", context.Open.ToString()),
            new("Listing proxy available", context.ProxyAvailable, "True", context.ProxyAvailable.ToString()),
            new("Search item matches", context.ProxyAvailable ? context.SearchItemId == expected.ItemId : null, "True",
                context.ProxyAvailable ? (context.SearchItemId == expected.ItemId).ToString() : "Not observed"),
            new("Listing row in range", context.ProxyAvailable ? inRange : null, "Valid row",
                context.ProxyAvailable ? $"Row {expected.SourceIndex}; count {context.ListingCount}; capacity {context.Capacity}" : "Not observed")
        };
        checks = predicates;
        if (!context.Active || !context.Open || !context.ProxyAvailable
            || context.SearchItemId != expected.ItemId || !inRange)
            return false;

        var actual = read(expected.SourceIndex);
        void Match(string name, bool matches) => predicates.Add(new(name, matches, "True", matches.ToString()));
        Match("Listing identity matches", actual.ListingId == expected.ListingId);
        Match("Listing item matches", actual.ItemId == expected.ItemId);
        Match("Listing price matches", actual.UnitPrice == expected.UnitPrice);
        Match("Listing tax matches", actual.TotalTax == expected.TotalTax);
        Match("Listing quantity matches", actual.Quantity == expected.Quantity);
        Match("Listing quality matches", actual.IsHighQuality == expected.IsHighQuality);
        Match("Listing mannequin status matches", actual.IsMannequin == expected.IsMannequin);
        if (actual != expected)
            return false;
        var prepared = prepare();
        Match("Native purchase prepared", prepared);
        if (!prepared)
            return false;
        var sent = send();
        Match("Native purchase submitted", sent);
        return sent;
    }
}
