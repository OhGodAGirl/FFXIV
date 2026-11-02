namespace BetterGlamorPlates.Tests;

using BetterGlamorPlates.Features.GlamourPlates;

/// <summary>
/// Verifies the unlimited local plate library keeps configurations independent.
/// </summary>
public sealed class PlateLibraryTests
{
    /// <summary>
    /// Verifies a duplicated plate does not share mutable slot items with its source.
    /// </summary>
    [Fact]
    public void DuplicateDeepCopiesSlotItems()
    {
        // Arrange
        var original = CreatePlate("Original");
        original.Items[3] = new SavedPlateItem
        {
            ItemId = 123,
            Stain0Id = 4,
            Stain1Id = 5,
        };

        // Act
        var duplicate = PlateLibrary.Duplicate(
            original,
            TestTime,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        duplicate.Items[3]!.ItemId = 456;

        // Assert
        Assert.Equal(123u, original.Items[3]!.ItemId);
        Assert.Equal(456u, duplicate.Items[3]!.ItemId);
        Assert.NotEqual(original.Id, duplicate.Id);
    }

    /// <summary>
    /// Verifies malformed older arrays are safely normalized to twelve native slots.
    /// </summary>
    [Fact]
    public void EnsureShapePreservesKnownSlots()
    {
        // Arrange
        var plate = CreatePlate("Legacy");
        plate.Items =
        [
            new SavedPlateItem { ItemId = 77 },
        ];

        // Act
        PlateLibrary.EnsureShape(plate);

        // Assert
        Assert.Equal(12, plate.Items.Length);
        Assert.Equal(77u, plate.Items[0]!.ItemId);
        Assert.Null(plate.Items[11]);
    }

    /// <summary>
    /// Verifies blank user input receives a useful local name.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeNameRejectsBlankNames(string? value)
    {
        // Arrange and Act
        var normalized = PlateLibrary.NormalizeName(value);

        // Assert
        Assert.Equal("New Plate", normalized);
    }

    /// <summary>
    /// Verifies null legacy item storage becomes a twelve-slot array.
    /// </summary>
    [Fact]
    public void EnsureShapeRepairsNullLegacyItems()
    {
        // Arrange
        var plate = CreatePlate("Legacy null");
        plate.Items = null!;

        // Act
        PlateLibrary.EnsureShape(plate);

        // Assert
        Assert.Equal(12, plate.Items.Length);
        Assert.All(plate.Items, item => Assert.Null(item));
    }

    /// <summary>
    /// Verifies a canonical array is retained rather than replaced.
    /// </summary>
    [Fact]
    public void EnsureShapeKeepsCanonicalArrayIdentity()
    {
        // Arrange
        var plate = CreatePlate("Canonical");
        var items = plate.Items;

        // Act
        PlateLibrary.EnsureShape(plate);

        // Assert
        Assert.Same(items, plate.Items);
    }

    /// <summary>
    /// Verifies oversized legacy arrays retain only the native twelve slots.
    /// </summary>
    [Fact]
    public void EnsureShapeTruncatesOversizedLegacyItems()
    {
        // Arrange
        var plate = CreatePlate("Oversized");
        plate.Items = Enumerable.Range(1, 13)
            .Select(id => new SavedPlateItem { ItemId = (uint)id })
            .ToArray();

        // Act
        PlateLibrary.EnsureShape(plate);

        // Assert
        Assert.Equal(12, plate.Items.Length);
        Assert.Equal(12u, plate.Items[11]!.ItemId);
    }

    /// <summary>
    /// Verifies custom duplicate names are normalized and empty slots stay empty.
    /// </summary>
    [Fact]
    public void DuplicateUsesNormalizedCustomNameAndPreservesEmptySlots()
    {
        // Arrange
        var original = CreatePlate("Original");

        // Act
        var duplicate = PlateLibrary.Duplicate(
            original,
            TestTime,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "  Custom  ");

        // Assert
        Assert.Equal("Custom", duplicate.Name);
        Assert.All(duplicate.Items, item => Assert.Null(item));
    }

    /// <summary>Verifies the default duplicate name clearly identifies its source.</summary>
    [Fact]
    public void DuplicateUsesSourceNameWithCopySuffixByDefault()
    {
        var original = CreatePlate("Original");

        var duplicate = PlateLibrary.Duplicate(
            original,
            TestTime,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.Equal("Original Copy", duplicate.Name);
    }

    /// <summary>
    /// Verifies duplicate repairs null legacy storage without mutating its source.
    /// </summary>
    [Fact]
    public void DuplicateRepairsNullLegacyStorage()
    {
        // Arrange
        var original = CreatePlate("Legacy null");
        original.Items = null!;

        // Act
        var duplicate = PlateLibrary.Duplicate(
            original,
            TestTime,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        // Assert
        Assert.Null(original.Items);
        Assert.Equal(12, duplicate.Items.Length);
        Assert.All(duplicate.Items, item => Assert.Null(item));
    }

    /// <summary>
    /// Verifies a null source is rejected explicitly.
    /// </summary>
    [Fact]
    public void DuplicateRejectsNullSource()
    {
        // Arrange and Act
        var duplicate = () => PlateLibrary.Duplicate(
            null!,
            TestTime,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));

        // Assert
        Assert.Throws<ArgumentNullException>(duplicate);
    }

    /// <summary>
    /// Verifies a nonblank name is trimmed without changing its content.
    /// </summary>
    [Fact]
    public void NormalizeNameTrimsNonblankName()
    {
        // Arrange and Act
        var normalized = PlateLibrary.NormalizeName("  My Plate  ");

        // Assert
        Assert.Equal("My Plate", normalized);
    }

    /// <summary>
    /// Verifies every legacy array length from empty through oversized is
    /// normalized to the native shape while preserving the supported prefix.
    /// </summary>
    [Fact]
    public void GeneratedLegacyShapesPreserveTheSupportedPrefix()
    {
        for (var length = 0; length <= 64; length++)
        {
            var plate = CreatePlate($"Legacy {length}");
            plate.Items = Enumerable.Range(1, length)
                .Select(id => new SavedPlateItem { ItemId = (uint)id })
                .ToArray();

            PlateLibrary.EnsureShape(plate);

            Assert.Equal(PlateRules.SlotCount, plate.Items.Length);
            var preserved = Math.Min(length, PlateRules.SlotCount);
            for (var index = 0; index < preserved; index++)
                Assert.Equal((uint)(index + 1), plate.Items[index]!.ItemId);
            for (var index = preserved; index < PlateRules.SlotCount; index++)
                Assert.Null(plate.Items[index]);
        }
    }

    /// <summary>
    /// Verifies normalization is idempotent across deterministic generated
    /// names, including surrounding whitespace and empty values.
    /// </summary>
    [Fact]
    public void GeneratedNamesNormalizeIdempotently()
    {
        var random = new Random(20260901);
        for (var sample = 0; sample < 250; sample++)
        {
            var length = random.Next(0, 80);
            var body = new string(Enumerable.Range(0, length)
                .Select(_ => (char)random.Next('a', 'z' + 1))
                .ToArray());
            var generated = sample % 5 == 0
                ? new string(' ', random.Next(0, 12))
                : $"  {body}  ";

            var once = PlateLibrary.NormalizeName(generated);
            var twice = PlateLibrary.NormalizeName(once);

            Assert.Equal(once, twice);
            Assert.False(string.IsNullOrWhiteSpace(once));
            Assert.Equal(once.Trim(), once);
        }
    }

    private static DateTime TestTime { get; } = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SavedPlate CreatePlate(string name)
        => PlateLibrary.Create(
            name,
            TestTime,
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
}
