using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrivacyEventProcessing.Domain.Interfaces;
using PrivacyEventProcessing.Domain.Models;
using PrivacyEventProcessing.MockData;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace PrivacyEventProcessing.MAUI.ViewModels
{
    // A "load run" is one press of Simulate Load: generate a batch, queue it, and wait for
    // the workers to account for every event. Two phases - generating, then draining.
    public partial class MainDashboardViewModel : ObservableObject, IAsyncDisposable
    {
        private const int LoadRunEventCount = 10000;

        // Enough per-event work that a 10k run lasts long enough to watch and to cancel
        private const int SlowModeWorkMs = 5;

        private const int MaxDisplayedFailures = 50;

        // The bound list has to be capped. Binding costs UI thread time per row, so a ~10,000
        // row rebind froze the window; 200 rows fits inside the 500ms tick. Reading the store
        // was never the problem - GetSnapshot is a ~10us array copy.
        private const int DisplayWindow = 200;
        private const int RefreshIntervalMs = 500;

        private readonly IEventQueue eventQueue;
        private readonly IEventRepository eventRepository;
        private readonly IEventProcessor eventProcessor;
        private readonly IProcessingMetrics processingMetrics;
        private readonly MockDataGenerator mockDataGenerator;
        private readonly IDispatcherTimer refreshTimer;

        private CancellationTokenSource? generatorCts;
        private Task? generatorTask;

        private long lastProcessedCount;
        private long lastThroughputTimestamp;
        private long lastRenderedFailureCount = -1;
        private long lastRenderedProcessedCount = -1;

        // How many rows are bound. Grows a page at a time as the user scrolls back.
        private int displayCount = DisplayWindow;

        // One page per refresh tick. See LoadMoreEvents.
        private bool canPageThisTick = true;

        // Baseline taken when the run starts, so events submitted from the entry form
        // beforehand don't count towards this run's progress
        private long loadRunBaselineTotal;
        private int loadRunQueuedCount;
        private int loadRunTargetCount;

        // A load run may only start against an empty queue, so the simulate buttons follow
        // this. See CanStartLoadRun.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SimulateLoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(SimulateSlowLoadCommand))]
        [NotifyPropertyChangedFor(nameof(HasQueueBacklog))]
        public partial int QueueLength { get; set; }

        // Drives the hint that explains why the simulate buttons are disabled
        public bool HasQueueBacklog => QueueLength > 0 && !IsLoadRunActive;
        [ObservableProperty] public partial long ProcessedCount { get; set; }
        [ObservableProperty] public partial long FailedCount { get; set; }
        [ObservableProperty] public partial long InvalidInputCount { get; set; }
        [ObservableProperty] public partial long ProcessingErrorCount { get; set; }
        [ObservableProperty] public partial long UnknownErrorCount { get; set; }
        [ObservableProperty] public partial double AverageProcessingTimeMs { get; set; }
        [ObservableProperty] public partial double EventsPerSecond { get; set; }
        [ObservableProperty] public partial int CachedEventCount { get; set; }

        [ObservableProperty] public partial string StatusMessage { get; set; }
        [ObservableProperty] public partial int WorkerCount { get; set; }
        [ObservableProperty] public partial ProcessedEvent? SelectedEvent { get; set; }

        // The popup binds to this, not to SelectedEvent. Replacing the snapshot clears the
        // CollectionView's SelectedItem, which would close the popup on every refresh.
        [ObservableProperty] public partial ProcessedEvent? DetailEvent { get; set; }

        partial void OnSelectedEventChanged(ProcessedEvent? value)
        {
            if (value is not null)
            {
                DetailEvent = value;
            }
        }
        [ObservableProperty] public partial string ProcessorStateText { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartProcessingCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopProcessingCommand))]
        public partial bool IsProcessorRunning { get; set; }

        partial void OnIsProcessorRunningChanged(bool value)
        {
            ProcessorStateText = value ? "Running" : "Stopped";
        }

        // Stays visible after a run so the final figure is readable; only Clear hides it.
        [ObservableProperty] public partial bool ShowLoadProgress { get; set; }
        [ObservableProperty] public partial double LoadProgress { get; set; }
        [ObservableProperty] public partial string LoadProgressText { get; set; }

        // Phase one only. The channel is bounded at the batch size, so this goes false
        // almost immediately and says nothing about whether the workers are done.
        [ObservableProperty] public partial bool IsGeneratingEvents { get; set; }

        // Both phases - what the buttons key off.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SimulateLoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(SimulateSlowLoadCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelLoadRunCommand))]
        [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
        [NotifyPropertyChangedFor(nameof(HasQueueBacklog))]
        public partial bool IsLoadRunActive { get; set; }

        // Both lists fit side by side on a wide window. On a narrow one the user picks which
        // is on screen, so only one CollectionView is measured and scrolled at a time.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEventListVisible))]
        [NotifyPropertyChangedFor(nameof(IsFailureListVisible))]
        public partial bool IsNarrowLayout { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEventListVisible))]
        [NotifyPropertyChangedFor(nameof(IsFailureListVisible))]
        public partial bool ShowFailureList { get; set; }

        public bool IsEventListVisible => !IsNarrowLayout || !ShowFailureList;

        public bool IsFailureListVisible => !IsNarrowLayout || ShowFailureList;

        // The stepper binds to these rather than hardcoding the numbers in XAML
        public int WorkerCountMinimum => WorkerLimits.Minimum;

        public int WorkerCountMaximum => eventProcessor.MaxWorkerCount;

        public ObservableCollection<FailureRecord> RecentFailures { get; } = [];

        // Newest DisplayWindow events. A snapshot rather than an ObservableCollection
        // mirroring the store - that raises one notification per mutation, so thousands of
        // inserts a second marshalled onto the UI thread. This is one array copy per refresh.
        [ObservableProperty] public partial IReadOnlyList<ProcessedEvent> ProcessedEvents { get; set; }

        [ObservableProperty] public partial string ProcessedEventsCaption { get; set; }

        // False once the user has paged back. The tail stops replacing the list while they
        // are reading history, both so the rows don't shift under them and so the refresh
        // doesn't grow into the cost the window exists to avoid.
        [ObservableProperty] public partial bool IsShowingLatest { get; set; }

        [RelayCommand]
        private void ShowEvents()
        {
            ShowFailureList = false;
        }

        [RelayCommand]
        private void ShowFailures()
        {
            ShowFailureList = true;
        }

        // Raised when the user scrolls within RemainingItemsThreshold of the end.
        //
        // Rate limited to one page per tick because the trigger also fires on binding, not
        // just on a real scroll: without the guard, extending the window re-raises it
        // immediately and the list walks 200 -> 400 -> 600 through the whole cache in one
        // layout pass. That was the Simulate Load freeze.
        [RelayCommand]
        private void LoadMoreEvents()
        {
            if (!canPageThisTick) return;

            IReadOnlyList<ProcessedEvent> snapshot = eventRepository.GetSnapshot();

            if (displayCount >= snapshot.Count) return;

            canPageThisTick = false;
            displayCount += DisplayWindow;
            IsShowingLatest = false;
            BindEventWindow(snapshot);
        }

        // Back to the tail. Also re-arms the live refresh.
        [RelayCommand]
        private void ShowLatestEvents()
        {
            displayCount = DisplayWindow;
            IsShowingLatest = true;
            canPageThisTick = false;
            SelectedEvent = null;
            BindEventWindow(eventRepository.GetSnapshot());
        }

        public MainDashboardViewModel(
            IEventQueue eventQueue,
            IEventRepository eventRepository,
            IEventProcessor eventProcessor,
            IProcessingMetrics processingMetrics,
            MockDataGenerator mockDataGenerator,
            IDispatcher dispatcher)
        {
            this.eventQueue = eventQueue;
            this.eventRepository = eventRepository;
            this.eventProcessor = eventProcessor;
            this.processingMetrics = processingMetrics;
            this.mockDataGenerator = mockDataGenerator;

            StatusMessage = "Idle";
            WorkerCount = WorkerLimits.Default;
            LoadProgressText = string.Empty;
            ProcessorStateText = "Stopped";
            ProcessedEvents = [];
            IsShowingLatest = true;
            ProcessedEventsCaption = "Processed events";

            refreshTimer = dispatcher.CreateTimer();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs);
            refreshTimer.IsRepeating = true;
            refreshTimer.Tick += OnRefreshTick;
        }

        public void Activate()
        {
            lastProcessedCount = processingMetrics.GetSnapshot().ProcessedCount;
            lastThroughputTimestamp = Stopwatch.GetTimestamp();
            IsProcessorRunning = eventProcessor.IsRunning;
            Refresh();
            refreshTimer.Start();
        }

        public void Deactivate()
        {
            refreshTimer.Stop();
        }

        [RelayCommand]
        private void CloseEventDetail()
        {
            DetailEvent = null;

            // Deselect too, otherwise tapping the same row again raises no selection change
            SelectedEvent = null;
        }

        // Times the refresh. It runs on the UI thread, so however long it takes is time the
        // window isn't responding to input - which is what makes it worth showing.
        private void OnRefreshTick(object? sender, EventArgs e)
        {
            // Re-arm paging. Bounds a runaway trigger to one extra page per tick while
            // leaving a genuine scroll-to-load responsive.
            canPageThisTick = true;

            Refresh();
        }

        // Dispatcher timer, so this is already on the UI thread and nothing needs marshalling.
        // One snapshot read per tick regardless of event volume.
        private void Refresh()
        {
            MetricsSnapshot snapshot = processingMetrics.GetSnapshot();

            QueueLength = eventQueue.EventCount;
            ProcessedCount = snapshot.ProcessedCount;
            FailedCount = snapshot.FailedCount;
            InvalidInputCount = snapshot.InvalidInputCount;
            ProcessingErrorCount = snapshot.ProcessingErrorCount;
            UnknownErrorCount = snapshot.UnknownErrorCount;
            AverageProcessingTimeMs = snapshot.AverageProcessingTimeMs;
            CachedEventCount = eventRepository.CurrentEventCount;

            UpdateThroughput(snapshot.ProcessedCount);
            UpdateLoadProgress(snapshot.TotalCount);
            RefreshProcessedEvents(snapshot.ProcessedCount);
            RefreshFailures();
        }

        // Replacing the bound list resets scroll position, so only do it when something was
        // stored since the last tick. Keyed off the all-time processed count rather than the
        // cache size, which stops changing once the cache is at its cap.
        private void RefreshProcessedEvents(long processedNow)
        {
            // Paused while the user is reading history
            if (!IsShowingLatest) return;

            if (processedNow == lastRenderedProcessedCount) return;

            lastRenderedProcessedCount = processedNow;

            BindEventWindow(eventRepository.GetSnapshot());
        }

        private void BindEventWindow(IReadOnlyList<ProcessedEvent> snapshot)
        {
            int take = Math.Min(displayCount, snapshot.Count);

            ProcessedEvents = take == snapshot.Count
                ? snapshot
                : snapshot.Take(take).ToArray();

            // Shown vs held are different numbers, so say both rather than hide the gap
            ProcessedEventsCaption = take == snapshot.Count
                ? $"Processed events — {snapshot.Count:N0} in memory"
                : $"Processed events — newest {take:N0} of {snapshot.Count:N0} in memory";
        }

        // Only rebuilt when something failed since the last tick
        private void RefreshFailures()
        {
            if (FailedCount == lastRenderedFailureCount) return;

            lastRenderedFailureCount = FailedCount;
            RecentFailures.Clear();

            foreach (FailureRecord failure in processingMetrics.GetRecentFailures(MaxDisplayedFailures))
            {
                RecentFailures.Add(failure);
            }
        }

        // Counts successes and failures both - a failed event is still done with. Otherwise
        // the bar would stall at 95% on a 5% failure rate.
        private void UpdateLoadProgress(long totalNow)
        {
            if (!ShowLoadProgress) return;

            int target = loadRunTargetCount;
            if (target <= 0)
            {
                LoadProgress = 0;
                LoadProgressText = string.Empty;
                return;
            }

            long done = Math.Clamp(totalNow - loadRunBaselineTotal, 0, target);

            LoadProgress = (double)done / target;
            LoadProgressText = $"{done:N0} / {target:N0}";

            // Done once the generator has stopped and the workers have caught up. The status
            // line still reads "Queuing..." at this point - the generator set that, and only
            // this knows when the draining half is over - so it is retired here.
            if (!IsGeneratingEvents && done >= target && IsLoadRunActive)
            {
                IsLoadRunActive = false;
                StatusMessage = $"Run finished. {done:N0} events processed.";
            }
        }

        // Delta over the last tick, not an all-time average
        private void UpdateThroughput(long processedNow)
        {
            long now = Stopwatch.GetTimestamp();
            double seconds = Stopwatch.GetElapsedTime(lastThroughputTimestamp, now).TotalSeconds;

            if (seconds > 0)
            {
                EventsPerSecond = (processedNow - lastProcessedCount) / seconds;
            }

            lastProcessedCount = processedNow;
            lastThroughputTimestamp = now;
        }

        // Called from the workers switch. The guard stops us acting on the Toggled event
        // that fires when the view model updates the switch rather than the user.
        public async Task SetProcessingAsync(bool shouldRun)
        {
            if (shouldRun == eventProcessor.IsRunning) return;

            if (shouldRun)
            {
                await StartProcessingAsync();
            }
            else
            {
                await StopProcessingAsync();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartProcessing))]
        private async Task StartProcessingAsync()
        {
            await eventProcessor.StartProcessingAsync(WorkerCount);
            IsProcessorRunning = eventProcessor.IsRunning;
            StatusMessage = $"Processing with {WorkerCount} worker(s).";
        }

        private bool CanStartProcessing() => !IsProcessorRunning;

        [RelayCommand(CanExecute = nameof(CanStopProcessing))]
        private async Task StopProcessingAsync()
        {
            StatusMessage = "Stopping workers...";

            // Producer first, or the generator keeps filling a queue nobody is draining
            await StopGeneratingAsync();
            await eventProcessor.StopProcessingAsync();

            IsProcessorRunning = eventProcessor.IsRunning;
            Refresh();
            StatusMessage = "Workers stopped. Queued events are kept.";
        }

        private bool CanStopProcessing() => IsProcessorRunning;

        [RelayCommand(CanExecute = nameof(CanStartLoadRun))]
        private Task SimulateLoadAsync()
        {
            return RunLoadAsync(0);
        }

        // Same run with simulated work per event. Without it the pipeline is pure CPU and
        // clears 10,000 events faster than anyone can react.
        [RelayCommand(CanExecute = nameof(CanStartLoadRun))]
        private Task SimulateSlowLoadAsync()
        {
            return RunLoadAsync(SlowModeWorkMs);
        }

        // Only against an empty queue. A cancelled run leaves its events behind, and a new run
        // on top of them would count that backlog towards its own target - the bar would
        // finish while the leftovers were still draining. The user resolves it instead: drain
        // the backlog with the workers, or discard it with Clear.
        private bool CanStartLoadRun() => !IsLoadRunActive && eventQueue.EventCount == 0;

        private async Task RunLoadAsync(int simulatedWorkMs)
        {
            // Before the workers start, so the first event already sees it
            eventProcessor.SimulatedWorkMs = simulatedWorkMs;

            if (!IsProcessorRunning)
            {
                await StartProcessingAsync();
            }

            loadRunBaselineTotal = processingMetrics.GetSnapshot().TotalCount;
            Volatile.Write(ref loadRunQueuedCount, 0);
            loadRunTargetCount = LoadRunEventCount;

            LoadProgress = 0;
            LoadProgressText = $"0 / {LoadRunEventCount:N0}";
            ShowLoadProgress = true;
            IsLoadRunActive = true;

            generatorCts = new CancellationTokenSource();
            IsGeneratingEvents = true;
            StatusMessage = simulatedWorkMs > 0
                ? $"Queuing {LoadRunEventCount:N0} events with {simulatedWorkMs} ms simulated work each..."
                : $"Queuing {LoadRunEventCount:N0} events...";

            CancellationToken token = generatorCts.Token;
            generatorTask = Task.Run(() => GenerateEventsAsync(LoadRunEventCount, token), CancellationToken.None);

            try
            {
                await generatorTask;

                // Queuing is only half the run. UpdateLoadProgress replaces this once the
                // workers have drained what was queued.
                StatusMessage = $"Queued {LoadRunEventCount:N0} events. Processing...";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"Stopped generating after {Volatile.Read(ref loadRunQueuedCount):N0} events.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Event generation failed: {ex.Message}";
            }
            finally
            {
                // A cancelled run queued fewer than it meant to, so retarget the bar against
                // what was actually produced or it never reaches the end
                loadRunTargetCount = Volatile.Read(ref loadRunQueuedCount);

                IsGeneratingEvents = false;
                generatorCts?.Dispose();
                generatorCts = null;
                generatorTask = null;
            }
        }

        // Background thread. The queue is bounded, so this awaits when full and the generator
        // gets throttled by the workers instead of running away with memory.
        private async Task GenerateEventsAsync(int count, CancellationToken cancellationToken)
        {
            foreach (EventRequest request in mockDataGenerator.GenerateBulkEvents(
                count, MockDataGenerator.DefaultMalformedRatio))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await eventQueue.EnqueueEventAsync(request, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref loadRunQueuedCount);
            }
        }

        // Cancels the whole run, not just the generator. The generator has usually finished
        // by the time the user hits this, so cancelling it alone would look like a no-op.
        [RelayCommand(CanExecute = nameof(CanCancelLoadRun))]
        private async Task CancelLoadRunAsync()
        {
            StatusMessage = "Cancelling...";

            await StopGeneratingAsync();
            await eventProcessor.StopProcessingAsync();

            IsProcessorRunning = eventProcessor.IsRunning;
            IsLoadRunActive = false;
            Refresh();

            StatusMessage = QueueLength > 0
                ? $"Cancelled. {QueueLength:N0} events still queued — switch the workers back "
                    + "on to drain them, or press Clear to discard them."
                : "Cancelled.";
        }

        private bool CanCancelLoadRun() => IsLoadRunActive;

        // Separate from the command because Stop workers calls it too
        private async Task StopGeneratingAsync()
        {
            CancellationTokenSource? source = generatorCts;
            if (source is null) return;

            try
            {
                await source.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // run finished and disposed it between the null check and here
                return;
            }

            Task? running = generatorTask;
            if (running is null) return;

            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Disabled mid-run: resetting the counters would leave the progress bar measuring
        // against a baseline that no longer exists
        [RelayCommand(CanExecute = nameof(CanClear))]
        private async Task ClearAsync()
        {
            // Discards the backlog a cancelled run left behind. Without this the queue keeps
            // events the reset counters no longer account for, and the simulate buttons stay
            // disabled with nothing on screen explaining why.
            int discarded = eventQueue.DrainAll();

            await eventRepository.ClearEventsAsync();
            processingMetrics.Reset();

            ProcessedEvents = [];
            ProcessedEventsCaption = "Processed events";
            RecentFailures.Clear();
            SelectedEvent = null;
            DetailEvent = null;

            displayCount = DisplayWindow;
            IsShowingLatest = true;

            lastRenderedFailureCount = -1;
            lastRenderedProcessedCount = -1;
            lastProcessedCount = 0;
            lastThroughputTimestamp = Stopwatch.GetTimestamp();
            EventsPerSecond = 0;

            ShowLoadProgress = false;
            IsLoadRunActive = false;
            LoadProgress = 0;
            LoadProgressText = string.Empty;

            Refresh();
            StatusMessage = discarded > 0
                ? $"Cleared. {discarded:N0} queued events discarded."
                : "Metrics and cache cleared.";
        }

        private bool CanClear() => !IsLoadRunActive;

        public async ValueTask DisposeAsync()
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= OnRefreshTick;

            await StopGeneratingAsync();
            await eventProcessor.StopProcessingAsync();
        }
    }
}
