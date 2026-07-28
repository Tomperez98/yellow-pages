# Yellow Pages — Spec & Conformance

Spec-driven testing for the Yellow Pages API using [Microsoft Accordant](https://github.com/microsoft/accordant). A single spec generates tests that run against three independent implementations of the same business logic, verifying they behave identically.

## Scenarios

Scenarios declare test generation strategy and inputs. Implement `IScenario`:

```csharp
public interface IScenario
{
    TestSuite BuildTests(Spec<YellowPagesState> spec, YellowPagesState initialState);
}
```

`BuildTests` returns a `TestSuite` — either `Sequential` or `Concurrent`:

```csharp
// Sequential: calls spec.GenerateTests internally
return new TestSuite.Sequential(spec.GenerateTests(initialState, inputs, options));

// Concurrent: calls spec.GenerateConcurrentTests — tests all interleavings
return new TestSuite.Concurrent(spec.GenerateConcurrentTests(initialState, inputs, options));
```

Registered scenarios:

```sh
dotnet run -- list-scenarios
```

| Scenario | Mode | What it tests |
|----------|------|---------------|
| `country-crud` | Sequential | Full CRUD: create, update, delete derivations |
| `country-create-only` | Sequential | Create-only, including duplicate detection |
| `country-create-race` | Concurrent | Two admins create the same country code concurrently — TOCTOU race |

### Race conditions

`GenerateConcurrentTests` validates **linearizability**: even though operations run concurrently, the results must be explainable by some sequential ordering. If both concurrent creates return `Created`, no sequential order explains it → bug.

The `country-create-race` scenario catches a TOCTOU race when two admins simultaneously create the same country code. The in-memory server exposes the gap between check and write via `Task.Yield()`:

```sh
# Thread-safe (default) — lock protects the check-and-write
$ dotnet run -- test --scenario country-create-race
✓ All tests passed

# --no-lock — gap exposed, race detected
$ dotnet run -- test --scenario country-create-race --no-lock
✗ Some tests failed
The spec cannot explain the behavior of concurrently invoking the following operations
```

`InMemoryServer` takes a `threadSafe` parameter (default `true`). When `false`, the `SemaphoreSlim` is bypassed, turning the server into a demonstration of what happens without proper synchronization.

## Targets

The `ITarget` interface defines the bridge between the spec runner and an implementation:

```
ITarget
├── AsyncReset()          — wipe state before each test case
└── AsyncSend<T>(T)       — send a request, get back a TargetResponse
```

Three targets exist, each implementing the same CRUD operations (create/update/delete countries):

| Target | Implementation | Communication | Use case |
|--------|---------------|---------------|----------|
| `inmemory` | C# (`InMemoryServer`) | Direct method calls | Fastest feedback during spec authoring |
| `http` | TypeScript/Bun (`apps/server`) | HTTP + JWT | Validates the real server over the wire |
| `stdio` | Go (`apps/stdio`) | JSON-lines over stdin/stdout | Validates a compiled binary with no network |

## How stdio works

The Go binary reads JSON-lines from stdin and writes one JSON-line response per line. The C# `StdioTarget` spawns the binary as a child process and talks this protocol over pipes.

**Envelope:**

```
→ {"type":"create_country", "payload":{"code":"US","claims":{...}}}
← {"status":201, "result":{"CountryId":"019f..."}}

→ {"type":"reset"}
← {"status":204}
```

Each operation gets its own typed payload struct. Error responses omit `result` and include `error`:

```
← {"status":409, "error":"Country already exists"}
```

No JWT — claims are passed inline since stdio is a trusted local pipe.

## Running

```sh
# List available commands
dotnet run --project apps/spec -- list-scenarios
dotnet run --project apps/spec -- list-targets

# Single target, single scenario
dotnet run --project apps/spec -- test --target inmemory --scenario country-crud
dotnet run --project apps/spec -- test --target inmemory --scenario country-create-race
dotnet run --project apps/spec -- test --target inmemory --scenario country-create-race --no-lock  # demo the race

# Conformance: one scenario against multiple targets
dotnet run --project apps/spec -- conformance --targets inmemory,http,stdio

# All three targets (requires Go, Bun, .NET)
bun run compliance

# In-memory + stdio only (no server needed)
bun run conformance

# With a running HTTP server
dotnet run --project apps/spec -- conformance --target http --url http://localhost:3000 --jwt-secret <secret>
```
