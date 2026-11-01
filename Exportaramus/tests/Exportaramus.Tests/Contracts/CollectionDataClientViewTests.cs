using V1 = Exportaramus.ConsumerKit.V1;
using V11 = Exportaramus.ConsumerKit.V1_1;
using V12 = Exportaramus.ConsumerKit.V1_2;

namespace Exportaramus.Tests.Contracts;

public sealed class CollectionDataClientViewTests
{
    [Fact]
    public void Version10EmptyViewClearsPublicationIdentityAndPreservesFailure()
    {
        // Arrange.
        var active = new V1.CollectionDataClientView(V1.CollectionDataAvailability.Ready, null, "session", "42", 7, V1.CollectionDataValidationCode.None);

        // Act.
        var withdrawn = V1.CollectionDataClientView.Empty(V1.CollectionDataAvailability.Unavailable, V1.CollectionDataValidationCode.ProviderUnavailable);

        // Assert: withdrawal clears the old publication identity and revision.
        Assert.Equal(V1.CollectionDataAvailability.Unavailable, withdrawn.Availability);
        Assert.Equal(V1.CollectionDataValidationCode.ProviderUnavailable, withdrawn.Failure);
        Assert.Null(withdrawn.Snapshot);
        Assert.Null(withdrawn.ProviderSessionId);
        Assert.Null(withdrawn.ContentId);
        Assert.Equal(0, withdrawn.Revision);
        Assert.Equal("session", active.ProviderSessionId);
        Assert.Equal("42", active.ContentId);
        Assert.Equal(7, active.Revision);
    }

    [Fact]
    public void Version11EmptyViewClearsPublicationIdentityAndPreservesFailure()
    {
        // Arrange.
        var active = new V11.CollectionDataClientView(V11.CollectionDataAvailability.Ready, null, "session", "42", 7, V11.CollectionDataValidationCode.None);

        // Act.
        var withdrawn = V11.CollectionDataClientView.Empty(V11.CollectionDataAvailability.Rejected, V11.CollectionDataValidationCode.WrongCharacter);

        // Assert.
        Assert.Equal(V11.CollectionDataAvailability.Rejected, withdrawn.Availability);
        Assert.Equal(V11.CollectionDataValidationCode.WrongCharacter, withdrawn.Failure);
        Assert.Null(withdrawn.Snapshot);
        Assert.Null(withdrawn.ProviderSessionId);
        Assert.Null(withdrawn.ContentId);
        Assert.Equal(0, withdrawn.Revision);
        Assert.Equal("session", active.ProviderSessionId);
        Assert.Equal("42", active.ContentId);
        Assert.Equal(7, active.Revision);
    }

    [Fact]
    public void Version12EmptyViewClearsPublicationIdentityAndPreservesFailure()
    {
        // Arrange.
        var active = new V12.CollectionDataClientView(V12.CollectionDataAvailability.Ready, null, "session", "42", 7, V12.CollectionDataValidationCode.None);

        // Act.
        var withdrawn = V12.CollectionDataClientView.Empty(V12.CollectionDataAvailability.Disposed, V12.CollectionDataValidationCode.Disposed);

        // Assert.
        Assert.Equal(V12.CollectionDataAvailability.Disposed, withdrawn.Availability);
        Assert.Equal(V12.CollectionDataValidationCode.Disposed, withdrawn.Failure);
        Assert.Null(withdrawn.Snapshot);
        Assert.Null(withdrawn.ProviderSessionId);
        Assert.Null(withdrawn.ContentId);
        Assert.Equal(0, withdrawn.Revision);
        Assert.Equal("session", active.ProviderSessionId);
        Assert.Equal("42", active.ContentId);
        Assert.Equal(7, active.Revision);
    }
}
