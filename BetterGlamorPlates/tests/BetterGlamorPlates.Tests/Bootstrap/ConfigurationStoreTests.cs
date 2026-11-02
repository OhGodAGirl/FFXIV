using BetterGlamorPlates.Bootstrap;
using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Tests.Bootstrap;

/// <summary>Verifies real configuration mapping with an injected persistence boundary.</summary>
public sealed class ConfigurationStoreTests
{
    /// <summary>Absent or unrelated serialized configuration becomes an empty versioned library.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrWrongConfigurationStartsEmpty(bool wrongType)
    {
        object? configuration = wrongType ? "old unrelated data" : null;
        Configuration? saved = null;
        var store = new ConfigurationStore(() => configuration, value => saved = value);
        var state = store.Load();
        Assert.Empty(state.SavedPlates);
        Assert.Null(state.SelectedPlateId);
        Assert.Null(saved);
        store.Save(state);
        Assert.Equal(1, saved!.Version);
    }

    /// <summary>Legacy null collections normalize without discarding selection or writing during load.</summary>
    [Fact]
    public void LegacyNullLibraryNormalizesWithoutSaving()
    {
        var configuration = new Configuration { SavedPlates = null!, SelectedPlateId = Guid.NewGuid(), Version = 7 };
        var writes = 0;
        var store = new ConfigurationStore(() => configuration, _ => writes++);
        var state = store.Load();
        Assert.Empty(state.SavedPlates);
        Assert.Equal(configuration.SelectedPlateId, state.SelectedPlateId);
        Assert.Equal(7, configuration.Version);
        Assert.Equal(0, writes);
    }

    /// <summary>Save writes the complete replacement list and selection into the original configuration.</summary>
    [Fact]
    public void SaveMapsBothFieldsAndPreservesSchema()
    {
        var original = new SavedPlate { Id = Guid.NewGuid(), Name = "Original" };
        var configuration = new Configuration { SavedPlates = [original], SelectedPlateId = original.Id, Version = 3 };
        Configuration? written = null;
        var store = new ConfigurationStore(() => configuration, value => written = value);
        Assert.Same(original, Assert.Single(store.Load().SavedPlates));
        var replacement = new SavedPlate { Id = Guid.NewGuid(), Name = "Replacement" };
        var state = new PlateLibraryState([replacement], replacement.Id);

        store.Save(state);

        Assert.Same(configuration, written);
        Assert.Same(state.SavedPlates, written!.SavedPlates);
        Assert.Equal(replacement.Id, written.SelectedPlateId);
        Assert.Equal(3, written.Version);
        Assert.Same(state.SavedPlates, store.Load().SavedPlates);
        Assert.Equal(state.SelectedPlateId, store.Load().SelectedPlateId);
    }

    /// <summary>Persistence failures propagate to the controller rollback boundary rather than claiming success.</summary>
    [Fact]
    public void WriteFailurePropagatesWithoutRetry()
    {
        var writes = 0;
        var failure = new IOException("disk unavailable");
        var store = new ConfigurationStore(() => null, _ => { writes++; throw failure; });
        Assert.Same(failure, Assert.Throws<IOException>(() => store.Save(new([], null))));
        Assert.Equal(1, writes);
    }

    /// <summary>Read failures propagate without constructing or persisting a replacement over unreadable data.</summary>
    [Fact]
    public void ReadFailureNeverWritesAnEmptyReplacement()
    {
        var writes = 0;
        var failure = new IOException("unreadable");
        Assert.Same(failure, Assert.Throws<IOException>(() => new ConfigurationStore(() => throw failure, _ => writes++)));
        Assert.Equal(0, writes);
    }
}
