# Generating Binding Files

Reference for creating ITarget.cs, ApiClient.cs, and InMemoryTarget.cs. Read this when generating or modifying binding and target files.

These files **derive artifacts from the model** — they don't define new behaviour, they translate the specification into runnable code. The InMemoryServer is a fake: it commits to single paths, returns concrete values (e.g. `Guid.CreateVersion7()` for IDs), and mirrors the model's guard clauses in the same order. This mirroring isn't coincidental — the fake's correctness depends on faithfully reproducing the model's logic. The ApiClient bridges Accordant's test engine to targets (in-memory or real HTTP). The ITarget is boilerplate that adapts any backend to the ApiClient.

## ITarget.cs

This file is boilerplate — same structure every time, only the namespace changes:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spec.Targets;

public abstract record TargetResponse
{
    private TargetResponse() { }

    public sealed record Ok(HttpStatusCode Status, string Data) : TargetResponse
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public T Deserialize<T>() => JsonSerializer.Deserialize<T>(Data, _jsonOptions)!;
    }

    public sealed record Err(HttpStatusCode Status, string Error) : TargetResponse;
}

public interface ITarget
{
    Task AsyncReset();
    Task<TargetResponse> AsyncSend<TRequest>(TRequest request);
}
```

## ApiClient.cs

The ApiClient bridges Accordant to the ITarget. It has four parts:

### Constructor

Takes an `ITarget`:
```csharp
using System.Net;
using Microsoft.Accordant;
using Spec.Model;
using Spec.Targets;

namespace Spec;

public class ApiClient(ITarget target)
{
```

### BindTo

Static method that registers every operation with Accordant. One `.BindAsync` per operation:
```csharp
    public static void BindTo(Spec<ThingState> spec)
    {
        spec.ExecuteWith<ApiClient>()
            .BindAsync<CreateThingRequest, CreateThingResponse>(
                "CreateThing",
                (c, req) => c.CreateThingAsync(req)
            )
            .BindAsync<GetThingRequest, GetThingResponse>(
                "GetThing",
                (c, req) => c.GetThingAsync(req)
            );
        // ... one .BindAsync per operation
    }
```

### ResetAsync

Delegates to target:
```csharp
    public Task ResetAsync() => target.AsyncReset();
```

### Async methods (one per operation)

Each method calls `target.AsyncSend()`, then switches on `TargetResponse.Ok`/`TargetResponse.Err`:

```csharp
    public async Task<CreateThingResponse> CreateThingAsync(CreateThingRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: HttpStatusCode.Created } ok =>
                new CreateThingResponse.Created(ok.Deserialize<CreateOk>().ThingId),
            TargetResponse.Err { Status: HttpStatusCode.Conflict } =>
                new CreateThingResponse.Conflict(),
            TargetResponse.Err { Status: HttpStatusCode.BadRequest } =>
                new CreateThingResponse.BadRequest(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }
```

### Mapping rules

**For each async method:**
- `TargetResponse.Ok` with the matching success status code → deserialize the data payload into the success response variant.
- `TargetResponse.Err` with each error status code → return the corresponding error variant (no data payload for variants without fields).
- Always add `_ => throw new InvalidOperationException(...)` as the fallback.
- If the success variant has data fields, define a `private record` for deserialization inside the class. Only include the fields the variant carries.

**HttpStatusCode mapping:**

| Variant | HttpStatusCode |
|---|---|
| `Ok` | `HttpStatusCode.OK` |
| `Created` | `HttpStatusCode.Created` |
| `Accepted` | `HttpStatusCode.Accepted` |
| `NoContent` | `HttpStatusCode.NoContent` |
| `BadRequest` | `HttpStatusCode.BadRequest` |
| `Unauthorized` | `HttpStatusCode.Unauthorized` |
| `Forbidden` | `HttpStatusCode.Forbidden` |
| `NotFound` | `HttpStatusCode.NotFound` |
| `Conflict` | `HttpStatusCode.Conflict` |
| `Gone` | `HttpStatusCode.Gone` |
| `UnprocessableEntity` | `HttpStatusCode.UnprocessableEntity` |
| `TooManyRequests` | `HttpStatusCode.TooManyRequests` |
| `InternalServerError` | `HttpStatusCode.InternalServerError` |
| `ServiceUnavailable` | `HttpStatusCode.ServiceUnavailable` |

**Deserialization records** — private records inside ApiClient, one per success variant that carries data:
```csharp
    private record CreateOk(Guid ThingId);
    private record GetOk(ThingStatus Status);
```

The field names must match the JSON property names from the serialized response.

### Read-only operation example

```csharp
    public async Task<GetThingResponse> GetThingAsync(GetThingRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok ok => new GetThingResponse.Ok(ok.Deserialize<GetOk>().Status),
            TargetResponse.Err { Status: HttpStatusCode.NotFound } =>
                new GetThingResponse.NotFound(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }
```

### Adding to existing ApiClient.cs

When adding a new operation:
1. Add the `.BindAsync(...)` line to `BindTo`
2. Add the async method following the pattern above
3. Add deserialization record(s) if the success variant has data fields
4. Preserve existing methods and records — do not modify them

## InMemoryTarget.cs

Contains two classes: `InMemoryTarget` (adapts `InMemoryServer` to `ITarget`) and `InMemoryServer` (the actual implementation).

### InMemoryTarget

Dispatches requests to the server and converts responses to `TargetResponse`:

```csharp
using System.Net;
using System.Text.Json;
using Spec.Model;

namespace Spec.Targets;

public class InMemoryTarget(InMemoryServer server) : ITarget
{
    public Task AsyncReset()
    {
        server.Reset();
        return Task.CompletedTask;
    }

    public async Task<TargetResponse> AsyncSend<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = request switch
        {
            CreateThingRequest r => ToResult(await server.CreateThingAsync(r)),
            GetThingRequest r => ToResult(await server.GetThingAsync(r)),
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}"),
        };
        return response;
    }

