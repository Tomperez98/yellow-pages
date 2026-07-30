# Generating Test Files

Reference for creating TargetFixture.cs, ExampleTests.cs, and GeneratedTests.cs. Read this when generating a complete project from scratch.

## Detect the test framework

Before generating tests, ask the user which test framework they're using. If they don't specify, default to **xUnit** (the most common in .NET Accordant projects). The templates below are xUnit, but adapt to NUnit or MSTest if the user's project uses them.

Key adaptations between frameworks:

| Concept | xUnit | NUnit | MSTest |
|---|---|---|---|
| Test class attribute | `[Fact]` / `[Theory]` | `[Test]` / `[TestCase]` | `[TestMethod]` |
| Fixture/Setup | `IClassFixture<T>` | `[SetUp]` / `[OneTimeSetUp]` | `[TestInitialize]` / `[ClassInitialize]` |
| Parameterized | `[Theory]` + `[MemberData]` | `[TestCaseSource]` | `[DataRow]` / `[DynamicData]` |
| Assert | `Assert.Equal(...)` | `Assert.That(..., Is.EqualTo(...))` | `Assert.AreEqual(...)` |
| Assert type | `Assert.IsType<T>(...)` | `Assert.That(..., Is.InstanceOf<T>())` | `Assert.IsInstanceOfType(...)` |

## TargetFixture.cs

Provides every available `ITarget` so a single `dotnet test` run covers all backends. Tests reference it via `IClassFixture<TargetFixture>` and iterate targets with `[MemberData]`.

```csharp
using Spec.Model;
using Spec.Targets;
using Xunit;

namespace Spec.Tests;

/// <summary>
/// Creates every available <see cref="ITarget"/> so a single
/// <c>dotnet test</c> run covers all backends. Targets are
/// addressed by name from <see cref="TargetNames.All"/>.
///
/// <list type="bullet">
/// <item><c>"inmemory"</c> — always available</item>
/// <item><c>"http"</c> — enabled when <c>{PROJECT}_URL</c> is set</item>
/// <item><c>"stdio"</c> — enabled when <c>{PROJECT}_STDIO_PATH</c> is set</item>
/// </list>
/// </summary>
public class TargetFixture : IAsyncLifetime
{
    public IReadOnlyDictionary<string, ApiClient> Clients { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var clients = new Dictionary<string, ApiClient>();

        foreach (var name in TargetNames.Available)
        {
            switch (name)
            {
                case "inmemory":
                    var server = new InMemoryServer(new {StateType}());
                    clients[name] = new ApiClient(new InMemoryTarget(server));
                    break;
                case "http":
                    var url = Environment.GetEnvironmentVariable("{PROJECT}_URL")!;
                    clients[name] = new ApiClient(new HttpTarget(url));
                    break;
                case "stdio":
                    var path = Environment.GetEnvironmentVariable("{PROJECT}_STDIO_PATH")!;
                    clients[name] = new ApiClient(new StdioTarget(path));
                    break;
            }
        }

        Clients = clients;

        foreach (var client in Clients.Values)
            await client.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public static class TargetNames
{
    public static IReadOnlyList<string> Available { get; }

    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        foreach (var name in Available)
            data.Add(name);
        return data;
    }

    static TargetNames()
    {
        var names = new List<string> { "inmemory" };

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("{PROJECT}_URL")))
            names.Add("http");

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("{PROJECT}_STDIO_PATH")))
            names.Add("stdio");

        Available = names;
    }
}
```

Replace `{StateType}` with the state type (e.g., `TimerState`, `OrderState`) and `{PROJECT}` with the project name in UPPER_SNAKE_CASE (e.g., `TIMER`, `ORDER`).

**Rules:**
- The `inmemory` target is always available — it's the zero-config default
- `http` and `stdio` targets are conditional on environment variables
- `TargetNames.All()` returns `TheoryData<string>` for use with `[MemberData]`
- `TargetFixture.InitializeAsync` calls `ResetAsync` on all clients after construction

