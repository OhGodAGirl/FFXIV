namespace SpringCleaning.Tests;

internal static class InventoryFixture
{
    internal const string Capabilities = """{"schemaVersion":"1.0","scope":"CurrentCharacter","quantityQuality":["Normal","HighQuality"],"retainedIsCurrent":false}""";
    internal static string Inventory(string state = "Observed", string items = "[]", string observed = "\"2026-09-12T00:00:00Z\"", string kind = "Inventory", bool quantities = true, ulong revision = 1) =>
        $$"""{"schemaVersion":"1.0","available":true,"contentId":"12","revision":{{revision}},"retainerRosterKnown":false,"locations":[{"kind":"{{kind}}","retainerId":"0","name":"","state":"{{state}}","observedAtUtc":{{observed}},"attemptedAtUtc":"2026-09-12T00:00:00Z","supportsQuantity":{{quantities.ToString().ToLowerInvariant()}},"items":{{items}}}]}""";
}
