using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Integration.Engine;
using PrivacyEventProcessing.Integration.Services;
using PrivacyEventProcessing.Integration.Storage;
using PrivacyEventProcessing.MockData;
using System.Diagnostics;

namespace PrivacyEventProcessing.Test
{
    public class BackgroundWorkerPoolTests
    {
        private static (BackgroundWorkerPool pool, ChannelEventQueue queue, InMemoryEventRepository repository, ProcessingMetrics metrics) CreatePipeline(
            IFaultPolicy? faultPolicy = null)
        {
            var queue = new ChannelEventQueue();
            var repository = new InMemoryEventRepository();
            var metrics = new ProcessingMetrics();
            var privacyService = new PrivacyService(new PrivacyOptions("test-hash-key"));
            var pool = new BackgroundWorkerPool(
                queue,
                repository,
                privacyService,
                metrics,
                faultPolicy ?? new SimulatedFaultPolicy(new FaultInjectionOptions()));

            return (pool, queue, repository, metrics);
        }

        // Pins the outcome so tests assert exact counts, not a statistical range
        private sealed class FixedFaultPolicy(ProcessingFault? fault) : IFaultPolicy
        {
            public ProcessingFault? NextFault() => fault;
        }

        private static IFaultPolicy NeverFails() => new FixedFaultPolicy(null);

        private static async Task<bool> WaitForTotalAsync(ProcessingMetrics metrics, long expected, TimeSpan timeout)
        {
            long deadline = Stopwatch.GetTimestamp();

            while (Stopwatch.GetElapsedTime(deadline) < timeout)
            {
                if (metrics.GetSnapshot().TotalCount >= expected) return true;
                await Task.Delay(25);
            }

            return metrics.GetSnapshot().TotalCount >= expected;
        }

        [Fact]
        public async Task EveryQueuedEventIsAccountedForAsSuccessOrFailure()
        {
            const int eventCount = 2000;
            var (pool, queue, _, metrics) = CreatePipeline();
            var generator = new MockDataGenerator(seed: 7);

            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(eventCount))
            {
                await queue.EnqueueEventAsync(request);
            }

            Assert.True(await WaitForTotalAsync(metrics, eventCount, TimeSpan.FromSeconds(30)));
            await pool.StopProcessingAsync();

            MetricsSnapshot snapshot = metrics.GetSnapshot();
            Assert.Equal(eventCount, snapshot.TotalCount);
        }

        [Fact]
        public async Task EventsFailAtRoughlyTheSimulatedRate()
        {
            const int eventCount = 5000;
            var (pool, queue, _, metrics) = CreatePipeline();
            var generator = new MockDataGenerator(seed: 11);

            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(eventCount))
            {
                await queue.EnqueueEventAsync(request);
            }

            Assert.True(await WaitForTotalAsync(metrics, eventCount, TimeSpan.FromSeconds(60)));
            await pool.StopProcessingAsync();

            MetricsSnapshot snapshot = metrics.GetSnapshot();

            // No malformed events requested, so validation rejects nothing
            Assert.Equal(0, snapshot.InvalidInputCount);

