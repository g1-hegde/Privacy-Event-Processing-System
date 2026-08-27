using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using System.Collections.Concurrent;

namespace PrivacyEventProcessing.Integration.Storage
{
    // Writes are the hot path (one per event, every worker, thousands a second) and reads
    // are the cold path (the dashboard, twice a second). ConcurrentQueue gives lock-free
    // writes; ToArray on read is O(n) and allocates, which is the trade worth making.
    public class InMemoryEventRepository : IEventRepository
    {
        private const int MaxCacheSize = 12000; // 10k batch + 2000 headroom

        private readonly ConcurrentQueue<ProcessedEvent> cache = new();
        private int currentEventCount;

        public int CurrentEventCount => Volatile.Read(ref currentEventCount);

        public ValueTask AddEventAsync(ProcessedEvent processedEvent)
        {
            cache.Enqueue(processedEvent);
            Interlocked.Increment(ref currentEventCount);

            // Drop oldest at the cap. Several writers can each see the cap exceeded and each
            // dequeue, so the count can sit a little under it - fine for a ceiling, and it's
            // what not taking a lock costs.
            while (Volatile.Read(ref currentEventCount) > MaxCacheSize && cache.TryDequeue(out _))
            {
                Interlocked.Decrement(ref currentEventCount);
            }

            return ValueTask.CompletedTask;
        }

        // Everything held, newest first. ToArray snapshots without blocking writers, so the
        // list the CollectionView is bound to can't shift under the user mid-scroll.
        public IReadOnlyList<ProcessedEvent> GetSnapshot()
        {
            ProcessedEvent[] items = cache.ToArray();
            Array.Reverse(items);
            return items;
        }

        public ValueTask ClearEventsAsync()
        {
            cache.Clear();
            Interlocked.Exchange(ref currentEventCount, 0);

            return ValueTask.CompletedTask;
        }
    }
}
