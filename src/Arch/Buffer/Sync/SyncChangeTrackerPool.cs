using System.Collections.Concurrent;
using System.Threading;

namespace Arch.Buffer.Sync;

public sealed class SyncChangeTrackerPool : IDisposable
{
    private readonly ConcurrentQueue<SyncChangeTracker> _pool;
    private readonly ConcurrentBag<SyncChangeTracker> _workers;
    private readonly Lazy<SyncChangeTracker> _main;
    private readonly int _maxPoolSize;
    private readonly int _initialTrackerCapacity;
    private int _poolCount;

    public SyncChangeTrackerPool(int maxPoolSize = 1024, int initialPoolSize = 128, int initialTrackerCapacity = 128)
    {
        _pool = new ConcurrentQueue<SyncChangeTracker>();
        _workers = new ConcurrentBag<SyncChangeTracker>();
        _maxPoolSize = maxPoolSize;
        _initialTrackerCapacity = initialTrackerCapacity;
        _poolCount = 0;

        // Pre-populate pool with initial trackers
        for (int i = 0; i < initialPoolSize; i++)
        {
            _pool.Enqueue(new SyncChangeTracker(_initialTrackerCapacity));
            _poolCount++;
        }

        _main = new Lazy<SyncChangeTracker>(() =>
        {
            if (_pool.TryDequeue(out var tracker))
            {
                Interlocked.Decrement(ref _poolCount);
                return tracker;
            }

            return new SyncChangeTracker(_initialTrackerCapacity);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the main tracker. If single-threaded, this is the only tracker used.
    /// Thread-safe lazy initialization.
    /// </summary>
    public SyncChangeTracker Main
    {
        get => _main.Value;
    }

    /// <summary>
    /// Rents a tracker from the pool (for worker threads).
    /// </summary>
    public SyncChangeTracker Rent()
    {
        if (_pool.TryDequeue(out var tracker))
        {
            Interlocked.Decrement(ref _poolCount);
            _workers.Add(tracker);
            return tracker;
        }

        tracker = new SyncChangeTracker(_initialTrackerCapacity);
        _workers.Add(tracker);
        return tracker;
    }

    /// <summary>
    /// Returns a tracker to the pool after clearing it.
    /// Respects maxPoolSize limit.
    /// </summary>
    public void Return(SyncChangeTracker tracker)
    {
        tracker.Clear();

        // Only return to pool if we haven't exceeded max size
        if (_poolCount < _maxPoolSize)
        {
            _pool.Enqueue(tracker);
            Interlocked.Increment(ref _poolCount);
        }
        // Otherwise, let it be garbage collected
    }

    /// <summary>
    /// Merges all worker trackers into the main tracker.
    /// </summary>
    public void MergeAll()
    {
        foreach (var tracker in _workers)
        {
            if (!tracker.IsEmpty)
            {
                Main.Merge(tracker);
            }
        }
    }

    /// <summary>
    /// Returns all worker trackers back to the pool.
    /// </summary>
    public void ReturnAll()
    {
        foreach (var tracker in _workers)
        {
            Return(tracker);
        }

        _workers.Clear();
    }

    /// <summary>
    /// Clears the main tracker (call after bindings have read changes).
    /// </summary>
    public void ClearMain()
    {
        if (_main.IsValueCreated)
        {
            _main.Value.Clear();
        }
    }

    public void Dispose()
    {
        foreach (var tracker in _workers)
        {
            tracker.Dispose();
        }

        _workers.Clear();

        foreach (var tracker in _pool)
        {
            tracker.Dispose();
        }

        _pool.Clear();
    }
}
