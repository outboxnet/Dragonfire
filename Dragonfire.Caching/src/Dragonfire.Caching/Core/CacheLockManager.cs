using System.Collections.Concurrent;

namespace Dragonfire.Caching.Core;

/// <summary>
/// In-process per-key semaphore locks for preventing cache stampedes.
/// Suitable for single-node deployments. For distributed locking, use a
/// Redis-based implementation (e.g. RedLock.net) and register your own
/// abstraction in DI.
/// </summary>
public sealed class CacheLockManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Acquires an exclusive lock for <paramref name="key"/>.
    /// Dispose the returned handle to release the lock.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        return new LockHandle(sem);
    }

    private sealed class LockHandle : IDisposable
    {
        private readonly SemaphoreSlim _sem;
        private bool _disposed;

        public LockHandle(SemaphoreSlim sem) => _sem = sem;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sem.Release();
        }
    }
}
