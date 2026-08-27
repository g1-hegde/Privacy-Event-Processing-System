# Privacy Event Processing System

A .NET MAUI application simulating a privacy-aware event processing system. Events arrive from
an entry form or a bulk generator, pass through a queue, are processed by a pool of background
workers, and are cached in memory. A dashboard shows live metrics while processing runs.

Built on .NET 10. Developed and tested on Windows; the project also targets Android, iOS and
Mac Catalyst.

## Architecture

Five projects, dependencies pointing inwards:

| Project | Contains |
|---|---|
| `Domain` | Models, interfaces, validation rules. No dependencies on anything else. |
| `Integration` | Queue, worker pool, privacy service, in-memory store, metrics. |
| `MockData` | Sample event generation. |
| `MAUI` | Views, view models, validation behaviours, DI registration in `MauiProgram.cs`. |
| `Test` | xUnit tests. |

The pipeline follows the suggested producer-consumer flow:

```
UI / generator → ChannelEventQueue → worker pool → validate → protect → in-memory cache → dashboard
```

Each stage sits behind an interface in `Domain` — `IEventQueue`, `IEventProcessor`,
`IPrivacyService`, `IEventRepository`, `IProcessingMetrics`, `IFaultPolicy` — registered as
singletons in `MauiProgram`. The view models depend on those interfaces rather than the
implementations. `MockDataGenerator` is the one exception, injected as a concrete type since
nothing needs to substitute it.

## Event entry

`EventEntryPage` submits **User ID**, **Email Address**, **IP Address** and **Event Type**.

Each field carries a validation behaviour that shows its own error message once the field has
been left. Submit stays disabled until all four are valid, with a note under the button
explaining why. A banner warns when the workers are stopped, since a submitted event will then
sit in the queue rather than being processed.

## Event processing pipeline

`ChannelEventQueue` wraps `Channel<EventRequest>`, bounded at 10,000 with `FullMode.Wait`:

- A worker awaiting the channel parks without holding a thread.
- A producer that outruns the workers gets back pressure rather than an unbounded queue.

`BackgroundWorkerPool` runs N async loops. Each dequeues one event, validates it, protects it,
stores it and records metrics, then loops.

## Privacy

Sensitive fields are protected before anything is stored or displayed:

| Field | Treatment | Example output |
|---|---|---|
| User ID | HMAC-SHA256, base64 | `k7Rr9...=` |
| Email Address | Masked, domain kept | `j***n@example.com` |
| IP Address | Host part dropped | `192.168.1.xxx`, `2001:0db8:xxxx:...` |

**Why hash the User ID rather than mask it.** The dashboard has to distinguish users without
identifying them, which masking cannot do.

**IP masking** works off the parsed address bytes rather than string splitting, so shortened
IPv6 forms such as `::1` are handled correctly.

**Enforcement.** `ProcessedEvent` has no field capable of holding an original value, so an
unprotected value cannot reach the cache or the UI by accident. Tapping a row on the dashboard
opens a detail popup listing every stored field — the quickest way to confirm this by eye.

## Local storage

In memory only — no SQLite, SQL Server, local files or cloud storage.

`InMemoryEventRepository` holds a single `ConcurrentQueue<ProcessedEvent>` capped at 12,000,
dropping oldest at the cap.

The read and write paths are deliberately asymmetric:

- **Writes are hot** — one per event, from every worker, thousands a second. `Enqueue` is
  lock-free, so workers on different cores don't serialise behind each other.
- **Reads are cold** — the dashboard, twice a second. `GetSnapshot` calls `ToArray`, which is
  O(n) and allocates, but takes a point-in-time copy without blocking writers.

Concurrency is covered by tests: 5,000 writes across 16 threads, and a test that hammers
`GetSnapshot` while a writer runs and asserts the snapshot is never torn.

## Background processing

Workers run on the thread pool via `Task.Run` and never touch the UI — they only increment
`Interlocked` counters in `ProcessingMetrics`. The dashboard pulls one metrics snapshot per
dispatcher-timer tick (500 ms), so UI cost is the same at 10 events/sec as at 10,000.

Four things keep the UI responsive under load:

1. Workers never marshal to the UI thread; the dashboard pulls, it is not pushed to.
2. Refresh is a fixed 500 ms tick, independent of event rate.
3. The processed-event list is capped at 200 rows.
4. Bulk generation runs on a background task, throttled by the bounded channel.

**Worker count** is configurable from the dashboard stepper: default 5, range 1–99.
`WorkerLimits` in `Domain` is the single source for those bounds — the pool validates against
it and the stepper binds to it, so the UI cannot offer a value the pool would reject. The count
is applied when the pool starts, which is why the stepper is disabled while the workers are
running rather than appearing to take effect.

## Bulk event generation

**Simulate Load** generates 10,000 events on a background task, queues them, starts the workers
if they aren't already running, and processes them while the dashboard keeps updating.

