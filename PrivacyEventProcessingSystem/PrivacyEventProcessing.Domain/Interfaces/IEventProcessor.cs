using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    public interface IEventProcessor
    {
        bool IsRunning { get; }

        // Largest worker count this processor accepts, so callers can bound their own input
        int MaxWorkerCount { get; }

        // Simulated per-event work in ms. 0 means none.
        int SimulatedWorkMs { get; set; }
        Task StartProcessingAsync(int workerCount = WorkerLimits.Default, CancellationToken cancellationToken = default);
        Task StopProcessingAsync(CancellationToken cancellationToken = default);
    }
}