## ExampleTests.cs

Hand-written tests covering CRUD, lifecycle, concurrency, and race conditions. Each `[Theory]` runs against every target via `TargetFixture`.

### Structure

Organize tests into nested classes by concern:

```csharp
using Microsoft.Accordant;
using Spec.Model;
using Spec.Targets;
using Xunit;

namespace Spec.Tests;

// --- CRUD Tests ---
public class ExampleCrudTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => TargetNames.All();

    // Success cases: exercise every operation's happy path
    // Error cases: BadRequest, NotFound, Conflict for every operation
}

// --- Lifecycle Tests ---
public class ExampleLifecycleTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => TargetNames.All();

    // Tests that exercise async state transitions (Triggers)
    // Poll for expected state changes with timeout
}

// --- Concurrency Tests ---
public class ExampleConcurrencyTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => TargetNames.All();

    // Concurrent requests — race conditions, consistency under load
}
```

### CRUD test examples

For each mutating operation, write:
- **Success case** — verify response type, check server-generated fields
- **Every error variant** — one test per guard clause, verify the right error type

```csharp
[Theory]
[MemberData(nameof(AllTargets))]
public async Task CreateThing_ValidRequest_ReturnsCreated(string targetName)
{
    var client = fixture.Clients[targetName];
    var resp = await client.CreateThingAsync(new CreateThingRequest("name", 3));

    var created = Assert.IsType<CreateThingResponse.Created>(resp);
    Assert.NotEqual(Guid.Empty, created.ThingId);
}

[Theory]
[MemberData(nameof(AllTargets))]
public async Task CreateThing_EmptyName_ReturnsBadRequest(string targetName)
{
    var client = fixture.Clients[targetName];
    var resp = await client.CreateThingAsync(new CreateThingRequest("", 3));
    Assert.IsType<CreateThingResponse.BadRequest>(resp);
}

[Theory]
[MemberData(nameof(AllTargets))]
public async Task CreateThing_DuplicateName_ReturnsConflict(string targetName)
{
    var client = fixture.Clients[targetName];
    await client.CreateThingAsync(new CreateThingRequest("unique", 3));
    var resp = await client.CreateThingAsync(new CreateThingRequest("unique", 3));
    Assert.IsType<CreateThingResponse.Conflict>(resp);
}
```

For read operations:
```csharp
[Theory]
[MemberData(nameof(AllTargets))]
public async Task GetThing_ExistingId_ReturnsOk(string targetName)
{
    var client = fixture.Clients[targetName];
    var created = await client.CreateThingAsync(new CreateThingRequest("test", 3));
    var id = ((CreateThingResponse.Created)created).ThingId;

    var resp = await client.GetThingAsync(new GetThingRequest(id));
    var ok = Assert.IsType<GetThingResponse.Ok>(resp);
    // Assert on fields if the response carries data
}

[Theory]
[MemberData(nameof(AllTargets))]
public async Task GetThing_NonexistentId_ReturnsNotFound(string targetName)
{
    var client = fixture.Clients[targetName];
    var resp = await client.GetThingAsync(new GetThingRequest(Guid.CreateVersion7()));
    Assert.IsType<GetThingResponse.NotFound>(resp);
}
```

### Lifecycle tests (for specs with Triggers)

When the spec has async transitions, write a lifecycle test that polls for completion:

```csharp
[Theory]
[MemberData(nameof(AllTargets))]
public async Task Thing_TransitionsToCompleted_AfterCreation(string targetName)
{
    var client = fixture.Clients[targetName];
    var created = await client.CreateThingAsync(new CreateThingRequest("transition-me", 1));
    var id = ((CreateThingResponse.Created)created).ThingId;

    // Poll until async transition fires
    for (var i = 0; i < 200; i++)
    {
        await Task.Delay(50);
        var resp = await client.GetThingAsync(new GetThingRequest(id));
        if (resp is GetThingResponse.Ok { Status: ThingStatus.Completed })
            return;
    }

    Assert.Fail($"Thing did not transition within 10 seconds [{targetName}]");
}
```

