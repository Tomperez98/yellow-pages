# Timer Spec

Spec-driven conformance testing for a timer API, using [Microsoft Accordant](https://github.com/microsoft/accordant) and [xUnit](https://xunit.net/). One spec, three independent implementations — must behave identically.

A single `dotnet test` runs every test against every available backend. No env-var switching required.

## Quick start

```sh
bun run build        # build all packages
cd apps/spec && dotnet test  # run tests (in-memory, thread-safe)
```

## Targets

Three implementations behind a single `ITarget` interface:

| Target | Language | Transport | Enabled by default |
|--------|----------|-----------|---------------------|
| `inmemory` | C# | Direct calls | Yes |
| `http` | TypeScript/Bun | HTTP | Opt-in via `TIMER_URL` |
| `stdio` | Go | JSON-lines via stdin/stdout | Opt-in via `TIMER_STDIO_PATH` |

Every test is a `[Theory]` parameterized by target name via `TargetNames.All()`. The fixture creates all available targets and the test runner iterates over each. To add an optional target, set its environment variable:

```sh
# in-memory only (default)
cd apps/spec && dotnet test

# in-memory + HTTP
cd apps/spec && TIMER_URL=http://localhost:3000 dotnet test

# in-memory + stdio
cd apps/spec && TIMER_STDIO_PATH=../../../../stdio/stdio dotnet test

# all three
cd apps/spec && TIMER_URL=http://localhost:3000 TIMER_STDIO_PATH=../../../../stdio/stdio dotnet test
```

No `TIMER_TARGET` variable — all available targets are exercised together.

## Tests

Three files under `Tests/`:

### `TargetFixture.cs`

Creates and manages the lifecycle of all available `ITarget` instances. Exposes `Clients` keyed by name, plus the static `TargetNames.All()` that `[MemberData]` references.

### `ExampleTests.cs`

Hand-written examples organized by concern:

| Class | Tests | Parameterized |
|-------|-------|:---:|
| `ExampleCrudTests` | Create/Get valid/invalid/duplicate | Yes |
| `ExampleLifecycleTests` | Auto-complete on deadline | Yes |
| `ExampleConcurrencyTests` | Concurrent create race | Yes |
| `ExampleRaceConditionTests` | Lock vs. no-lock TOCTOU | No |
| `ExampleSpecValidatedTests` | End-to-end with `spec.Allows()` | Yes |

### `GeneratedTests.cs`

Accordant explores the full state space from the model in `Model/Operations.cs`, generating exhaustive sequential and concurrent test cases. Each runs against every target.

## Race condition

`InMemoryServer` uses `SemaphoreSlim` to serialize check-then-insert. Without it, two concurrent creates with the same slug both pass the existence check before either inserts.

`ExampleRaceConditionTests` is the only test class not parameterized — it directly constructs `InMemoryTarget` with `threadSafe: true` vs. `threadSafe: false` to prove the lock works. These variants only apply to the in-memory implementation.

```sh
cd apps/spec && dotnet test --filter "FullyQualifiedName~WithLock"     # always passes
cd apps/spec && dotnet test --filter "FullyQualifiedName~WithoutLock"  # TOCTOU exposed
```

`GeneratedTests.Concurrent_SlugRace_AllPass` also probes the race surface — Accordant explores concurrent interleavings where the duplicate-slug invariant would fire.
