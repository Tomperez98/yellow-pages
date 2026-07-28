# Yellow Pages — Spec & Conformance

Spec-driven testing for the Yellow Pages API using [Microsoft Accordant](https://github.com/microsoft/accordant). A single spec generates tests that run against three independent implementations of the same business logic, verifying they behave identically.

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
# All three targets (requires Go, Bun, .NET)
bun run compliance

# In-memory + stdio only (no server needed)
bun run conformance

# Single target
dotnet run --project apps/spec -- conformance --target inmemory
bun run --cwd apps/stdio build && dotnet run --project apps/spec -- conformance --target stdio
dotnet run --project apps/spec -- conformance --target http --url http://localhost:3000 --jwt-secret <secret>
```