### Concurrency tests

```csharp
[Theory]
[MemberData(nameof(AllTargets))]
public async Task ConcurrentCreate_SameUniqueField_AtMostOneSucceeds(string targetName)
{
    var client = fixture.Clients[targetName];
    var uniqueValue = $"unique-{Guid.NewGuid():N}";
    var taskA = client.CreateThingAsync(new CreateThingRequest(uniqueValue, 3));
    var taskB = client.CreateThingAsync(new CreateThingRequest(uniqueValue, 3));
    var results = await Task.WhenAll(taskA, taskB);

    var createdCount = results.Count(r => r is CreateThingResponse.Created);
    Assert.True(createdCount <= 1,
        $"Expected at most 1 Created, got {createdCount} — race condition [{targetName}]");
}

[Theory]
[MemberData(nameof(AllTargets))]
public async Task ConcurrentCreate_DifferentValues_BothSucceed(string targetName)
{
    var client = fixture.Clients[targetName];
    var taskA = client.CreateThingAsync(new CreateThingRequest($"a-{Guid.NewGuid():N}", 3));
    var taskB = client.CreateThingAsync(new CreateThingRequest($"b-{Guid.NewGuid():N}", 3));
    var results = await Task.WhenAll(taskA, taskB);

    Assert.All(results, r => Assert.IsType<CreateThingResponse.Created>(r));
}
```

### Spec-validated tests

Write at least one test that validates responses against the Accordant model directly using `spec.Allows()`. This proves the spec and the implementation agree:

```csharp
[Theory]
[MemberData(nameof(AllTargets))]
public async Task Lifecycle_ValidatesAllResponsesAgainstSpec(string targetName)
{
    var client = fixture.Clients[targetName];
    var spec = {SpecClass}.Create();
    ApiClient.BindTo(spec);

    var createOp = spec.GetOperation<CreateThingRequest, CreateThingResponse>("CreateThing");
    var getOp = spec.GetOperation<GetThingRequest, GetThingResponse>("GetThing");

    await client.ResetAsync();
    var stateProfile = new StateProfile(new {StateType}());

    // Create
    var createReq = new CreateThingRequest("valid-name", 3);
    var createResp = await client.CreateThingAsync(createReq);
    var (isValid, message, next) = spec.Allows(createOp, createReq, createResp, stateProfile);
    Assert.True(isValid, message);
    stateProfile = next;

    // Get
    var created = Assert.IsType<CreateThingResponse.Created>(createResp);
    var getReq = new GetThingRequest(created.ThingId);
    var getResp = await client.GetThingAsync(getReq);
    (isValid, message, _) = spec.Allows(getOp, getReq, getResp, stateProfile);
    Assert.True(isValid, message);
}
```

**What to adapt per spec:**
- Operation names must match the Operations.cs definitions
- Request types and response variants must match Protocol.cs
- Replace `{SpecClass}` with the spec class name (e.g., `TimerSpec`)
- Replace `{StateType}` with the state class name
- For specs without Triggers, skip the lifecycle polling test
- For specs with server-generated fields, always assert they're not default/empty
- Generate one CRUD test class covering all operations, one lifecycle class if there are triggers, one concurrency class

## GeneratedTests.cs

Accordant-generated test suites that explore the full state space. These use `spec.GenerateTests()` and `spec.RunTests()` to systematically exercise the model.

