using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Services;

namespace PrivacyEventProcessing.Test
{
    public class ChannelEventQueueTests
    {
        private static EventRequest CreateEvent(int i) => new()
        {
            UserId = $"U{i}",
            Email = $"user{i}@example.com",
            IpAddress = "192.168.1.10",
            EventType = "Login"
        };

        [Fact]
        public async Task DrainAll_EmptiesTheQueueAndReturnsWhatItDiscarded()
        {
            var queue = new ChannelEventQueue();

            for (int i = 0; i < 250; i++)
            {
                await queue.EnqueueEventAsync(CreateEvent(i));
            }

            Assert.Equal(250, queue.DrainAll());
            Assert.Equal(0, queue.EventCount);
        }

        [Fact]
        public void DrainAll_ReturnsZeroWhenAlreadyEmpty()
        {
            var queue = new ChannelEventQueue();

            Assert.Equal(0, queue.DrainAll());
        }

        // Clear has to leave the queue usable - the user can start another run straight after
        [Fact]
        public async Task DrainAll_LeavesTheQueueWritableAndReadable()
        {
            var queue = new ChannelEventQueue();

            await queue.EnqueueEventAsync(CreateEvent(1));
            queue.DrainAll();

            await queue.EnqueueEventAsync(CreateEvent(2));

            Assert.Equal(1, queue.EventCount);
            EventRequest dequeued = await queue.DequeueEventAsync();
            Assert.Equal("U2", dequeued.UserId);
        }

        // The backlog a cancelled run leaves is what Clear discards, so the count the UI
        // shows before the drain has to match what the drain reports
        [Fact]
        public async Task EventCount_MatchesWhatDrainAllDiscards()
        {
            var queue = new ChannelEventQueue();

            for (int i = 0; i < 60; i++)
            {
                await queue.EnqueueEventAsync(CreateEvent(i));
            }

            int reported = queue.EventCount;

            Assert.Equal(reported, queue.DrainAll());
        }
    }
}
