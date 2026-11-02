using System.Diagnostics.CodeAnalysis;

namespace EZDelivery.Features.Interface;

/// <summary>Retains shared references, never ownership of the resources themselves.</summary>
public sealed class TextureCache<T> : IDisposable where T : class
{
    private readonly Dictionary<uint, T> textures = new();
    private readonly HashSet<uint> wanted = new();
    private readonly List<uint> obsolete = new();
    private bool disposed;

    /// <summary>Caches shared texture references and drops references absent from the new set.</summary>
    public void Prepare(IEnumerable<uint> icons, Func<uint, T?> lookup)
    {
        if (disposed) return;
        wanted.Clear();
        foreach (var icon in icons)
        {
            if (icon != 0) wanted.Add(icon);
            if (wanted.Count == 2048) break;
        }
        obsolete.Clear();
        foreach (var icon in textures.Keys)
            if (!wanted.Contains(icon)) obsolete.Add(icon);
        foreach (var icon in obsolete) textures.Remove(icon);
        foreach (var icon in wanted)
            if (!textures.ContainsKey(icon) && lookup(icon) is { } texture)
                textures[icon] = texture;
    }

    /// <summary>Looks up a prepared texture without loading during rendering.</summary>
    public bool TryGetValue(uint icon, [NotNullWhen(true)] out T? texture) => textures.TryGetValue(icon, out texture);

    /// <summary>Clears cached references without disposing shared texture resources.</summary>
    public void Dispose()
    {
        disposed = true;
        textures.Clear();
        wanted.Clear();
        obsolete.Clear();
    }
}
