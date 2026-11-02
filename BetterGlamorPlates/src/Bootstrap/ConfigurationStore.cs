using BetterGlamorPlates.Features.GlamourPlates;

namespace BetterGlamorPlates.Bootstrap;

internal sealed partial class ConfigurationStore : IPlateLibraryStore
{
    private readonly Action<Configuration> persist;
    private readonly Configuration configuration;

    internal ConfigurationStore(Func<object?> load, Action<Configuration> persist)
    {
        this.persist = persist;
        configuration = load() as Configuration ?? new Configuration();
        configuration.SavedPlates ??= [];
    }

    public PlateLibraryState Load()
        => new(configuration.SavedPlates, configuration.SelectedPlateId);

    public void Save(PlateLibraryState state)
    {
        configuration.SavedPlates = state.SavedPlates;
        configuration.SelectedPlateId = state.SelectedPlateId;
        persist(configuration);
    }
}