            double failureRate = (double)snapshot.FailedCount / eventCount;
            Assert.InRange(failureRate, 0.03, 0.07);
        }

        // The ~5% budget is split across all three reason types, not dumped in one bucket
        [Fact]
        public async Task SimulatedFailuresAreSpreadAcrossAllThreeReasonTypes()
        {
            const int eventCount = 5000;
            var (pool, queue, _, metrics) = CreatePipeline();
            var generator = new MockDataGenerator(seed: 11);

            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(
                eventCount, MockDataGenerator.DefaultMalformedRatio))
            {
                await queue.EnqueueEventAsync(request);
            }

            Assert.True(await WaitForTotalAsync(metrics, eventCount, TimeSpan.FromSeconds(60)));
            await pool.StopProcessingAsync();

            MetricsSnapshot snapshot = metrics.GetSnapshot();

            Assert.True(snapshot.InvalidInputCount > 0, "no InvalidInput failures were recorded");
            Assert.True(snapshot.ProcessingErrorCount > 0, "no ProcessingError failures were recorded");
            Assert.True(snapshot.UnknownErrorCount > 0, "no UnknownError failures were recorded");

            // ProcessingError is the largest share by design, not an accident of this seed
            Assert.True(snapshot.ProcessingErrorCount > snapshot.InvalidInputCount);
            Assert.True(snapshot.ProcessingErrorCount > snapshot.UnknownErrorCount);

            double failureRate = (double)snapshot.FailedCount / eventCount;
            Assert.InRange(failureRate, 0.03, 0.07);
        }

        [Fact]
        public async Task NoEventFailsWhenTheFaultPolicyIsDisabled()
        {
            const int eventCount = 500;
            var (pool, queue, repository, metrics) = CreatePipeline(NeverFails());
            var generator = new MockDataGenerator(seed: 5);

            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(eventCount))
            {
                await queue.EnqueueEventAsync(request);
            }

            Assert.True(await WaitForTotalAsync(metrics, eventCount, TimeSpan.FromSeconds(30)));
            await pool.StopProcessingAsync();

            MetricsSnapshot snapshot = metrics.GetSnapshot();

            Assert.Equal(eventCount, snapshot.ProcessedCount);
            Assert.Equal(0, snapshot.FailedCount);
            Assert.Equal(eventCount, repository.CurrentEventCount);
        }

        [Theory]
        [InlineData(FailureReasonType.ProcessingError)]
        [InlineData(FailureReasonType.UnknownError)]
        public async Task EveryEventFailsWhenTheFaultPolicyAlwaysFires(FailureReasonType reason)
        {
            const int eventCount = 200;
            var policy = new FixedFaultPolicy(new ProcessingFault(reason, "injected"));
            var (pool, queue, repository, metrics) = CreatePipeline(policy);
            var generator = new MockDataGenerator(seed: 9);

            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(eventCount))
            {
                await queue.EnqueueEventAsync(request);
            }

            Assert.True(await WaitForTotalAsync(metrics, eventCount, TimeSpan.FromSeconds(30)));
            await pool.StopProcessingAsync();

            MetricsSnapshot snapshot = metrics.GetSnapshot();

            Assert.Equal(0, snapshot.ProcessedCount);
            Assert.Equal(eventCount, snapshot.FailedCount);
            Assert.Equal(0, repository.CurrentEventCount);

            // A failed event must never reach the store
            Assert.All(
                metrics.GetRecentFailures(50),
                failure => Assert.Equal(reason, failure.Reason));
        }

        [Fact]
        public async Task InvalidEventsAreCountedAsInvalidInputAndNotStored()
        {
            var (pool, queue, repository, metrics) = CreatePipeline();

            await pool.StartProcessingAsync(2);

            await queue.EnqueueEventAsync(new EventRequest
            {
                UserId = "",
                Email = "broken",
                IpAddress = "not-an-ip",
                EventType = ""
            });

            Assert.True(await WaitForTotalAsync(metrics, 1, TimeSpan.FromSeconds(10)));
            await pool.StopProcessingAsync();

            Assert.Equal(1, metrics.GetSnapshot().InvalidInputCount);
            Assert.Equal(0, repository.CurrentEventCount);
        }

        [Fact]
        public async Task StopProcessingAsync_LeavesThePoolStoppedAndRestartable()
        {
            var (pool, queue, _, _) = CreatePipeline();
            var generator = new MockDataGenerator(seed: 3);

            await pool.StartProcessingAsync(5);
            Assert.True(pool.IsRunning);

            foreach (EventRequest request in generator.GenerateBulkEvents(500))
            {
                await queue.EnqueueEventAsync(request);
            }

            await pool.StopProcessingAsync();
            Assert.False(pool.IsRunning);

            await pool.StartProcessingAsync(3);
            Assert.True(pool.IsRunning);

            await pool.StopProcessingAsync();
            Assert.False(pool.IsRunning);
        }

        // A dequeued event is no longer in the channel and Channel<T> can't put it back, so a
        // worker that abandoned one mid-processing would lose it - not stored, not counted.
        // Simulated work keeps the pool busy long enough to cancel while that is in progress.
        [Fact]
        public async Task StopProcessingAsync_LosesNoEventThatWasMidFlight()
        {
            const int eventCount = 100;
            var (pool, queue, _, metrics) = CreatePipeline(NeverFails());
            var generator = new MockDataGenerator(seed: 3);

            foreach (EventRequest request in generator.GenerateBulkEvents(eventCount))
            {
                await queue.EnqueueEventAsync(request);
            }

            // Wide enough that stop is near-certain to land while workers are inside the delay
            pool.SimulatedWorkMs = 20;
            await pool.StartProcessingAsync(4);

            await Task.Delay(150);
            await pool.StopProcessingAsync();

            long accounted = metrics.GetSnapshot().TotalCount;
            int stillQueued = queue.EventCount;

            Assert.Equal(eventCount, accounted + stillQueued);

            // Guard the guard: if either of these is zero the assertion above proved nothing
            Assert.True(accounted > 0, "no events were processed, so nothing was ever mid-flight");
            Assert.True(stillQueued > 0, "the queue drained before the cancel, so nothing was interrupted");
        }

        // Stop stays prompt: the loop takes cancellation at the top, so it finishes at most one
        // event per worker rather than draining the queue
        [Fact]
        public async Task StopProcessingAsync_InterruptsWorkersThatAreMidEvent()
        {
            var (pool, queue, _, metrics) = CreatePipeline();
            var generator = new MockDataGenerator(seed: 21);

            pool.SimulatedWorkMs = 50;
            await pool.StartProcessingAsync(5);

            foreach (EventRequest request in generator.GenerateBulkEvents(500))
            {
                await queue.EnqueueEventAsync(request);
            }

            await Task.Delay(200);

            long start = Stopwatch.GetTimestamp();
            await pool.StopProcessingAsync();
            TimeSpan stopDuration = Stopwatch.GetElapsedTime(start);

            Assert.False(pool.IsRunning);
            Assert.True(
                stopDuration < TimeSpan.FromSeconds(2),
                $"Stop took {stopDuration.TotalMilliseconds:F0} ms, so cancellation was not observed promptly.");

            // Stopped early rather than draining the queue
            Assert.True(metrics.GetSnapshot().TotalCount < 500);
        }

        [Fact]
        public async Task StopProcessingAsync_IsSafeWhenNeverStarted()
        {
            var (pool, _, _, _) = CreatePipeline();

            await pool.StopProcessingAsync();

            Assert.False(pool.IsRunning);
        }

        [Fact]
        public async Task StartProcessingAsync_IgnoresASecondStart()
        {
            var (pool, _, _, _) = CreatePipeline();

            await pool.StartProcessingAsync(5);
            await pool.StartProcessingAsync(5);

            Assert.True(pool.IsRunning);
            await pool.StopProcessingAsync();
        }

        [Fact]
        public async Task StartProcessingAsync_RejectsAWorkerCountBelowOne()
        {
            var (pool, _, _, _) = CreatePipeline();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pool.StartProcessingAsync(0));
        }

        [Fact]
        public async Task NothingIsStoredInItsOriginalForm()
        {
            var (pool, queue, repository, metrics) = CreatePipeline();

            await pool.StartProcessingAsync(1);

            for (int i = 0; i < 200; i++)
            {
                await queue.EnqueueEventAsync(new EventRequest
                {
                    UserId = "U12345",
                    Email = "john@example.com",
                    IpAddress = "192.168.1.10",
                    EventType = "Login"
                });
            }

            Assert.True(await WaitForTotalAsync(metrics, 200, TimeSpan.FromSeconds(20)));
            await pool.StopProcessingAsync();

            IReadOnlyList<ProcessedEvent> stored200 = repository.GetSnapshot();
            Assert.NotEmpty(stored200);

            foreach (ProcessedEvent stored in stored200)
            {
                Assert.DoesNotContain("U12345", stored.HashedUserId);
                Assert.DoesNotContain("john", stored.MaskedEmail);
                Assert.Equal("192.168.1.xxx", stored.MaskedIpAddress);
            }
        }
    }
}
