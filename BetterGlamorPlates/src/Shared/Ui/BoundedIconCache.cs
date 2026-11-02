namespace BetterGlamorPlates.Shared.Ui;

/// <summary>FIFO references to provider-owned textures; eviction never disposes shared values.</summary>
internal sealed class BoundedIconCache<T>(int capacity) where T : class
{
    private readonly int capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Dictionary<uint, T> values = [];
    private readonly Queue<uint> order = [];

    internal T Get(uint id, Func<uint, T> create)
    {
        if (values.TryGetValue(id, out var value)) return value;
        // Resolve before eviction so a provider failure does not disturb the existing cache.
        value = create(id);
        if (values.Count >= capacity)
            values.Remove(order.Dequeue());
        values.Add(id, value);
        order.Enqueue(id);
        return value;
    }

    internal void Clear()
    {
        values.Clear();
        order.Clear();
    }
}
