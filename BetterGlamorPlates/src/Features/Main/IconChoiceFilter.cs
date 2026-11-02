using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Features.Main;

/// <summary>Caches a projection of an immutable icon catalog until its input changes.</summary>
internal sealed class IconChoiceFilter
{
    private IReadOnlyList<GameIconOption>? source;
    private string? category;
    private string? search;
    internal GameIconOption[] Items { get; private set; } = [];

    internal bool Update(IReadOnlyList<GameIconOption> choices, string selectedCategory, string query)
    {
        if (ReferenceEquals(source, choices) && category == selectedCategory && search == query) return false;
        Items = choices.Where(icon => icon.Category == selectedCategory
            && icon.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        source = choices;
        category = selectedCategory;
        search = query;
        return true;
    }
}
