namespace CollectionExporter.Tests;

public sealed class FfxivCollectProfileTests
{
    [Theory]
    [InlineData("12345678", 12345678UL)]
    [InlineData(" 12345678/ ", 12345678UL)]
    [InlineData("https://ffxivcollect.com/characters/12345678", 12345678UL)]
    [InlineData("https://FFXIVCOLLECT.COM/characters/12345678/", 12345678UL)]
    public void TryParseCharacterIdAcceptsSupportedProfiles(string input, ulong expected)
    {
        var parsed = FfxivCollectProfile.TryParseCharacterId(input, out var characterId);

        Assert.True(parsed);
        Assert.Equal(expected, characterId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("https://example.com/characters/12345678")]
    [InlineData("http://ffxivcollect.com/characters/12345678")]
    [InlineData("https://ffxivcollect.com/users/12345678")]
    [InlineData("https://ffxivcollect.com/archive/characters/12345678")]
    public void TryParseCharacterIdRejectsUnsupportedProfiles(string input)
    {
        var parsed = FfxivCollectProfile.TryParseCharacterId(input, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void GetCharacterUrlCreatesCanonicalUrl()
    {
        Assert.Equal(
            "https://ffxivcollect.com/characters/12345678",
            FfxivCollectProfile.GetCharacterUrl(12345678));
    }
}
