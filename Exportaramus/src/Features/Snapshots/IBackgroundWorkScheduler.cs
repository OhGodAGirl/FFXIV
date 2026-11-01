namespace Exportaramus.Features.Snapshots;

/// <summary>Runs background work separately from controller updates and publication.</summary>
public interface IBackgroundWorkScheduler
{
    Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
