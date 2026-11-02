using System.Runtime.ExceptionServices;

namespace BetterGlamorPlates.Shared.Operations;

/// <summary>Owns two resources, including partial construction and failing reverse-order cleanup.</summary>
internal sealed class OwnedResourcePair<T> : IDisposable where T : IDisposable
{
    private bool disposed;
    internal T First { get; }
    internal T Second { get; }

    internal OwnedResourcePair(Func<T> createFirst, Func<T> createSecond)
    {
        First = createFirst();
        try
        {
            Second = createSecond();
        }
        catch (Exception creationFailure)
        {
            try { First.Dispose(); }
            catch (Exception releaseFailure) { throw new AggregateException(creationFailure, releaseFailure); }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>(2);
        try { Second.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        try { First.Dispose(); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}
