# Timer Spec

Spec-driven conformance testing for a timer API, using [Microsoft Accordant](https://github.com/microsoft/accordant). One spec, three independent implementations — must behave identically.

## Quick start

```sh
# 1. Build
bun run build

# 2. Start the Bun server
bun run apps/server/src/index.ts
# → Server running on http://localhost:3000
```

## Commands

```sh
# List what's available
dotnet run --project apps/spec -- list-scenarios    # timer-lifecycle, timer-create-only, timer-slug-race
dotnet run --project apps/spec -- list-transitions  # timer-lifecycle
dotnet run --project apps/spec -- list-targets      # inmemory, http, stdio
```

### In-memory (no server needed)

```sh
dotnet run --project apps/spec -- test --target inmemory --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target inmemory --scenario timer-create-only
dotnet run --project apps/spec -- test --target inmemory --scenario timer-slug-race

# Race condition: with lock → pass, without → fail
dotnet run --project apps/spec -- test --target inmemory --scenario timer-slug-race --no-lock
```

### HTTP (Bun server must be running)

```sh
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --scenario timer-create-only
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --scenario timer-slug-race
```

### Stdio (Go binary)

```sh
dotnet run --project apps/spec -- test --target stdio --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target stdio --scenario timer-create-only
dotnet run --project apps/spec -- test --target stdio --scenario timer-slug-race   # passes — stdin serializes concurrent requests
```

### Transitions (hand-written conformance tests)

```sh
# Like unit tests, but every response is validated by the Accordant model
dotnet run --project apps/spec -- transition --target inmemory --transition timer-lifecycle
dotnet run --project apps/spec -- transition --target stdio --transition timer-lifecycle
dotnet run --project apps/spec -- transition --target http --url http://localhost:3000 --transition timer-lifecycle
```

### Conformance

```sh
# All three targets in one run
dotnet run --project apps/spec -- conformance --targets inmemory,http,stdio --url http://localhost:3000
```

## How it works

Three targets implement the same timer API — create by slug + deadline, auto-transitions `Active → Completed` on expiry:

| Target | Language | Transport |
|--------|----------|-----------|
| `inmemory` | C# | Direct calls |
| `http` | TypeScript/Bun | HTTP |
| `stdio` | Go | JSON-lines over stdin/stdout |

The spec defines `CreateTimer` + `GetTimer` operations and invariants (`no duplicate slugs`, `all timer IDs are version 7`). Accordant generates sequential and concurrent tests, runs them against each target, and asserts identical behavior.

The race scenario catches a TOCTOU gap: checking for a duplicate slug and then inserting with no lock in between. In-memory fixes it with `SemaphoreSlim`, HTTP with a promise-chain lock. Stdio passes trivially — stdin serializes concurrent requests.

### Scenarios vs Transitions

| | Scenarios (`IScenario`) | Transitions (`ITransition`) |
|---|---|---|
| How tests are created | Generated from state graph | Hand-written, step by step |
| Good for | Exploring state space, finding unknown bugs | Specific edge cases, regression tests |
| Assertions | Automatic (model validates all responses) | Automatic (model validates all responses) |
| Response access | Indirect (model picks inputs) | Direct (you hold the typed response) |

Transitions are like unit tests validated by the spec — you write the exact sequence, capture responses, and branch on them. The Accordant model checks every response against what the spec permits. If a response isn't allowed, the transition fails with a conformance error.
