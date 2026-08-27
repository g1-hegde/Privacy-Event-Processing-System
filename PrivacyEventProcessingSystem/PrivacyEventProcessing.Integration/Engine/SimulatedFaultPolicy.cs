using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Integration.Engine
{
    // Injects the simulated failures. One roll per event decides both whether it fails and
    // which category, so the configured rates read as a share of all events.
    // Thread safe: rates are immutable and Random.Shared is safe for concurrent use.
    public sealed class SimulatedFaultPolicy : IFaultPolicy
    {
        // Varied so the dashboard shows something other than 100 identical rows
        private static readonly string[] ProcessingErrorMessages =
        [
            "Downstream enrichment service timed out after 2000 ms.",
            "Consent lookup returned 503 Service Unavailable.",
            "Geo-IP provider rejected the request: rate limit exceeded.",
            "Retention policy service returned a malformed response.",
            "Audit sink refused the write: batch already committed.",
        ];

        private static readonly string[] UnknownErrorMessages =
        [
            "Unexpected null in the enrichment response payload.",
            "Arithmetic overflow while computing the retention window.",
        ];

        private readonly double processingErrorRate;
        private readonly double anyFaultRate;

        public SimulatedFaultPolicy(FaultInjectionOptions options)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(options.ProcessingErrorRate);
            ArgumentOutOfRangeException.ThrowIfNegative(options.UnknownErrorRate);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TotalRate, 1.0);

            processingErrorRate = options.ProcessingErrorRate;
            anyFaultRate = options.TotalRate;
        }

        public ProcessingFault? NextFault()
        {
            // Adjacent bands off a single roll so the two rates can't overlap
            double roll = Random.Shared.NextDouble();

            if (roll < processingErrorRate)
            {
                return new ProcessingFault(
                    FailureReasonType.ProcessingError,
                    Pick(ProcessingErrorMessages));
            }

            if (roll < anyFaultRate)
            {
                return new ProcessingFault(
                    FailureReasonType.UnknownError,
                    Pick(UnknownErrorMessages));
            }

            return null;
        }

        private static string Pick(string[] messages) => messages[Random.Shared.Next(messages.Length)];
    }
}
