using System.Text;
using BetterGlamorPlates.Shared.Ui;

namespace BetterGlamorPlates.Tests;

/// <summary>Tests text and texture-reference behavior without loading ImGui.</summary>
public sealed class UiBoundaryTests
{
    /// <summary>Cache hits preserve identity; insertion evicts the oldest reference without disposal.</summary>
    [Fact]
    public void CacheIsBoundedFifoAndDoesNotOwnProviderValues()
    {
        // Arrange
        var cache = new BoundedIconCache<object>(2);
        var calls = new List<uint>();
        object Create(uint id) { calls.Add(id); return new object(); }
        var first = cache.Get(1, Create);
        var second = cache.Get(2, Create);

        // Act
        Assert.Same(first, cache.Get(1, Create));
        cache.Get(3, Create);

        // Assert
        Assert.Same(second, cache.Get(2, Create));
        Assert.NotSame(first, cache.Get(1, Create));
        Assert.Equal(new uint[] { 1, 2, 3, 1 }, calls);
        cache.Clear();
        Assert.NotSame(second, cache.Get(2, Create));
        Assert.Equal(5, calls.Count);
    }

    /// <summary>A failed provider lookup leaves the existing entries usable and can be retried.</summary>
    [Fact]
    public void ProviderFailureDoesNotEvictOrCacheFailure()
    {
        var cache = new BoundedIconCache<object>(1);
        var value = cache.Get(1, _ => new object());

        Assert.Throws<IOException>(() => cache.Get(2, _ => throw new IOException()));

        Assert.Same(value, cache.Get(1, _ => throw new InvalidOperationException()));
        Assert.NotNull(cache.Get(2, _ => new object()));
    }

    /// <summary>Clearing removes the old eviction order as well as the cached references.</summary>
    [Fact]
    public void RefillingClearedCacheStillHonorsCapacity()
    {
        var cache = new BoundedIconCache<object>(2);
        cache.Get(1, _ => new object());
        cache.Get(2, _ => new object());
        cache.Clear();

        var firstAfterClear = cache.Get(3, _ => new object());
        cache.Get(4, _ => new object());
        cache.Get(5, _ => new object());

        Assert.NotSame(firstAfterClear, cache.Get(3, _ => new object()));
    }

    /// <summary>A non-positive cache bound cannot silently create an unbounded cache.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CacheRejectsInvalidBound(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedIconCache<object>(size));

    /// <summary>Reads stop at zero, trim whitespace and accept nonterminated input.</summary>
    [Fact]
    public void TextReadsNativeBufferBoundaries()
    {
        Assert.Equal("Coat", UiText.Read(Encoding.UTF8.GetBytes(" Coat \0ignored")));
        Assert.Equal("Coat", UiText.Read(Encoding.UTF8.GetBytes(" Coat ")));
        Assert.Equal("", UiText.Read([]));
    }

    /// <summary>Writing erases old text, reserves termination and keeps complete Unicode characters.</summary>
    [Fact]
    public void TextWritesBoundedUtf8WithoutPartialCharacters()
    {
        var buffer = Enumerable.Repeat((byte)255, 7).ToArray();

        UiText.Write(buffer, "猫猫猫");

        Assert.Equal("猫猫", UiText.Read(buffer));
        Assert.Equal(0, buffer[^1]);
        UiText.Write(buffer, "A");
        Assert.Equal(new byte[] { 65, 0, 0, 0, 0, 0, 0 }, buffer);
        UiText.Write(buffer, "");
        Assert.All(buffer, value => Assert.Equal(0, value));
        UiText.Write([], "test");
        var tiny = new byte[2];
        UiText.Write(tiny, "猫");
        Assert.Equal("", UiText.Read(tiny));
        UiText.Write(tiny, "猫A");
        Assert.Equal("", UiText.Read(tiny));
    }

    /// <summary>Fitting keeps exact matches and returns bounded ellipsis when no prefix fits.</summary>
    [Theory]
    [InlineData("Coat", 4, "Coat")]
    [InlineData("Coat", 3, "Co…")]
    [InlineData("Coat", 0, "…")]
    [InlineData("", 0, "")]
    public void FitTextUsesMeasuredWidth(string input, float width, string expected)
    {
        var measurements = 0;
        var result = UiText.Fit(input, width, text =>
        {
            Assert.True(++measurements <= input.Length + 2, "Fitting must make bounded progress.");
            return text.Length;
        });
        Assert.Equal(expected, result);
    }
}
