namespace PrivacyEventProcessing.Domain.Models;

public record ProcessedEvent(
    string HashedUserId,
    string MaskedEmail,
    string MaskedIpAddress,
    string EventType,
    DateTime OccurredAt,
    DateTime ProcessedAt
);
