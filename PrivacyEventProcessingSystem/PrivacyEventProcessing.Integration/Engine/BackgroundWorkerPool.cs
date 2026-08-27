using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using System.Diagnostics;

namespace PrivacyEventProcessing.Integration.Engine
{
    public class BackgroundWorkerPool:IEventProcessor, IAsyncDisposable
    {
        private readonly IEventQueue queue;
        private readonly IEventRepository repository;
        private readonly IPrivacyService privacyService;
        private readonly IProcessingMetrics processingMetrics;
        private readonly IFaultPolicy faultPolicy;

        public const int DefaultWorkerCount = WorkerLimits.Default;

        private readonly SemaphoreSlim stateLock = new(1, 1);
        private Task[]? workers;
        private CancellationTokenSource? cts;
        private int simulatedWorkMs;

        public bool IsRunning => Volatile.Read(ref workers) is not null;

        public int MaxWorkerCount => WorkerLimits.Maximum;

        // Read fresh per event so it can be changed while the workers are running.
        public int SimulatedWorkMs
        {
            get => Volatile.Read(ref simulatedWorkMs);
            set => Volatile.Write(ref simulatedWorkMs, value);
        }

        public BackgroundWorkerPool(IEventQueue queue, IEventRepository repository, IPrivacyService privacyService, IProcessingMetrics processingMetrics, IFaultPolicy faultPolicy)
        {
            this.queue = queue;
            this.repository = repository;
            this.privacyService = privacyService;
            this.processingMetrics = processingMetrics;
            this.faultPolicy = faultPolicy;
        }

        public async Task StartProcessingAsync(int workerCount = DefaultWorkerCount, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, WorkerLimits.Minimum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(workerCount, WorkerLimits.Maximum);

            await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (workers is not null)
                    return;

                cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;

                Task[] started = new Task[workerCount];
                for (int i = 0; i < workerCount; i++)
                {
                    started[i] = Task.Run(() => WorkerLoopAsync(token), CancellationToken.None);
                }
                Volatile.Write(ref workers, started);
            }
            finally
            {
                stateLock.Release();
            }
        }

        private async Task WorkerLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EventRequest request;

                try
                {
                    request = await queue.DequeueEventAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // The event is off the channel now and Channel<T> has no peek/acknowledge, so
                // there is nowhere to hand it back. Cancellation is therefore taken at the top
                // of the loop, never inside this block: every event is either still queued or
                // accounted for, never neither. Costs one event per worker on shutdown.
                long startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    if (!privacyService.ValidateInput(request, out string error))
                    {
                        processingMetrics.RecordFailure(FailureReasonType.InvalidInput, error);
                        continue;
                    }

                    // CancellationToken.None on purpose, not an oversight: passing the worker
                    // token here is what used to drop the in-flight event on cancel.
                    int workMs = SimulatedWorkMs;
                    if (workMs > 0)
                    {
                        await Task.Delay(workMs, CancellationToken.None).ConfigureAwait(false);
                    }

                    // UnknownError is thrown, not recorded, so it goes through the catch-all below
                    ProcessingFault? fault = faultPolicy.NextFault();
                    if (fault is not null)
                    {
                        if (fault.Reason == FailureReasonType.UnknownError)
                            throw new InvalidOperationException(fault.Message);

                        processingMetrics.RecordFailure(fault.Reason, fault.Message);
                        continue;
                    }

                    var processed = new ProcessedEvent(
                        privacyService.HashUserId(request.UserId),
                        privacyService.MaskEmail(request.Email),
                        privacyService.MaskIpAddress(request.IpAddress),
                        request.EventType,
                        request.CreatedAt,
                        DateTime.UtcNow);

                    await repository.AddEventAsync(processed).ConfigureAwait(false);

                    processingMetrics.RecordSuccess(Stopwatch.GetElapsedTime(startTimestamp).Ticks);
                }
                // No OperationCanceledException case on purpose: catching it to break would
                // silently drop the event. Recording it keeps the accounting invariant.
                catch (Exception ex)
                {
                    processingMetrics.RecordFailure(FailureReasonType.UnknownError, ex.Message);
                }
            }
        }

        public async Task StopProcessingAsync(CancellationToken cancellationToken = default)
        {
            Task[]? running;
            CancellationTokenSource? source;

            await stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (workers is null)
                    return;

                running = workers;
                source = cts;

                Volatile.Write(ref workers, null);
                cts = null;
            }
            finally
            {
                stateLock.Release();
            }

            if (source is not null)
                await source.CancelAsync().ConfigureAwait(false);

            // Not bounded by cancellationToken - callers need the workers to have actually
            // finished when this returns, and bailing early would dispose the CTS while
            // workers are still observing it.
            try
            {
                await Task.WhenAll(running).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            finally
            {
                source?.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopProcessingAsync().ConfigureAwait(false);
            stateLock.Dispose();
        }
    }
}
