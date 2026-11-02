namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Verifies native loading only chooses an exact, currently available source.
/// </summary>
public sealed class CollectionSourceResolverTests
{
    /// <summary>
    /// Verifies programming errors at the source boundary fail explicitly.
    /// </summary>
    [Fact]
    public void ResolveExactRejectsNullDependencies()
    {
        // Arrange
        var saved = new SavedPlateItem { ItemId = 100 };

        // Act
        var nullCollection = () => CollectionSourceResolver.ResolveExact(null!, saved);
        var nullItem = () => CollectionSourceResolver.ResolveExact([], null!);

        // Assert
        Assert.Equal("collection", Assert.Throws<ArgumentNullException>(nullCollection).ParamName);
        Assert.Equal("item", Assert.Throws<ArgumentNullException>(nullItem).ParamName);
    }

    /// <summary>
    /// Verifies a dyed saved item cannot silently fall back to an undyed Armoire item.
    /// </summary>
    [Fact]
    public void DyedItemDoesNotFallBackToArmoire()
    {
        // Arrange
        NativeCollectionItem[] collection =
        [
            new(100, CollectionSource.Armoire, 9, 0, 0),
        ];
        var saved = new SavedPlateItem { ItemId = 100, Stain0Id = 5 };

        // Act
        var result = CollectionSourceResolver.ResolveExact(collection, saved);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies an exact Dresser dye match is preferred over the Armoire.
    /// </summary>
    [Fact]
    public void ExactDresserDyeMatchWins()
    {
        // Arrange
        NativeCollectionItem dresser = new(100, CollectionSource.Dresser, 44, 5, 6);
        NativeCollectionItem[] collection =
        [
            new(100, CollectionSource.Armoire, 9, 0, 0),
            dresser,
        ];
        var saved = new SavedPlateItem { ItemId = 100, Stain0Id = 5, Stain1Id = 6 };

        // Act
        var result = CollectionSourceResolver.ResolveExact(collection, saved);

        // Assert
        Assert.Same(dresser, result);
    }

    /// <summary>
    /// Verifies an undyed item may resolve from the currently loaded Armoire.
    /// </summary>
    [Fact]
    public void UndyedItemMayUseArmoire()
    {
        // Arrange
        NativeCollectionItem armoire = new(100, CollectionSource.Armoire, 9, 0, 0);
        var saved = new SavedPlateItem { ItemId = 100 };

        // Act
        var result = CollectionSourceResolver.ResolveExact([armoire], saved);

        // Assert
        Assert.Same(armoire, result);
    }

    /// <summary>
    /// Verifies an unavailable undyed item does not resolve to another item.
    /// </summary>
    [Fact]
    public void UndyedItemDoesNotResolveAnotherItem()
    {
        // Arrange
        NativeCollectionItem[] collection =
        [
            new(101, CollectionSource.Dresser, 8, 0, 0),
            new(102, CollectionSource.Armoire, 9, 0, 0),
        ];
        var saved = new SavedPlateItem { ItemId = 100 };

        // Act
        var result = CollectionSourceResolver.ResolveExact(collection, saved);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies generated decoy collections never resolve an item or dye that
    /// differs from the saved appearance, and prefer an exact Dresser source.
    /// </summary>
    [Fact]
    public void GeneratedDecoysNeverResolveTheWrongAppearance()
    {
        var random = new Random(701);
        for (var sample = 0; sample < 200; sample++)
        {
            var itemId = (uint)random.Next(1, 100_000);
            var stain0 = (byte)random.Next(0, 16);
            var stain1 = (byte)random.Next(0, 16);
            var saved = new SavedPlateItem
            {
                ItemId = itemId,
                Stain0Id = stain0,
                Stain1Id = stain1,
            };
            var decoys = Enumerable.Range(0, random.Next(0, 80))
                .Select(index => new NativeCollectionItem(
                    itemId + (uint)index + 1,
                    index % 2 == 0 ? CollectionSource.Dresser : CollectionSource.Armoire,
                    (uint)index,
                    (byte)random.Next(0, 16),
                    (byte)random.Next(0, 16)))
                .ToList();

            NativeCollectionItem? expected = null;
            if (sample % 3 == 0)
            {
                expected = new NativeCollectionItem(
                    itemId,
                    CollectionSource.Dresser,
                    900_000u + (uint)sample,
                    stain0,
                    stain1);
                decoys.Insert(random.Next(0, decoys.Count + 1), expected);
            }
            else if (stain0 == 0 && stain1 == 0 && sample % 3 == 1)
            {
                expected = new NativeCollectionItem(
                    itemId,
                    CollectionSource.Armoire,
                    800_000u + (uint)sample,
                    0,
                    0);
                decoys.Insert(random.Next(0, decoys.Count + 1), expected);
            }

            var resolved = CollectionSourceResolver.ResolveExact(decoys, saved);

            Assert.Same(expected, resolved);
        }
    }
}
