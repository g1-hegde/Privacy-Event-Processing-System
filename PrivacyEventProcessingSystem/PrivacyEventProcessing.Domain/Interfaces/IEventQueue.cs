using PrivacyEventProcessing.Domain.Models;

namespace PrivacyEventProcessing.Domain.Interfaces
{
    public interface IEventQueue
    {
        int EventCount { get; }
        ValueTask EnqueueEventAsync(EventRequest eventRequest, CancellationToken cancellationToken = default);
        ValueTask<EventRequest> DequeueEventAsync(CancellationToken cancellationToken = default);

        // Discards everything still queued and returns how many went. Needed by Clear:
        // without it a cancelled run leaves a backlog that no counter accounts for.
        int DrainAll();
    }
}
