namespace PrivacyEventProcessing.Domain.Models;

public sealed record ProcessingFault(FailureReasonType Reason, string Message);
