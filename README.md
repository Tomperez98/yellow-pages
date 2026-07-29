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
dotnet run --project apps/spec -- list-scenarios   # timer-lifecycle, timer-create-only, timer-slug-race
dotnet run --project apps/spec -- list-targets     # inmemory, http, stdio
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
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --jwt-secret 'dev-secret-at-least-128-bits-long!!' --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --jwt-secret 'dev-secret-at-least-128-bits-long!!' --scenario timer-create-only
dotnet run --project apps/spec -- test --target http --url http://localhost:3000 --jwt-secret 'dev-secret-at-least-128-bits-long!!' --scenario timer-slug-race
```

### Stdio (Go binary)

```sh
dotnet run --project apps/spec -- test --target stdio --scenario timer-lifecycle
dotnet run --project apps/spec -- test --target stdio --scenario timer-create-only
dotnet run --project apps/spec -- test --target stdio --scenario timer-slug-race   # passes — stdin serializes concurrent requests
```

### Conformance

```sh
# All three targets in one run
dotnet run --project apps/spec -- conformance --targets inmemory,http,stdio \
  --url http://localhost:3000 \
  --jwt-secret 'dev-secret-at-least-128-bits-long!!'
```

## How it works

Three targets implement the same timer API — create by slug + deadline, auto-transitions `Active → Completed` on expiry:

| Target | Language | Transport |
|--------|----------|-----------|
| `inmemory` | C# | Direct calls |
| `http` | TypeScript/Bun | HTTP + JWT |
| `stdio` | Go | JSON-lines over stdin/stdout |

The spec defines `CreateTimer` + `GetTimer` operations and invariants (`no duplicate slugs`, `all timer IDs are version 7`). Accordant generates sequential and concurrent tests, runs them against each target, and asserts identical behavior.

The race scenario catches a TOCTOU gap: checking for a duplicate slug and then inserting with no lock in between. In-memory fixes it with `SemaphoreSlim`, HTTP with a promise-chain lock. Stdio passes trivially — stdin serializes concurrent requests.
