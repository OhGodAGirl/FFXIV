namespace Exportaramus.Features.Inventory;

public static class RetainerMenuPolicy
{
    public static int FindUniqueIndex(IReadOnlyList<string> entries, string label, bool prefix)
    {
        var index = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            var matches = prefix
                ? entries[i].StartsWith(label, StringComparison.Ordinal)
                : entries[i].TrimEnd('.').Equals(label.TrimEnd('.'), StringComparison.Ordinal);
            if (matches)
            {
                if (index != -1)
                {
                    return -1;
                }
                index = i;
            }
        }
        return index;
    }
}
