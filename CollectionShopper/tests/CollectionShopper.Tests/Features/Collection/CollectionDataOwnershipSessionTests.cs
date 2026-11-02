namespace CollectionShopper.Tests;

public sealed class CollectionDataOwnershipSessionTests
{
    [Fact]
    public void CharacterChangeRejectsStalePublicationAndClearsState()
    {
        using var session = new CollectionDataOwnershipSession();
        var firstVersion = session.Begin(100);
        Assert.True(session.TryPublish(100, firstVersion, AcceptedState("First.")));

        session.Invalidate(200);

        Assert.False(session.TryPublish(100, firstVersion, AcceptedState("Stale.")));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
    }

    [Fact]
    public void LogoutInvalidationAllowsNextCharacterPublication()
    {
        using var session = new CollectionDataOwnershipSession();
        var firstVersion = session.Begin(100);
        Assert.True(session.TryPublish(100, firstVersion, AcceptedState("First.")));

        session.Invalidate();
        var secondVersion = session.Begin(200);

        Assert.True(session.TryPublish(200, secondVersion, AcceptedState("Second.")));
        Assert.Equal("Second.", session.State.StatusDetail);
    }

    [Fact]
    public void DisposalIsIdempotentAndBlocksLatePublication()
    {
        var session = new CollectionDataOwnershipSession();
        var version = session.Begin(100);
        session.Dispose();
        session.Dispose();

        Assert.False(session.TryPublish(100, version, AcceptedState("Late.")));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
    }

    [Fact]
    public void SameCharacterBeginKeepsGenerationAndAcceptedState()
    {
        using var session = new CollectionDataOwnershipSession();
        var version = session.Begin(100);
        var accepted = AcceptedState("Accepted.");
        Assert.True(session.TryPublish(100, version, accepted));

        Assert.Equal(1, version);
        Assert.Equal(version, session.Begin(100));
        Assert.Same(accepted, session.State);
        Assert.True(session.IsCurrent(100, version));
    }

    [Fact]
    public void PublishRejectsEveryStaleOrInvalidIdentityShape()
    {
        AssertRejected((session, version) => session.TryPublish(0, version, AcceptedState("Zero.")));
        AssertRejected((session, version) => session.TryPublish(200, version, AcceptedState("Wrong character.")));
        AssertRejected((session, version) => session.TryPublish(100, version + 1, AcceptedState("Wrong generation.")));

        using var current = new CollectionDataOwnershipSession();
        var currentVersion = current.Begin(100);
        Assert.False(current.IsCurrent(0, currentVersion));
        Assert.False(current.IsCurrent(200, currentVersion));
        Assert.False(current.IsCurrent(100, currentVersion + 1));
    }

    [Fact]
    public void InvalidateAlwaysAdvancesGenerationAndClearsState()
    {
        using var session = new CollectionDataOwnershipSession();
        var firstVersion = session.Begin(100);
        Assert.True(session.TryPublish(100, firstVersion, AcceptedState("Accepted.")));

        session.Invalidate(100);
        var secondVersion = session.Begin(100);

        Assert.Equal(firstVersion + 1, secondVersion);
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
        Assert.False(session.TryPublish(100, firstVersion, AcceptedState("Late.")));
        Assert.True(session.IsCurrent(100, secondVersion));
    }

    [Fact]
    public void DisposedSessionKeepsItsTerminalGeneration()
    {
        var session = new CollectionDataOwnershipSession();
        var version = session.Begin(100);
        session.Dispose();

        var disposedVersion = session.Begin(200);
        session.Dispose();
        session.Invalidate(200);

        Assert.Equal(version + 1, disposedVersion);
        Assert.Equal(disposedVersion, session.Begin(300));
        Assert.False(session.IsCurrent(0, disposedVersion));
        Assert.False(session.IsCurrent(200, disposedVersion));
        Assert.False(session.TryPublish(200, disposedVersion, AcceptedState("Late.")));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
    }

    [Fact]
    public void NullPublicationIsRejectedBeforeStateMutation()
    {
        using var session = new CollectionDataOwnershipSession();
        var version = session.Begin(100);

        Assert.Throws<ArgumentNullException>(() => session.TryPublish(100, version, null!));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
    }

    private static void AssertRejected(Func<CollectionDataOwnershipSession, int, bool> publish)
    {
        using var session = new CollectionDataOwnershipSession();
        var version = session.Begin(100);
        Assert.False(publish(session, version));
        Assert.Same(CollectionDataOwnershipPolicy.NotChecked, session.State);
    }

    private static CollectionDataOwnershipState AcceptedState(string detail)
        => new(
            true,
            Exportaramus.ConsumerKit.V1_2.CollectionDataValidationCode.None,
            null,
            [123U],
            [],
            detail);
}
