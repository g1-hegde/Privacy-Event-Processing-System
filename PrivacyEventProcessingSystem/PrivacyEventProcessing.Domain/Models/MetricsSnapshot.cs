namespace PrivacyEventProcessing.Domain.Models;

public readonly record struct MetricsSnapshot(
    long ProcessedCount,
    long InvalidInputCount,
    long ProcessingErrorCount,
    long UnknownErrorCount,
    long TotalProcessingTicks
)
{
    public long FailedCount => InvalidInputCount + ProcessingErrorCount + UnknownErrorCount;

    public long TotalCount => ProcessedCount + FailedCount;

    // Running total rather than a list of samples, so cost doesn't grow with event count
    public double AverageProcessingTimeMs =>
        ProcessedCount == 0
            ? 0
            : (double)TotalProcessingTicks / ProcessedCount / TimeSpan.TicksPerMillisecond;
}
