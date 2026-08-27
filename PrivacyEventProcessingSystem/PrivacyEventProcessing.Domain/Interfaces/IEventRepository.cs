using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    public interface IEventRepository
    {
        int CurrentEventCount { get; }

        ValueTask AddEventAsync(ProcessedEvent processedEvent);

        // Everything held, newest first, as a snapshot that won't change afterwards
        IReadOnlyList<ProcessedEvent> GetSnapshot();

        ValueTask ClearEventsAsync();
    }
}
