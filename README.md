# Timer Spec — Accordant conformance testing

Spec-driven testing for a timer API using [Microsoft Accordant](https://github.com/microsoft/accordant). A single spec generates tests that run against three independent implementations of the same business logic, verifying they behave identically.

## Domain

Create a timer with a user-defined **slug** (unique) and a **deadline**. The timer starts `Active` and autonomously transitions to `Completed` when the deadline is reached — no client API call triggers this.

| Concept | Accordant feature |
|---------|-------------------|
| Deadline-based `Active → Completed` | `AsyncOperation.Create` + step function |
| Framework waits for async completion | `PollingSetup` on the `OperationInput` |
| Slug uniqueness across concurrent creates | `Invariant.Assert` + `GenerateConcurrentTests` |
| `--no-lock` flag to demonstrate the race | TOCTOU gap in `InMemoryServer` |

## Scenarios

| Scenario | Mode | What it tests |
|----------|------|---------------|
| `timer-lifecycle` | Sequential | Create with future + near-future deadline (5s), duplicate slug, empty slug. Polls `GetTimer` until the 5s timer completes. |
| `timer-create-only` | Sequential | Create-only: validation branches (Forbidden, BadRequest, Conflict) |
| `timer-slug-race` | Concurrent | Two users create the same slug concurrently — TOCTOU race detected by the invariant |

### Polling — async step function resolution

`TimerLifecycleScenario` creates a timer with a deadline 5 seconds in the future. The spec declares that background work transitions it to `Completed`:

```csharp
.Triggers(AsyncOperation.Create<TimerState>(
    isTerminal: s => !s.Items.Any(t =>
        t.Status == TimerStatus.Active && t.Deadline < DateTime.UtcNow),
    transitions: ... // Active → Completed for past-deadline items
))
```

The `OperationInput` tells the framework how to poll:

```csharp
create.With(req, "Create near-future timer")
      .WithPolling(new PollingSetup
      {
          Operation = "GetTimer",    // framework polls this repeatedly
          WaitTimeInMs = 100,        // every 100ms
          MaxRetryCount = 100,       // liveness: fail if still Active after 10s
      })
```

A derivation maps the `CreateTimer` response to a `GetTimer` polling request. The background `RunDeadlineMonitor` (500ms interval) fires after the deadline, the framework observes `Completed` via polling, and the step function terminates.

### Race condition detection

`TimerSlugRaceScenario` catches a TOCTOU race on slug uniqueness:

```sh
# With lock (default) — SemaphoreSlim serializes check-then-insert
$ dotnet run -- test --scenario timer-slug-race
✓ All tests passed

# Without lock — gap exposed, duplicate slug invariant fires
$ dotnet run -- test --scenario timer-slug-race --no-lock
✗ Some tests failed
The spec cannot explain the behavior of concurrently invoking the following operations
```

The gap in `InMemoryServer.CreateTimerAsync`:

```csharp
if (_state.Items.Any(t => t.Slug == req.Slug))  // ← check
    return new CreateTimerResponse.Conflict();

await Task.Yield();  // ← TOCTOU window: without lock, thread B enters here

_state.Items.Add(...);  // ← insert
```

The invariant in `Spec.cs` that catches the violation:

```csharp
Invariant.Assert(
    s.Items.Select(t => t.Slug).Distinct().Count() == s.Items.Count,
    "duplicate slugs"
);
```

`GenerateConcurrentTests` validates **linearizability**: even though operations run concurrently, results must be explainable by some sequential ordering. If both concurrent creates return `Created`, no sequential order explains it — the invariant fires.

## Targets

The `ITarget` interface bridges the spec runner and an implementation:

```
ITarget
├── AsyncReset()          — wipe state before each test case
└── AsyncSend<T>(T)       — send a request, get back a TargetResponse
```

| Target | Implementation | Communication | Use case |
|--------|---------------|---------------|----------|
| `inmemory` | C# (`InMemoryServer`) | Direct method calls | Fastest feedback during spec authoring |
| `http` | TypeScript/Bun (`apps/server`) | HTTP + JWT | Validates the real server over the wire |
| `stdio` | Go (`apps/stdio`) | JSON-lines over stdin/stdout | Validates a compiled binary with no network |

All three run a background deadline monitor that transitions `Active → Completed` asynchronously.

## How stdio works

The Go binary reads JSON-lines from stdin, writes one JSON-line response per line. `StdioTarget` spawns it as a child process.

**Envelope:**

```
→ {"type":"create_timer","payload":{"slug":"tea","deadline":"2026-07-29T...","claims":{...}}}
← {"status":201,"result":{"TimerId":"019f..."}}

→ {"type":"get_timer","payload":{"id":"019f...","claims":{...}}}
← {"status":200,"result":{"Status":"Completed"}}

→ {"type":"reset"}
← {"status":204}
```

No JWT — claims are passed inline since stdio is a trusted local pipe.

## Running

```sh
# List available commands
dotnet run --project apps/spec -- list-scenarios
dotnet run --project apps/spec -- list-targets

# Single target, single scenario
dotnet run --project apps/spec -- test --target inmemory --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target inmemory --scenario timer-slug-race

# Demo the race condition
dotnet run --project apps/spec -- test --target inmemory --scenario timer-slug-race --no-lock

# Conformance: one scenario against multiple targets
dotnet run --project apps/spec -- conformance --targets inmemory,http,stdio

# In-memory + stdio only (no server needed)
bun run conformance

# All three targets (requires Go, Bun, .NET)
bun run compliance

# With a running HTTP server
dotnet run --project apps/spec -- conformance --target http --url http://localhost:3000 --jwt-secret <secret>
```
