using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class TextureCacheTests
{
    private sealed class SharedTexture : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void NonzeroDistinctIconsAreLookedUpOnceAndReused()
    {
        // Arrange
        using var cache = new TextureCache<SharedTexture>();
        var resource = new SharedTexture();
        var calls = new List<uint>();
        SharedTexture Lookup(uint icon) { calls.Add(icon); return resource; }

        // Act
        cache.Prepare([0, 1, 1, 0], Lookup);
        cache.Prepare([1, 1], Lookup);

        // Assert
        Assert.Equal(new uint[] { 1 }, calls);
        Assert.False(cache.TryGetValue(0, out _));
        Assert.True(cache.TryGetValue(1, out var actual));
        Assert.Same(resource, actual);
    }

    [Fact]
    public void FailedLookupIsRetriedOnNextPreparation()
    {
        // Arrange
        using var cache = new TextureCache<SharedTexture>();
        var resource = new SharedTexture();

        // Act
        cache.Prepare([1], _ => null);
        var foundAfterFailure = cache.TryGetValue(1, out _);
        cache.Prepare([1], _ => resource);

        // Assert
        Assert.False(foundAfterFailure);
        Assert.True(cache.TryGetValue(1, out var actual));
        Assert.Same(resource, actual);
    }

    [Fact]
    public void StaleReferencesAreEvictedWithoutDisposingSharedResources()
    {
        // Arrange
        using var cache = new TextureCache<SharedTexture>();
        var oldResource = new SharedTexture();
        var newResource = new SharedTexture();
        cache.Prepare([1], _ => oldResource);

        // Act
        cache.Prepare([2], _ => newResource);

        // Assert
        Assert.False(cache.TryGetValue(1, out _));
        Assert.True(cache.TryGetValue(2, out var actual));
        Assert.Same(newResource, actual);
        Assert.False(oldResource.Disposed);
    }

    [Fact]
    public void LimitCountsDistinctIconsAndDoesNotEnumeratePastBound()
    {
        // Arrange
        using var cache = new TextureCache<SharedTexture>();
        var calls = new List<uint>();
        IEnumerable<uint> Icons()
        {
            for (uint icon = 1; icon <= 2048; icon++)
            {
                yield return 0;
                yield return icon;
                yield return icon;
            }
            throw new InvalidOperationException("Enumeration passed the cache bound.");
        }

        // Act
        cache.Prepare(Icons(), icon => { calls.Add(icon); return new(); });

        // Assert
        Assert.Equal(2048, calls.Count);
        Assert.Equal(2048, calls.Distinct().Count());
        Assert.True(cache.TryGetValue(2048, out _));
        Assert.False(cache.TryGetValue(2049, out _));
    }

    [Fact]
    public void EmptyPreparationClearsCacheAndDisposalNeverTouchesSharedOwnership()
    {
        // Arrange
        var cache = new TextureCache<SharedTexture>();
        var resource = new SharedTexture();
        cache.Prepare([1], _ => resource);

        // Act
        cache.Prepare([], _ => throw new InvalidOperationException());
        var empty = !cache.TryGetValue(1, out _);
        cache.Prepare([1], _ => resource);
        cache.Dispose();
        cache.Dispose();
        cache.Prepare(null!, _ => throw new InvalidOperationException());

        // Assert
        Assert.True(empty);
        Assert.False(cache.TryGetValue(1, out _));
        Assert.False(resource.Disposed);
    }

    [Fact]
    public void LookupExceptionRemainsVisibleAndNextPreparationCanRecover()
    {
        // Arrange
        using var cache = new TextureCache<SharedTexture>();
        var failure = new InvalidOperationException("Provider failed.");
        var resource = new SharedTexture();

        // Act
        var actual = Assert.Throws<InvalidOperationException>(() => cache.Prepare([1], _ => throw failure));
        cache.Prepare([1], _ => resource);

        // Assert
        Assert.Same(failure, actual);
        Assert.True(cache.TryGetValue(1, out var recovered));
        Assert.Same(resource, recovered);
    }

    [Fact]
    public void ReturningToAnEarlierIconDoesNotKeepEvictingItsNewReference()
    {
        using var cache = new TextureCache<SharedTexture>();
        var calls = new List<uint>();
        SharedTexture Lookup(uint icon) { calls.Add(icon); return new(); }
        cache.Prepare([1], Lookup);
        cache.Prepare([2], Lookup);
        cache.Prepare([1], Lookup);
        Assert.True(cache.TryGetValue(1, out var returned));
        cache.Prepare([1], Lookup);
        Assert.Equal(new uint[] { 1, 2, 1 }, calls);
        Assert.True(cache.TryGetValue(1, out var retained));
        Assert.Same(returned, retained);
    }
}
