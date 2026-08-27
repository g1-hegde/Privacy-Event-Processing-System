using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.Domain.Validation;

namespace PrivacyEventProcessing.MAUI.ViewModels
{
    public partial class EventEntryViewModel : ObservableObject
    {
        private static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(2);
        private const int StatusPollIntervalMs = 250;

        private readonly IEventQueue eventQueue;
        private readonly IEventProcessor eventProcessor;
        private readonly IProcessingMetrics processingMetrics;
        private readonly IDispatcherTimer statusTimer;

        // Total the pipeline has to reach before the queued event counts as done
        private long pendingTargetTotal;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
        [NotifyPropertyChangedFor(nameof(IsFormComplete))]
        public partial string UserId { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
        [NotifyPropertyChangedFor(nameof(IsFormComplete))]
        public partial string Email { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
        [NotifyPropertyChangedFor(nameof(IsFormComplete))]
        public partial string IpAddress { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
        [NotifyPropertyChangedFor(nameof(IsFormComplete))]
        public partial string EventType { get; set; }

        [ObservableProperty] public partial string StatusMessage { get; set; }
        [ObservableProperty] public partial bool IsProcessorRunning { get; set; }

        public EventEntryViewModel(
            IEventQueue eventQueue,
            IEventProcessor eventProcessor,
            IProcessingMetrics processingMetrics,
            IDispatcher dispatcher)
        {
            this.eventQueue = eventQueue;
            this.eventProcessor = eventProcessor;
            this.processingMetrics = processingMetrics;

            statusTimer = dispatcher.CreateTimer();
            statusTimer.Interval = TimeSpan.FromMilliseconds(StatusPollIntervalMs);
            statusTimer.IsRepeating = true;
            statusTimer.Tick += OnStatusTick;

            // Partial properties can't have field initialisers, so default here to keep the
            // bindings and the Trim() calls off null
            UserId = string.Empty;
            Email = string.Empty;
            IpAddress = string.Empty;
            EventType = string.Empty;
            StatusMessage = string.Empty;
        }

        public void Activate()
        {
            IsProcessorRunning = eventProcessor.IsRunning;
        }

        public void Deactivate()
        {
            statusTimer.Stop();
        }

        // Clears the confirmation once the pipeline has accounted for the event, rather than
        // leaving a stale message on screen
        private void OnStatusTick(object? sender, EventArgs e)
        {
            IsProcessorRunning = eventProcessor.IsRunning;

            if (processingMetrics.GetSnapshot().TotalCount < pendingTargetTotal)
            {
                return;
            }

            StatusMessage = string.Empty;
            statusTimer.Stop();
        }

        [RelayCommand(CanExecute = nameof(CanSubmit))]      
        private async Task SubmitAsync()
        {
            var request = new EventRequest
            {
                UserId = UserId.Trim(),
                Email = Email.Trim(),
                IpAddress = IpAddress.Trim(),
                EventType = EventType.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            // The queue is bounded - without a timeout a full queue blocks the UI thread
            // until a worker drains something
            using var cts = new CancellationTokenSource(EnqueueTimeout);

            try
            {
                // Before the enqueue, otherwise a worker could finish the event between the
                // snapshot and the target being set
                long totalBefore = processingMetrics.GetSnapshot().TotalCount;

                await eventQueue.EnqueueEventAsync(request, cts.Token);

                pendingTargetTotal = totalBefore + 1;
                statusTimer.Start();

                StatusMessage = eventProcessor.IsRunning
                    ? "Event queued for processing."
                    : "Event queued. Start the workers on the dashboard to process it.";

                UserId = string.Empty;
                Email = string.Empty;
                IpAddress = string.Empty;
                EventType = string.Empty;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Queue is full. Wait for the workers to catch up and try again.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not queue the event: {ex.Message}";
            }

            IsProcessorRunning = eventProcessor.IsRunning;
        }

        // Drives the hint under the button, so a disabled button says why
        public bool IsFormComplete => CanSubmit();

        // Same rules the behaviours and the pipeline use
        private bool CanSubmit()
        {
            return EventValidationRules.IsValidUserId(UserId, out _)
                && EventValidationRules.IsValidEmail(Email, out _)
                && EventValidationRules.IsValidIpAddress(IpAddress, out _)
                && EventValidationRules.IsValidEventType(EventType, out _);
        }
    }
}
