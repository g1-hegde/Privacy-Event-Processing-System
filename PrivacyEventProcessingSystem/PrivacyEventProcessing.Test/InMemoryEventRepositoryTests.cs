using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Storage;

namespace PrivacyEventProcessing.Test
{
    public class InMemoryEventRepositoryTests
    {
        private static ProcessedEvent CreateEvent(int index) => new(
            $"hash-{index}",
            "j***n@example.com",
            "192.168.1.xxx",
            "Login",
            DateTime.UtcNow,
            DateTime.UtcNow);

        [Fact]
        public async Task AddEventAsync_IsSafeUnderConcurrentWriters()
        {
            var repository = new InMemoryEventRepository();

            await Parallel.ForEachAsync(
                Enumerable.Range(0, 5000),
                new ParallelOptions { MaxDegreeOfParallelism = 16 },
                async (i, _) => await repository.AddEventAsync(CreateEvent(i)));

            Assert.Equal(5000, repository.CurrentEventCount);
        }

        [Fact]
        public async Task GetSnapshot_ReturnsEveryCachedEventNewestFirst()
        {
            var repository = new InMemoryEventRepository();
            for (int i = 0; i < 500; i++)
            {
                await repository.AddEventAsync(CreateEvent(i));
            }

            IReadOnlyList<ProcessedEvent> snapshot = repository.GetSnapshot();

            // Everything held, not just what the dashboard shows
            Assert.Equal(500, snapshot.Count);
            Assert.Equal("hash-499", snapshot[0].HashedUserId);
            Assert.Equal("hash-0", snapshot[^1].HashedUserId);
        }

        // Why it hands out a copy: the UI can scroll it without workers shifting rows
        [Fact]
        public async Task GetSnapshot_IsNotAffectedByLaterWrites()
        {
            var repository = new InMemoryEventRepository();
            await repository.AddEventAsync(CreateEvent(0));

            IReadOnlyList<ProcessedEvent> snapshot = repository.GetSnapshot();

            for (int i = 1; i < 100; i++)
            {
                await repository.AddEventAsync(CreateEvent(i));
            }

            Assert.Single(snapshot);
            Assert.Equal("hash-0", snapshot[0].HashedUserId);
        }

        [Fact]
        public async Task GetSnapshot_IsSafeWhileWritersAreRunning()
        {
            var repository = new InMemoryEventRepository();
            using var writing = new CancellationTokenSource();

            Task writer = Task.Run(async () =>
            {
                int i = 0;
                while (!writing.IsCancellationRequested)
                {
                    await repository.AddEventAsync(CreateEvent(i++));
                }
            });

            // Any length is fine mid-write, but it has to be consistent - no nulls, no throw
            for (int attempt = 0; attempt < 200; attempt++)
            {
                IReadOnlyList<ProcessedEvent> snapshot = repository.GetSnapshot();
                Assert.All(snapshot, stored => Assert.NotNull(stored.HashedUserId));
            }

            await writing.CancelAsync();
            await writer;
        }

        [Fact]
        public async Task GetSnapshot_IsEmptyAfterClear()
        {
            var repository = new InMemoryEventRepository();
            for (int i = 0; i < 10; i++)
            {
                await repository.AddEventAsync(CreateEvent(i));
            }

            await repository.ClearEventsAsync();

            Assert.Empty(repository.GetSnapshot());
        }

        [Fact]
        public async Task ClearEventsAsync_ResetsTheCountAndLeavesTheStoreWritable()
        {
            var repository = new InMemoryEventRepository();
            for (int i = 0; i < 10; i++)
            {
                await repository.AddEventAsync(CreateEvent(i));
            }

            await repository.ClearEventsAsync();

            Assert.Equal(0, repository.CurrentEventCount);

            // Clear must not leave the store in a state that swallows later writes
            await repository.AddEventAsync(CreateEvent(99));
            IReadOnlyList<ProcessedEvent> snapshot = repository.GetSnapshot();

            Assert.Equal(1, repository.CurrentEventCount);
            Assert.Single(snapshot);
            Assert.Equal("hash-99", snapshot[0].HashedUserId);
        }
    }
}