**Simulate Slow Load** does the same with 5 ms of simulated work per event. Without it the
pipeline is pure CPU and drains 10,000 events in about 50 ms — too fast to watch or to cancel.

Both buttons require an empty queue. A cancelled run leaves its events behind, and starting a
fresh run on top of them would count that backlog towards the new run's target, so the progress
bar would reach 100% while the leftovers were still draining. Rather than have the progress
maths guess which events belong to which run, the user resolves it: switch the workers back on
to drain the backlog, or press Clear to discard it. A label under the buttons says so.

## Dashboard

The five required metrics — **queue length**, **processed count**, **failed count**, **average
processing time**, **events per second** — plus a tile for how many events are held in the
cache. All update while processing runs. The failure breakdown by reason and the recent failure
messages are shown alongside.

- **Events/sec** is a delta between ticks, not an all-time average, so it reflects current
  throughput.
- **Average processing time** is per-event wall-clock latency, so it includes scheduling delay.
  More workers means higher latency and higher throughput.

## Failure handling

Roughly 5% of events fail. Both the failed count and the failure reason are displayed, and the
budget is spread across three reason types rather than landing in one bucket:

| Reason | Share | Source | Retryable |
|---|---|---|---|
| `InvalidInput` | ~1.0% | Malformed events from the generator, rejected by real validation | No — never becomes valid |
| `ProcessingError` | ~3.5% | Injected by `SimulatedFaultPolicy` | Yes, in principle |
| `UnknownError` | ~0.5% | Injected as a thrown exception, caught by the worker's catch-all | Unclear by definition |

Total: `0.01 + 0.99 × 0.04` = 4.96%.

### Strategy

**Isolate and count; never crash.** Each event is processed inside its own try/catch, so one
bad event cannot kill a worker — a lost worker would silently reduce throughput with no error
surfacing anywhere. Every event that reaches the end of processing is counted exactly once, as
either a success or a failure. There is a test asserting that over 2,000 events.

**Two mechanisms, deliberately separate.**

- `InvalidInput` is *not* injected. `MockDataGenerator` emits a 1% share of genuinely malformed
  events — one broken field each, cycling through all four rules — so they travel the real
  validation path. Faking that counter would mean recording a validation failure for an event
  that actually passed validation.
- `ProcessingError` and `UnknownError` come from `IFaultPolicy`, injected into the worker, with
  rates in `FaultInjectionOptions` rather than a constant buried in the loop. `UnknownError` is
  *thrown* rather than recorded, so it exercises the genuine catch-all.

Injecting the policy is also what makes the pipeline testable: a test can supply a policy that
never fires and assert exactly zero failures, or one that always fires and assert exactly zero
successes, instead of asserting a statistical range.

**No retry.** The faults are random, so a retry would eventually succeed and make the 5%
meaningless, and there is no real downstream dependency to recover from. In production the
retryable category would go to a dead-letter queue with backoff — the enum already separates
them, which is the point of splitting the budget three ways.

The last 100 failure messages are kept in a bounded queue and the most recent 50 are shown.
Messages vary within a reason (five downstream, two unknown), because a hundred identical rows
would tell an operator nothing.

## Cancellation

Cancel stops the generator and the workers. The status line reports how many events are still
queued and what to do about them, the progress bar holds where it reached, and the worker switch
flips to Stopped.

**It has to stop both.** The channel is bounded at the batch size, so the generator finishes
almost immediately and the rest of the run is draining. Cancelling only the generator would look
like it did nothing.

**The stop is awaited.** `StopProcessingAsync` keeps the worker `Task[]` and awaits `WhenAll`
after cancelling, so the workers have genuinely finished by the time it returns. It uses
`CancelAsync` rather than `Cancel`, since `Cancel` runs its callbacks on the calling thread —
the UI thread here.

**No event is lost.** Cancellation is observed at the top of the worker loop, never inside it.
Once an event has been read off the channel there is nowhere to hand it back — `Channel<T>` has
no peek/acknowledge — so the worker carries it through to exactly one success or one failure
before exiting. That costs one event's worth of work per worker on shutdown, and buys the
invariant that every event is either still queued or accounted for, never neither.

**Queued events are kept**, not dropped. Switching the workers back on drains them; Clear
discards them and reports how many it threw away. Clear is disabled during a run so the progress
baseline can't be reset underneath it.

## Tests

67 xUnit tests in `PrivacyEventProcessing.Test`, covering:

- Masking and hashing, including shortened IPv6 and per-key hash divergence.
- The validation rules, and that the pipeline entry point agrees with the per-field rules.
- Fault policy rates, band separation and message variety.
- Generated data validity, and that the malformed share hits every validation rule.
- The queue's drain behaviour and that it stays usable afterwards.
- The repository under concurrent writers, and snapshot isolation while a writer runs.
- The worker pool end to end — every event accounted for, a graceful and restartable stop,
  cancellation losing no in-flight event, and no original values reaching storage.
