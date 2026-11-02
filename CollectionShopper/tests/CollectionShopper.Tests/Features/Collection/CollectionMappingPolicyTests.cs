namespace CollectionShopper.Tests;

public sealed class CollectionMappingPolicyTests
{
    [Fact]
    public void FacewearUsesNamedRowsPlusOneAndDeduplicates()
    {
        // Arrange
        (uint, bool)[] rows = [(0, true), (1, false), (2, true), (2, true)];
        // Act
        var ids = CollectionMappingPolicy.Facewear(rows);
        // Assert
        Assert.True(ids.SetEquals(new uint[] { 1, 3 }));
    }

    [Fact]
    public void InvalidNamedRowCannotWrapIntoZeroIdentifier()
    {
        // Arrange
        (uint, bool)[] rows = [(uint.MaxValue, true)];
        // Act / Assert
        Assert.Throws<OverflowException>(() => CollectionMappingPolicy.Facewear(rows));
    }

    [Fact]
    public void UnnamedRowIsOmittedBeforeIdentifierArithmetic()
    {
        // Arrange
        (uint, bool)[] rows = [(uint.MaxValue, false)];
        // Act
        var ids = CollectionMappingPolicy.Facewear(rows);
        // Assert
        Assert.Empty(ids);
    }
}
