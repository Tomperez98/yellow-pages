# Generating Model Files

Reference for creating Protocol.cs, State.cs, and Operations.cs. Read this when generating or modifying model files.

## Step 1: Extract operations

From the user's description, list every operation. For each, identify:
- **Name** — PascalCase verb phrase (e.g., `CreateAccount`, `TransferMoney`)
- **Request fields** — input data the operation needs
- **Response variants** — every possible outcome, mapped to an HTTP status code name
- **Preconditions** — what must be true in current state for each outcome
- **State changes** — how state transitions for success outcomes
- **Invariants** — what must always hold after state changes
- **Background work** — any async/background processing triggered by the operation

## Step 2: Define State (`State.cs`)

Only track what operations need to check to decide their responses. Use `[State]` partial classes with dictionaries, lists, and value types. Define enums for status fields.

```csharp
using Microsoft.Accordant;

namespace Spec.Model;

public enum ThingStatus { Pending, Completed, Failed }

[State]
public partial class ThingItem : State
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ThingStatus Status { get; set; } = ThingStatus.Pending;
}

[State]
public partial class ThingState : State
{
    public Dictionary<Guid, ThingItem> Items { get; set; } = [];
}
```

## Step 3: Define Protocol (`Protocol.cs`)

**Requests** — simple records:
```csharp
public record CreateThingRequest(string Name, int Quantity);
```

**Responses** — abstract record with private constructor + sealed nested variants. Variant names must use HTTP status code names:
```csharp
public abstract record CreateThingResponse
{
    private CreateThingResponse() { }
    public sealed record Created(Guid ThingId) : CreateThingResponse;
    public sealed record Conflict : CreateThingResponse;
    public sealed record BadRequest : CreateThingResponse;
}
```

Valid variant names: `Ok`, `Created`, `Accepted`, `NoContent` (success); `BadRequest`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `Gone`, `UnprocessableEntity`, `TooManyRequests` (client error); `InternalServerError`, `ServiceUnavailable` (server error).

## Step 4: Define Operations (`Operations.cs`)

Every operation follows this exact structure:

```
spec.Operation<TRequest, TResponse>("Name", (req, state) =>
{
    // 1. INPUT-ONLY validations — no state access. Cheap checks first. .SameState().
    // 2. STATE-DEPENDENT validations — need current state. .SameState().
    // 3. SUCCESS — Expect.That(...) with .ThenState(...)
    // 4. Inside ThenState: mutate state, then Invariant.Assert(...)
});
```

**Guard clause patterns:**

```csharp
// Input-only (no state access):
if (string.IsNullOrWhiteSpace(req.Name))
    return Expect.That<CreateResponse>(r => r is CreateResponse.BadRequest,
               "name cannot be empty")
           .SameState();

if (req.Priority < 1 || req.Priority > 5)
    return Expect.That<CreateResponse>(r => r is CreateResponse.BadRequest,
               "priority must be between 1 and 5")
           .SameState();

// State-dependent:
if (state.Items.Values.Any(i => i.Name == req.Name))
    return Expect.That<CreateResponse>(r => r is CreateResponse.Conflict,
               "an item with this name already exists")
           .SameState();

if (!state.Items.TryGetValue(req.Id, out var item))
    return Expect.That<GetResponse>(r => r is GetResponse.NotFound,
               "item not found")
           .SameState();
```

All error branches use `.SameState()`. The message string is optional but recommended.

**Response matching:**
```csharp
// Variant without data:
r => r is CreateResponse.Conflict

// Variant with data, checking a field:
r => r is CreateResponse.Created { ThingId: var id } && id != Guid.Empty

// Variant with data, comparing to expected value:
r => r is GetResponse.Ok { Status: var s } && s == expectedStatus
```

**Success with state change** — use two-argument `ThenState` `((response, nextState) => ...)` when state depends on response data (server-generated IDs). Always provide `mock:`:
```csharp
return Expect.That<CreateResponse>(
           r => r is CreateResponse.Created { ThingId: var id } && id != Guid.Empty,
           "successful creation returns Created with a valid ThingId")
       .ThenState<ThingState>((resp, s) =>
       {
           var id = ((CreateResponse.Created)resp).ThingId;
           s.Items[id] = new ThingItem { Id = id, Name = req.Name };
           Invariant.Assert(
               s.Items.Select(t => t.Name).Distinct().Count() == s.Items.Count,
               "duplicate names in state");
       }, mock: () => new CreateResponse.Created(Guid.CreateVersion7()));
```

