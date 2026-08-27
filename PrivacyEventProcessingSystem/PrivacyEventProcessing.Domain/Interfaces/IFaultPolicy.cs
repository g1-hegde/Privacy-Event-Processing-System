using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    // Decides whether the next event should fail and how. Injected rather than hardcoded in
    // the worker so tests can pin the outcome instead of asserting a statistical range.
    public interface IFaultPolicy
    {
        // Null lets the event succeed. Called from every worker, so must be thread safe.
        ProcessingFault? NextFault();
    }
}
