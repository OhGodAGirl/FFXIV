using CollectionExample;

namespace Exportaramus.PrivateQualificationTests;

public sealed class ReaderAssemblyProbeTests
{
    [Fact]
    public void ReaderTestsUseTheInstrumentedProductionReaderAssembly()
    {
        // Arrange / Act: inspect the reader type used by the behavior tests.
        var assembly = typeof(InventoryItemReader).Assembly.GetName().Name;
        // Assert.
        Assert.Equal("Exportaramus.InventoryReader.Mutation", assembly);
    }
}
