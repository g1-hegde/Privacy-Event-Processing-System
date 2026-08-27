namespace PrivacyEventProcessing.Domain.Models;

// Split of the simulated ~5% failure rate. Rates are a share of all processed events, not
// of the failures. InvalidInput isn't here - it comes from the generator's malformed events
// (~1%) and goes through real validation. 0.01 + 0.99 * 0.04 = 4.96% overall.
public sealed record FaultInjectionOptions
{
    public double ProcessingErrorRate { get; init; } = 0.035;

    public double UnknownErrorRate { get; init; } = 0.005;

    public double TotalRate => ProcessingErrorRate + UnknownErrorRate;
}
