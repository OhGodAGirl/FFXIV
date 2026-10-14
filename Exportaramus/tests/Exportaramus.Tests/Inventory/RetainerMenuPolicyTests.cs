using Exportaramus.Features.Inventory;

namespace Exportaramus.Tests.Inventory;

public sealed class RetainerMenuPolicyTests
{
    [Theory]
    [InlineData("Entrust items...", "Entrust items", false, 1)]
    [InlineData("Entrust items", "Entrust items...", false, 1)]
    [InlineData("Entrust items (inventory)", "Entrust items", true, 1)]
    [InlineData("Entrust items (inventory)", "Entrust items", false, -1)]
    [InlineData("entrust items", "Entrust items", true, -1)]
    [InlineData("entrust items", "Entrust items", false, -1)]
    public void MatchesOnlyTheRequestedOrdinalLabel(string text, string label, bool prefix, int expected)
    {
        Assert.Equal(expected, RetainerMenuPolicy.FindUniqueIndex(new[] { "Quit", text }, label, prefix));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousEntriesNeverChooseAnAction(bool prefix)
    {
        Assert.Equal(-1, RetainerMenuPolicy.FindUniqueIndex(new[] { "Entrust...", "Entrust" }, "Entrust", prefix));
        Assert.Equal(-1, RetainerMenuPolicy.FindUniqueIndex(Array.Empty<string>(), "Entrust", prefix));
        Assert.Equal(0, RetainerMenuPolicy.FindUniqueIndex(new[] { "Entrust", "Quit" }, "Entrust", prefix));
    }
}
