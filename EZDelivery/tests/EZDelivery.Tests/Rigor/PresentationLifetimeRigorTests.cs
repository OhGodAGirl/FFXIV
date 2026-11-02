using EZDelivery.Features.Interface;

namespace EZDelivery.Tests.Interface;

public sealed class PresentationLifetimeRigorTests
{
    private sealed class SharedTexture : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void RepeatedCacheLifetimesReleaseReferencesWithoutTakingSharedOwnership()
    {
        for (uint iteration = 1; iteration <= 100; iteration++)
        {
            var retained = new SharedTexture();
            var replacement = new SharedTexture();
            var cache = new TextureCache<SharedTexture>();
            cache.Prepare([iteration, iteration], _ => retained);
            Assert.True(cache.TryGetValue(iteration, out var actual));
            Assert.Same(retained, actual);

            // A provider failure is visible; a later preparation replaces old references.
            Assert.Throws<InvalidOperationException>(() => cache.Prepare([iteration + 1], _ => throw new InvalidOperationException()));
            Assert.False(cache.TryGetValue(iteration, out _));
            cache.Prepare([iteration + 1], _ => replacement);
            Assert.True(cache.TryGetValue(iteration + 1, out actual));
            Assert.Same(replacement, actual);

            cache.Dispose();
            cache.Dispose();
            cache.Prepare(null!, _ => throw new InvalidOperationException("Disposed cache performed a lookup."));
            Assert.False(cache.TryGetValue(iteration + 1, out _));
            Assert.Equal(0, retained.Disposals);
            Assert.Equal(0, replacement.Disposals);
        }
    }

    [Fact]
    public void RepeatedThemeObserverLifetimesRetainLastSuccessAndRecoverAfterReadFailure()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var observer = new ThemeObserver();
            var selected = (uint)(iteration % 8);
            var replacement = (selected + 1) % 8;
            observer.Update(0, () => selected);
            observer.Update(499, () => throw new InvalidOperationException("Read before deadline."));
            Assert.Equal(selected, observer.Current);
            observer.Update(500, () => throw new InvalidOperationException("Host read failed."));
            Assert.Equal(selected, observer.Current);
            observer.Update(999, () => replacement);
            Assert.Equal(selected, observer.Current);
            observer.Update(1000, () => replacement);
            Assert.Equal(replacement, observer.Current);
            observer.Update(1500, () => null);
            Assert.Equal(replacement, observer.Current);
            Assert.Equal(0u, new ThemeObserver().Current);
        }
    }
}
