namespace Exportaramus.Bootstrap;

/// <summary>Coalesces configuration writes using immutable value copies and a host-supplied exact path.</summary>
public sealed class ConfigurationPersistence : IAsyncDisposable
{
    private readonly ConfigurationWriteCoordinator coordinator;
    public ConfigurationPersistence(string exactPath)
    {
        var typeName = typeof(Configuration).FullName + ", " + typeof(Configuration).Assembly.GetName().Name;
        coordinator = new(value => Task.Run(() => ConfigurationFile.WriteAsync(exactPath, ConfigurationFile.Serialize(value, typeName))));
    }
    public void Request(bool reduceMotion) => coordinator.Request(reduceMotion);
    public TaskStatus? WorkerStatus => coordinator.WorkerStatus;
    public void Update() => coordinator.Update();
    public ValueTask DisposeAsync() => coordinator.DisposeAsync();
}
