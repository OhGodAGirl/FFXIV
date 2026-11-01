namespace Exportaramus.Features.Snapshots;

/// <summary>Owns asynchronous work independently of the controller's update and publication steps.</summary>
public interface IBackgroundWorkScheduler
{
    Task<T> Run<T>(Func<Task<T>> work, CancellationToken cancellationToken);
}