```csharp
using Microsoft.Accordant;
using Spec.Model;
using Xunit;

namespace Spec.Tests;

public class GeneratedTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => TargetNames.All();

    /// <summary>
    /// Sequential execution: creates valid requests and one error case.
    /// Each step validates against the model. For CRUD specs without
    /// async triggers, this is the main generated test.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Sequential_AllOperations_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = {SpecClass}.Create();
        ApiClient.BindTo(spec);

        var context = spec.CreateTestingContext();
        context.Register(client);

        var createOp = spec.GetOperation<CreateThingRequest, CreateThingResponse>("CreateThing");
        var inputs = new InputSet
        {
            createOp
                .With(new CreateThingRequest("valid", 3), "Create valid thing")
                .WithoutPolling(),
            createOp
                .With(new CreateThingRequest("", 3), "Empty name — should BadRequest"),
        };

        var cases = spec.GenerateTests(new {StateType}(), inputs, new() { MaxDepth = 3 });
        var results = await spec.RunTests(
            context,
            new {StateType}(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(results, r =>
            Assert.True(r.Success,
                r.LastFailureMessage ?? $"Accordant sequential test failed [{targetName}]"));
    }
}
```

**If the spec has Triggers (async transitions):**

Add a lifecycle generated test with polling setup:

```csharp
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Sequential_Lifecycle_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = {SpecClass}.Create();
        ApiClient.BindTo(spec);

        // Wire up derivations so Accordant can construct Get requests from Create responses
        spec.ConfigureDerivations(
            "GetThing",
            Derive
                .From<CreateThingRequest, CreateThingResponse, GetThingRequest>("CreateThing")
                .When((_, resp) => resp is CreateThingResponse.Created)
                .As((_, resp) => new GetThingRequest(((CreateThingResponse.Created)resp).ThingId))
        );

        var context = spec.CreateTestingContext();
        context.Register(client);

        var createOp = spec.GetOperation<CreateThingRequest, CreateThingResponse>("CreateThing");
        var inputs = new InputSet
        {
            createOp
                .With(new CreateThingRequest("lifecycle-test", 1), "Create thing (triggers async)")
                .WithPolling(new PollingSetup
                {
                    Operation = "GetThing",
                    WaitTimeInMs = 100,
                    MaxRetryCount = 100,
                }),
        };

        var cases = spec.GenerateTests(new {StateType}(), inputs, new() { MaxDepth = 3 });
        var results = await spec.RunTests(
            context,
            new {StateType}(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(results, r =>
            Assert.True(r.Success,
                r.LastFailureMessage ?? $"Accordant lifecycle test failed [{targetName}]"));
    }
```

**Concurrent test (optional but recommended when there are uniqueness invariants):**

```csharp
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Concurrent_UniqueConstraint_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = {SpecClass}.Create();
        ApiClient.BindTo(spec);

        var context = spec.CreateTestingContext();
        context.Register(client);

        var createOp = spec.GetOperation<CreateThingRequest, CreateThingResponse>("CreateThing");
        var inputs = new InputSet
        {
            createOp
                .With(new CreateThingRequest("shared-name", 3), "User A")
                .WithoutPolling(),
            createOp
                .With(new CreateThingRequest("shared-name", 3), "User B — same name")
                .WithoutPolling(),
        };

        var cases = spec.GenerateConcurrentTests(new {StateType}(), inputs, new()
        {
            MaxDepth = 3,
            MaxConcurrencyLevel = 2,
            UnwindAllTerminatingStepFunctions = false,
        });

        await client.ResetAsync();
        var results = await spec.RunTests(
            context,
            new {StateType}(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(results, r =>
            Assert.True(r.Success,
                r.LastFailureMessage ?? $"Accordant concurrent test failed [{targetName}]"));
    }
```

**Rules:**
- Replace `{SpecClass}` and `{StateType}` with actual types
- The sequential test always includes at least one success case and one expected error case per operation
- Add lifecycle test only if the spec has Triggers
- Add concurrent test when there are uniqueness constraints worth testing
- Polling setup references the correct polling operation name
- `ConfigureDerivations` wires up the relationship from creating operation to polling operation