    private static TargetResponse ToResult(object resp) =>
        resp switch
        {
            CreateThingResponse.Created => Ok(HttpStatusCode.Created, resp),
            CreateThingResponse.Conflict => Err(HttpStatusCode.Conflict),
            CreateThingResponse.BadRequest => Err(HttpStatusCode.BadRequest),
            GetThingResponse.Ok => Ok(HttpStatusCode.OK, resp),
            GetThingResponse.NotFound => Err(HttpStatusCode.NotFound),
            _ => throw new ArgumentException($"Unknown response type: {resp.GetType().Name}"),
        };

    private static TargetResponse.Ok Ok(HttpStatusCode status, object data) =>
        new(status, JsonSerializer.Serialize(data, data.GetType()));

    private static TargetResponse.Err Err(HttpStatusCode status) => new(status, status.ToString());
}
```

The `request switch` must cover every request type. The `ToResult` switch must cover every response variant. Variants **with data fields** use `Ok(status, resp)` (serializes the response to JSON). Variants **without data** use `Err(status)`.

### InMemoryServer

The InMemoryServer is a real in-memory implementation that mirrors the spec's guard clauses. Derive it from Operations.cs. Keep it simple — no locks, no thread-safety flags. The purpose is to serve as a straightforward fake that matches the spec's behavior.

**Structure:**
```csharp
public class InMemoryServer : IDisposable
{
    private readonly ThingState _initial;
    private ThingState _state;
    private readonly CancellationTokenSource _backgroundCts = new();

    public InMemoryServer(ThingState initialState, int backgroundCheckMs = 500)
    {
        _initial = Clone(initialState);
        _state = Clone(initialState);
        _ = RunBackgroundWork(backgroundCheckMs, _backgroundCts.Token);
    }
```

**Method generation rules — for each operation:**

1. **Input-only validations first** — mirror the same checks from Operations.cs:
```csharp
    public async Task<CreateThingResponse> CreateThingAsync(CreateThingRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return new CreateThingResponse.BadRequest();
        if (req.Priority < 1 || req.Priority > 5)
            return new CreateThingResponse.BadRequest();
```

2. **State-dependent checks and mutations:**
```csharp
        if (_state.Items.Values.Any(t => t.Name == req.Name))
            return new CreateThingResponse.Conflict();

        await Task.Yield(); // simulate async gap — enables TOCTOU race detection

        var id = Guid.CreateVersion7();
        _state.Items[id] = new ThingItem { Id = id, Name = req.Name, Priority = req.Priority };
        return new CreateThingResponse.Created(id);
    }
```

3. **Success cases** create the response AND mutate `_state`. Use `Guid.CreateVersion7()` for server-generated IDs.

4. **Read-only operations** never mutate:
```csharp
    public async Task<GetThingResponse> GetThingAsync(GetThingRequest req)
    {
        if (!_state.Items.TryGetValue(req.Id, out var item))
            return new GetThingResponse.NotFound();
        return new GetThingResponse.Ok(item.Status);
    }
```

### Background work (Triggers)

For each operation that has `.Triggers()` in Operations.cs, generate a background loop. Map the `isTerminal` predicate and `transitions`/`transition`:

```csharp
    private async Task RunBackgroundWork(int intervalMs, CancellationToken ct)
    {
        var rng = new Random();
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(intervalMs, ct);
            // For each spec trigger, iterate items matching the inverted isTerminal
            foreach (var item in _state.Items.Values
                .Where(t => /* INVERTED isTerminal condition */))
            {
                // Apply transition(s) from the spec
                item.Status = ThingStatus.Completed; // from transition lambda
            }
        }
    }
