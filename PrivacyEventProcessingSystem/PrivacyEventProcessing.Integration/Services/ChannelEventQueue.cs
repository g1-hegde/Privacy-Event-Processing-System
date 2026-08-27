using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using System.Threading.Channels;

namespace PrivacyEventProcessing.Integration.Services
{
    public class ChannelEventQueue : IEventQueue
    {

        private readonly Channel<EventRequest> channel;

        public int EventCount => channel.Reader.Count;

        public ChannelEventQueue()
        {
            BoundedChannelOptions boundedChannelOptions = new(10000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            channel = Channel.CreateBounded<EventRequest>(boundedChannelOptions);
        }

        public ValueTask<EventRequest> DequeueEventAsync(CancellationToken cancellationToken = default)
        {
            return channel.Reader.ReadAsync(cancellationToken);
        }

        public ValueTask EnqueueEventAsync(EventRequest eventRequest, CancellationToken cancellationToken = default)
        {
            return channel.Writer.WriteAsync(eventRequest, cancellationToken);
        }

        // TryRead rather than completing the writer, so the queue stays usable afterwards.
        // A worker racing this just takes an event before it is discarded, which is fine -
        // either way it leaves the queue.
        public int DrainAll()
        {
            int discarded = 0;

            while (channel.Reader.TryRead(out _))
            {
                discarded++;
            }

            return discarded;
        }
    }
}
