namespace PrivacyEventProcessing.Domain.Models;

// Hash key, supplied by the composition root rather than baked into PrivacyService.
public record PrivacyOptions(string HashKey);