Use single-argument `ThenState(nextState => ...)` when state doesn't depend on response data (no `mock:` needed):
```csharp
return Expect.That<DeleteResponse>(r => r is DeleteResponse.NoContent, "item deleted")
       .ThenState<ThingState>(next =>
       {
           next.Items.Remove(req.Id);
           Invariant.Assert(
               next.Items.Select(t => t.Name).Distinct().Count() == next.Items.Count,
               "duplicate names in state");
       });
```

**Read-only operations** — every branch uses `.SameState()`:
```csharp
spec.Operation<GetThingRequest, GetThingResponse>("GetThing", (req, state) =>
{
    if (!state.Items.TryGetValue(req.Id, out var item))
        return Expect.That<GetThingResponse>(r => r is GetThingResponse.NotFound,
                   "item not found")
               .SameState();

    return Expect.That<GetThingResponse>(
               r => r is GetThingResponse.Ok { Name: var n } && n == item.Name,
               $"returns item name '{item.Name}'")
           .SameState();
});
```

**Triggers for async/background work** — append `.Triggers()` after `ThenState`:
```csharp
.Triggers(
    AsyncOperation.Create<State>(
        isTerminal: s => /* predicate: true when background work is done */,
        // Single deterministic outcome:
        transition: next => /* mutates cloned state */,
        // OR multiple non-deterministic outcomes:
        transitions:
        [
            next => /* outcome A */,
            next => /* outcome B */,
        ]
    )
);
```

To share a server-generated ID between `ThenState` and `Triggers`, declare a variable before `ThenState` and capture it in both closures:
```csharp
Guid createdId = default;

return Expect.That<CreateResponse>(r => r is CreateResponse.Created { ... })
       .ThenState<State>((resp, s) =>
       {
           createdId = ((CreateResponse.Created)resp).ThingId;
           s.Items[createdId] = new ThingItem { Id = createdId, ... };
           Invariant.Assert(...);
       }, mock: () => ...)
       .Triggers(
           AsyncOperation.Create<State>(
               isTerminal: s => s.Items[createdId].Status != Pending,
               transitions:
               [
                   next => next.Items[createdId].Status = Completed,
               ]
           ));
```

**Invariant rules:**
- Call `Invariant.Assert()` at the end of **every** `ThenState` block that mutates state — no exceptions, even for removals.
- Each invariant is a boolean condition + descriptive message
- Good invariants: uniqueness constraints, referential integrity, valid enum values, non-negative counts, valid ranges
- Bad invariants: tautologies (e.g., `s.Count >= 0` on an unsigned type)
- For removal operations, assert the same invariant that was checked on creation — it still holds after removal and keeps the rule mechanical.

```csharp
Invariant.Assert(s.Users.Count <= s.UserLimit, "user limit exceeded");
Invariant.Assert(s.Balance >= 0, "balance cannot go negative");
Invariant.Assert(s.Items.All(t => t.Status != Status.Unknown), "unknown status");
Invariant.Assert(s.Items.Select(t => t.Slug).Distinct().Count() == s.Items.Count,
    "duplicate slugs");
```

### Adding to existing Operations.cs

When adding a new operation to an existing `Create()` method, insert it before the `return spec;` line. Match the existing indentation, comment style, and operation structure. Do not modify existing operations.

## Fault Injection (advanced — add when asked)

When the user asks for fault injection support, add `InternalServerError` and `ServiceUnavailable` variants to every response record, a `FaultInjection?` field to every request, and `Expect.OneOf()` branches to mutating operations. This lets Accordant explore scenarios where the system crashes mid-operation, finding bugs in partial-failure handling.

The mechanism is runtime-agnostic: every request carries an optional `FaultInjection?` field. Each target implementation interprets the fault flags its own way — the spec only cares about what the client can observe.

### CrashBeforeMutation — unambiguous failure

The server crashes before doing any work. The client gets a 500. The state is unchanged.

For **all operations** (mutating and read-only), add this branch after input-only validations and before state-dependent checks:

```csharp
// Fault injection: crash before any work — request never processed
if (req.Fault?.CrashBeforeMutation == true)
    return Expect
        .That<CreateResponse>(r => r is CreateResponse.InternalServerError,
            "server crashed before processing")
        .SameState();
```

### CrashAfterMutation — ambiguous failure (mutating operations only)

