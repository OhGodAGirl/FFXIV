namespace SpringCleaning.Tests;

public sealed class PluginIdentityTests
{
    [Fact]
    public void IdentityMatchesPublicAndTechnicalContracts()
    {
        // Arrange

        // Act
        var identity = (
            PluginIdentity.PublicName,
            PluginIdentity.InternalName,
            PluginIdentity.CommandName);

        // Assert
        Assert.Equal(("Spring Cleaning", "SpringCleaning", "/springcleaning"), identity);
    }
}
