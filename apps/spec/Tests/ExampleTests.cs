using Microsoft.Accordant;
using Spec.Model;
using Spec.Targets;
using Xunit;

namespace Spec.Tests;

/// <summary>
/// Hand-written example tests: CRUD, lifecycle, concurrency, race
/// condition reproduction, and spec-validated patterns. Each
/// <c>[Theory]</c> runs against every target configured in
/// <see cref="TargetFixture"/>, so a single <c>dotnet test</c>
/// covers in-memory, HTTP, and stdio backends.
/// </summary>
public class ExampleCrudTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);

    public static TheoryData<string> AllTargets => Spec.Tests.TargetNames.All();

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task CreateTimer_ValidSlug_ReturnsCreated(string targetName)
    {
        var client = fixture.Clients[targetName];
        var resp = await client.CreateTimerAsync(new CreateTimerRequest("tea", Deadline));

        var created = Assert.IsType<CreateTimerResponse.Created>(resp);
        Assert.NotEqual(Guid.Empty, created.TimerId);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task CreateTimer_EmptySlug_ReturnsBadRequest(string targetName)
    {
        var client = fixture.Clients[targetName];
        var resp = await client.CreateTimerAsync(new CreateTimerRequest("", Deadline));

        Assert.IsType<CreateTimerResponse.BadRequest>(resp);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task CreateTimer_WhitespaceSlug_ReturnsBadRequest(string targetName)
    {
        var client = fixture.Clients[targetName];
        var resp = await client.CreateTimerAsync(new CreateTimerRequest("   ", Deadline));

        Assert.IsType<CreateTimerResponse.BadRequest>(resp);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task CreateTimer_DuplicateSlug_ReturnsConflict(string targetName)
    {
        var client = fixture.Clients[targetName];
        await client.CreateTimerAsync(new CreateTimerRequest("unique-slug", Deadline));

        var resp = await client.CreateTimerAsync(new CreateTimerRequest("unique-slug", Deadline));

        Assert.IsType<CreateTimerResponse.Conflict>(resp);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task GetTimer_ExistingTimer_ReturnsStatus(string targetName)
    {
        var client = fixture.Clients[targetName];
        var created = await client.CreateTimerAsync(new CreateTimerRequest("get-me", Deadline));
        var id = ((CreateTimerResponse.Created)created).TimerId;

        var resp = await client.GetTimerAsync(new GetTimerRequest(id));

        var ok = Assert.IsType<GetTimerResponse.Ok>(resp);
        Assert.Equal(TimerStatus.Active, ok.Status);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task GetTimer_NonexistentId_ReturnsNotFound(string targetName)
    {
        var client = fixture.Clients[targetName];
        var resp = await client.GetTimerAsync(new GetTimerRequest(Guid.CreateVersion7()));

        Assert.IsType<GetTimerResponse.NotFound>(resp);
    }
}

public class ExampleLifecycleTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => Spec.Tests.TargetNames.All();

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Timer_AutoCompletes_WhenDeadlineReached(string targetName)
    {
        var client = fixture.Clients[targetName];
        var created = await client.CreateTimerAsync(
            new CreateTimerRequest("quick-timer", DateTime.UtcNow.AddSeconds(1))
        );
        var id = ((CreateTimerResponse.Created)created).TimerId;

        for (var i = 0; i < 200; i++)
        {
            await Task.Delay(50);
            var resp = await client.GetTimerAsync(new GetTimerRequest(id));
            if (resp is GetTimerResponse.Ok { Status: TimerStatus.Completed })
                return;
        }

        Assert.Fail($"Timer did not reach Completed status within 10 seconds [{targetName}]");
    }
}

public class ExampleConcurrencyTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);

    public static TheoryData<string> AllTargets => Spec.Tests.TargetNames.All();

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task ConcurrentCreate_SameSlug_AtMostOneSucceeds(string targetName)
    {
        var client = fixture.Clients[targetName];
        var slug = $"slug-race-{Guid.NewGuid():N}";
        var taskA = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
        var taskB = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
        var results = await Task.WhenAll(taskA, taskB);

        var createdCount = results.Count(r => r is CreateTimerResponse.Created);
        Assert.True(
            createdCount <= 1,
            $"Expected at most 1 Created, got {createdCount} — TOCTOU race condition [{targetName}]"
        );
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task ConcurrentCreate_DifferentSlugs_BothSucceed(string targetName)
    {
        var client = fixture.Clients[targetName];
        var taskA = client.CreateTimerAsync(
            new CreateTimerRequest($"slug-a-{Guid.NewGuid():N}", Deadline)
        );
        var taskB = client.CreateTimerAsync(
            new CreateTimerRequest($"slug-b-{Guid.NewGuid():N}", Deadline)
        );
        var results = await Task.WhenAll(taskA, taskB);

        Assert.All(results, r => Assert.IsType<CreateTimerResponse.Created>(r));
    }
}

/// <summary>
/// Demonstrates the TOCTOU race condition directly against InMemoryTarget.
/// With the SemaphoreSlim lock, check-then-insert is atomic. Without it,
/// both threads pass the slug-exists check before either inserts.
///
/// These tests are intentionally NOT parameterized — they test specific
/// lock configurations that only apply to the in-memory target.
///
///   dotnet test --filter "FullyQualifiedName~WithLock"
///   dotnet test --filter "FullyQualifiedName~WithoutLock"
/// </summary>
public class ExampleRaceConditionTests
{
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);
    private const int Attempts = 20;

    [Fact]
    public async Task WithLock_ConcurrentSameSlug_AtMostOneSucceeds()
    {
        using var server = new InMemoryServer(new TimerState(), threadSafe: true);
        var target = new InMemoryTarget(server);
        var client = new ApiClient(target);

        for (var i = 0; i < Attempts; i++)
        {
            var slug = $"race-{Guid.NewGuid():N}";
            var taskA = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
            var taskB = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
            var results = await Task.WhenAll(taskA, taskB);

            var createdCount = results.Count(r => r is CreateTimerResponse.Created);
            Assert.True(
                createdCount <= 1,
                $"Lock failed: both creates succeeded for slug '{slug}'"
            );
        }
    }

    [Fact]
    public async Task WithoutLock_ConcurrentSameSlug_RaceConditionTriggers()
    {
        using var server = new InMemoryServer(new TimerState(), threadSafe: false);
        var target = new InMemoryTarget(server);
        var client = new ApiClient(target);

        var raceHits = 0;
        for (var i = 0; i < Attempts; i++)
        {
            var slug = $"race-{Guid.NewGuid():N}";
            var taskA = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
            var taskB = client.CreateTimerAsync(new CreateTimerRequest(slug, Deadline));
            var results = await Task.WhenAll(taskA, taskB);

            if (results.All(r => r is CreateTimerResponse.Created))
                raceHits++;
        }

        Assert.True(
            raceHits > 0,
            $"TOCTOU race did not trigger in {Attempts} attempts — "
                + "the async gap may be too narrow on this machine"
        );
    }
}

/// <summary>
/// Spec-validated example: every response is checked against the Accordant
/// model via spec.Allows(). Write the sequence, capture typed responses,
/// and the model confirms each one is valid. If a response isn't permitted,
/// the test fails with a conformance error.
/// </summary>
public class ExampleSpecValidatedTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    private const int PollIntervalMs = 50;
    private const int MaxRetries = 200;

    public static TheoryData<string> AllTargets => Spec.Tests.TargetNames.All();

    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Lifecycle_ValidatesAllResponsesAgainstSpec(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = TimerSpec.Create();
        ApiClient.BindTo(spec);

        var createOp = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");
        var getOp = spec.GetOperation<GetTimerRequest, GetTimerResponse>("GetTimer");

        await client.ResetAsync();
        var stateProfile = new StateProfile(new TimerState());

        // Step 1: Create a timer
        var deadline = DateTime.UtcNow.AddSeconds(1);
        var createReq = new CreateTimerRequest("lunch-break", deadline);
        var createResp = await client.CreateTimerAsync(createReq);

        var (isValid, message, next) = spec.Allows(createOp, createReq, createResp, stateProfile);
        Assert.True(isValid, message);
        stateProfile = next;

        var created = Assert.IsType<CreateTimerResponse.Created>(createResp);
        Assert.NotEqual(Guid.Empty, created.TimerId);

        // Step 2: Poll GetTimer until deadline monitor transitions to Completed
        var getReq = new GetTimerRequest(created.TimerId);
        GetTimerResponse pollResp = null!;
        for (var i = 0; i < MaxRetries; i++)
        {
            await Task.Delay(PollIntervalMs);
            pollResp = await client.GetTimerAsync(getReq);
            if (pollResp is GetTimerResponse.Ok { Status: TimerStatus.Completed })
                break;
        }

        (isValid, message, next) = spec.Allows(getOp, getReq, pollResp, stateProfile);
        Assert.True(isValid, message);

        var ok = Assert.IsType<GetTimerResponse.Ok>(pollResp);
        Assert.Equal(TimerStatus.Completed, ok.Status);
    }
}