The server processes the mutation successfully but crashes before sending the response. The client gets a 500. But the client can't know whether the mutation happened or not — both outcomes are valid interpretations of the same response.

Use `Expect.OneOf()` to model both possibilities. Add this branch after state-dependent validations and before the success case:

```csharp
// Fault injection: crash after mutation — client doesn't know if it took effect
if (req.Fault?.CrashAfterMutation == true)
    return Expect.OneOf<CreateResponse>(
        // Scenario A: mutation happened, response lost
        Expect.That<CreateResponse>(r => r is CreateResponse.InternalServerError,
            "server crashed after mutation — item may have been created")
              .ThenState<ThingState>(next =>
              {
                  next.Items[req.Name] = new ThingItem { Name = req.Name };
                  Invariant.Assert(
                      next.Items.Select(t => t.Name).Distinct().Count() == next.Items.Count,
                      "duplicate names in state");
              }),
        // Scenario B: mutation did not happen
        Expect.That<CreateResponse>(r => r is CreateResponse.InternalServerError,
            "server crashed after mutation — item may NOT have been created")
              .SameState()
    );
```

**Key rules for `Expect.OneOf`:**
- Each branch is a full `Expect.That(...)` with its own `.ThenState(...)` or `.SameState()`
- Use `.ThenState(...)` for the "mutation happened" branch — apply the same state change as the success case
- Use `.SameState()` for the "mutation didn't happen" branch
- Both branches match the same response variant (`InternalServerError`)
- Do NOT provide a `mock:` inside `OneOf` branches
- `Expect.OneOf` is NOT needed for read-only operations — a crash during a read is unambiguous

### ServiceUnavailable

In addition to the explicit `CrashBeforeMutation` / `CrashAfterMutation` branches, every operation's response record includes a `ServiceUnavailable` variant. This models transient unavailability (the server is simply down). Add a branch for it after the other fault branches:

```csharp
if (req.Fault?.CrashBeforeMutation == true)  // already present
    ...

// Transient unavailability — server is down, no work was done
if (req.Fault?.ServiceUnavailable == true)   // if FaultInjection has this field
    return Expect
        .That<CreateResponse>(r => r is CreateResponse.ServiceUnavailable,
            "service unavailable")
        .SameState();
```

Note: include `ServiceUnavailable` as a variant on every response record, but the explicit `FaultInjection` trigger for it is optional. The variant itself lets Accordant generate sequences where the server returns 503 at any point.

### Complete operation with fault injection

Here is a full mutating operation with all branches in the correct order:

```csharp
spec.Operation<CreateThingRequest, CreateThingResponse>("CreateThing", (req, state) =>
{
    // 1. INPUT-ONLY validations
    if (string.IsNullOrWhiteSpace(req.Name))
        return Expect.That<CreateThingResponse>(r => r is CreateThingResponse.BadRequest)
               .SameState();

    // 2. FAULT: crash before mutation
    if (req.Fault?.CrashBeforeMutation == true)
        return Expect.That<CreateThingResponse>(r => r is CreateThingResponse.InternalServerError)
               .SameState();

    // 3. STATE-DEPENDENT validations
    if (state.Items.ContainsKey(req.Name))
        return Expect.That<CreateThingResponse>(r => r is CreateThingResponse.Conflict)
               .SameState();

    // 4. FAULT: crash after mutation (ambiguous)
    if (req.Fault?.CrashAfterMutation == true)
        return Expect.OneOf<CreateThingResponse>(
            Expect.That<CreateThingResponse>(r => r is CreateThingResponse.InternalServerError)
                  .ThenState<ThingState>(next =>
                  {
                      next.Items[req.Name] = new ThingItem { Name = req.Name };
                      Invariant.Assert(
                          next.Items.Select(t => t.Name).Distinct().Count() == next.Items.Count,
                          "duplicate names");
                  }),
            Expect.That<CreateThingResponse>(r => r is CreateThingResponse.InternalServerError)
                  .SameState()
        );

    // 5. SUCCESS
    return Expect.That<CreateThingResponse>(r => r is CreateThingResponse.Created)
           .ThenState<ThingState>(next =>
           {
               next.Items[req.Name] = new ThingItem { Name = req.Name };
               Invariant.Assert(
                   next.Items.Select(t => t.Name).Distinct().Count() == next.Items.Count,
                   "duplicate names");
           });
});
```
