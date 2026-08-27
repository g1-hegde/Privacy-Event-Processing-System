namespace PrivacyEventProcessing.Domain.Models;

public record FailureRecord(
    FailureReasonType Reason,
    string Message,
    DateTime OccurredAt
);