```

**Mapping Triggers to background loop:**

- `isTerminal: s => condition` → iterate items where `!condition`
- `transition: next => next.X = Y` → apply `X = Y` to each item
- `transitions: [a, b]` → apply all transitions to each item (if multiple transitions, apply each one)

If there are **no Triggers** in the spec, omit the background loop and `_backgroundCts`:
```csharp
    public InMemoryServer(ThingState initialState)
    {
        _initial = Clone(initialState);
        _state = Clone(initialState);
    }
```

### Reset and Dispose

```csharp
    public void Reset()
    {
        _state = Clone(_initial);
    }

    public void Dispose()
    {
        _backgroundCts.Cancel();
        _backgroundCts.Dispose();
    }
```

If no background work, Dispose can be empty (no resources to clean up):
```csharp
    public void Dispose() { }
```

### Clone

Must deep-copy every field of every state class:

```csharp
    private static ThingState Clone(ThingState s) =>
        new()
        {
            Items = s.Items.ToDictionary(
                kv => kv.Key,
                kv => new ThingItem
                {
                    Id = kv.Value.Id,
                    Name = kv.Value.Name,
                    Priority = kv.Value.Priority,
                    Status = kv.Value.Status,
                }
            ),
        };
```

For dictionaries: iterate entries, creating new value objects for each. For lists: use `[.. s.Items.Select(i => new ThingItem { ... })]`. The clone must produce an independent copy so `Reset()` works correctly.

### Adding to existing InMemoryTarget.cs

When adding a new operation:
1. Add the request case to `AsyncSend`'s request switch
2. Add all response variant cases to `ToResult`
3. Add the new method to `InMemoryServer` following the **same pattern as existing methods** — if existing methods use locks/SemaphoreSlim, your new method must too. If they're simple (no locks), keep it simple. Match the existing style exactly.
4. If the operation has Triggers, add the background work to `RunBackgroundWork`
5. Update `Clone` if state fields changed

## Fault Injection (advanced — add when asked)

When the user asks for fault injection support, add crash simulation to the InMemoryServer. This pairs with the model-side changes in `generating-model.md` (adding `InternalServerError`/`ServiceUnavailable` variants, `FaultInjection` record, and `Expect.OneOf()` branches).

### Protocol and ApiClient additions

When fault injection is active, every request record gains `FaultInjection? Fault = null` and every response record gains `InternalServerError` and `ServiceUnavailable` variants. The ApiClient switch expressions and `ToResult` switch must include cases for these new variants.

### InMemoryServer crash implementation

For **mutating operations**, add two crash points:

```csharp
    public async Task<CreateThingResponse> CreateThingAsync(CreateThingRequest req)
    {
        // 1. Input-only validations (unchanged)
        if (string.IsNullOrWhiteSpace(req.Name))
            return new CreateThingResponse.BadRequest();

        // 2. CrashBeforeMutation — state untouched
        if (req.Fault?.CrashBeforeMutation == true)
            throw new InvalidOperationException("Simulated crash before mutation");

        // 3. State-dependent checks
        if (_state.Items.Values.Any(t => t.Name == req.Name))
            return new CreateThingResponse.Conflict();

        await Task.Yield();

        var id = Guid.CreateVersion7();
        _state.Items[id] = new ThingItem { Id = id, Name = req.Name };

        // 4. CrashAfterMutation — state changed, response lost
        if (req.Fault?.CrashAfterMutation == true)
            throw new InvalidOperationException("Simulated crash after mutation");

        return new CreateThingResponse.Created(id);
    }
```

**Rules:**
- `CrashBeforeMutation`: throw `InvalidOperationException` after input validations but **before** state-dependent checks. State is never touched.
- `CrashAfterMutation`: mutate `_state`, then throw **before** returning the success response. State changed but the client never finds out — this is the ambiguous case that `Expect.OneOf()` models.
- The exception type must be `InvalidOperationException`. The `InMemoryTarget.AsyncSend` method does NOT catch it, so the caller sees an unhandled exception (just like a real network timeout).
- For **read-only operations**, only `CrashBeforeMutation` applies. `CrashAfterMutation` has no meaning for reads.
