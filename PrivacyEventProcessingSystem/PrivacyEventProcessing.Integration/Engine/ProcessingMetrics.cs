using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using System.Collections.Concurrent;

namespace PrivacyEventProcessing.Integration.Engine
{
    // Workers only increment counters here - nothing calls back into the UI, so the refresh
    // rate stays independent of how fast events arrive.
    public class ProcessingMetrics : IProcessingMetrics
    {
        private const int MaxRecentFailures = 100;

        private long processedCount;
        private long invalidInputCount;
        private long processingErrorCount;
        private long unknownErrorCount;
        private long totalProcessingTicks;

        private readonly ConcurrentQueue<FailureRecord> recentFailures = new();

        public void RecordSuccess(long elapsedTicks)
        {
            Interlocked.Increment(ref processedCount);
            Interlocked.Add(ref totalProcessingTicks, elapsedTicks);
        }

        public void RecordFailure(FailureReasonType reason, string message)
        {
            switch (reason)
            {
                case FailureReasonType.InvalidInput:
                    Interlocked.Increment(ref invalidInputCount);
                    break;
                case FailureReasonType.ProcessingError:
                    Interlocked.Increment(ref processingErrorCount);
                    break;
                default:
                    Interlocked.Increment(ref unknownErrorCount);
                    break;
            }

            recentFailures.Enqueue(new FailureRecord(reason, message, DateTime.UtcNow));

            while (recentFailures.Count > MaxRecentFailures && recentFailures.TryDequeue(out _))
            {
            }
        }

        // Interlocked.Read because a long read isn't atomic on 32-bit
        public MetricsSnapshot GetSnapshot() => new(
            Interlocked.Read(ref processedCount),
            Interlocked.Read(ref invalidInputCount),
            Interlocked.Read(ref processingErrorCount),
            Interlocked.Read(ref unknownErrorCount),
            Interlocked.Read(ref totalProcessingTicks));

        public IReadOnlyList<FailureRecord> GetRecentFailures(int count)
        {
            if (count <= 0) return [];

            // Only OK because the queue is capped at 100
            return recentFailures.Reverse().Take(count).ToList();
        }

        public void Reset()
        {
            Interlocked.Exchange(ref processedCount, 0);
            Interlocked.Exchange(ref invalidInputCount, 0);
            Interlocked.Exchange(ref processingErrorCount, 0);
            Interlocked.Exchange(ref unknownErrorCount, 0);
            Interlocked.Exchange(ref totalProcessingTicks, 0);
            recentFailures.Clear();
        }
    }
}
