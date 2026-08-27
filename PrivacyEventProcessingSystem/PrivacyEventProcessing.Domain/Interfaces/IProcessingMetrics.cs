using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    public interface IProcessingMetrics
    {
        void RecordSuccess(long elapsedTicks);
        void RecordFailure(FailureReasonType reason, string message);
        MetricsSnapshot GetSnapshot();
        IReadOnlyList<FailureRecord> GetRecentFailures(int count);
        void Reset();
    }
}
