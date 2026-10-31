using CollectionExample;

namespace Exportaramus.PrivateQualificationTests;

public sealed class ReaderAssemblyProbeTests
{
    [Fact]
    public void ReaderTestsUseTheInstrumentedProductionReaderAssembly()
    {
        // Arrange / Act: inspect the actual type used by the source-linked reader behavior tests.
        var assembly = typeof(InventoryItemReader).Assembly.GetName().Name;
        // Assert.
        Assert.Equal("Exportaramus.InventoryReader.Mutation", assembly);
    }
}
