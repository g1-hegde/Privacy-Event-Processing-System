namespace PrivacyEventProcessing.Domain.Models;

// Single source for the worker bounds - the pool enforces them and the dashboard stepper
// binds to them, so the UI can't offer a value the pool would reject.
public static class WorkerLimits
{
    public const int Default = 5;

    public const int Minimum = 1;

    // Not ProcessorCount: workers spend their time awaiting the channel, not on the CPU.
    public const int Maximum = 99;
}
