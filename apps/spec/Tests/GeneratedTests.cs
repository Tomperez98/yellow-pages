using Microsoft.Accordant;
using Spec.Model;
using Xunit;

namespace Spec.Tests;

/// <summary>
/// Runs Accordant's generated test suites against the spec model,
/// exploring the full state space. Mirrors what the old
/// TimerCreateOnlyScenario, TimerLifecycleScenario, and TimerSlugRaceScenario
/// did — now callable from xUnit.
///
/// Each <c>[Theory]</c> runs against every target configured in
/// <see cref="TargetFixture"/>, so Accordant explores the state space
/// for in-memory, HTTP, and stdio backends in a single pass.
/// </summary>
public class GeneratedTests(TargetFixture fixture) : IClassFixture<TargetFixture>
{
    public static TheoryData<string> AllTargets => TargetNames.All();

    /// <summary>
    /// Mirrors TimerCreateOnlyScenario: creates valid timers and an
    /// empty-slug request, with async resolution disabled so we only
    /// test validation branches.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Sequential_CreateOnly_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = TimerSpec.Create();
        ApiClient.BindTo(spec);

        var context = spec.CreateTestingContext();
        context.Register(client);

        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");
        var inputs = new InputSet
        {
            create
                .With(
                    new CreateTimerRequest("tea", DateTime.UtcNow.AddHours(1)),
                    "Create tea timer"
                )
                .WithoutPolling(),
            create
                .With(
                    new CreateTimerRequest("coffee", DateTime.UtcNow.AddHours(1)),
                    "Create coffee timer"
                )
                .WithoutPolling(),
            create.With(new CreateTimerRequest("", DateTime.UtcNow.AddHours(1)), "Empty slug"),
        };

        var cases = spec.GenerateTests(new TimerState(), inputs, new() { MaxDepth = 3 });
        var results = await spec.RunTests(
            context,
            new TimerState(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(
            results,
            r =>
                Assert.True(
                    r.Success,
                    r.LastFailureMessage ?? $"Accordant sequential test failed [{targetName}]"
                )
        );
    }

    /// <summary>
    /// Mirrors TimerLifecycleScenario: creates a timer with a near-future
    /// deadline and lets the async step function transition it to Completed.
    /// Accordant handles the polling via ConfigureDerivations.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Sequential_Lifecycle_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = TimerSpec.Create();
        ApiClient.BindTo(spec);

        spec.ConfigureDerivations(
            "GetTimer",
            Derive
                .From<CreateTimerRequest, CreateTimerResponse, GetTimerRequest>("CreateTimer")
                .When((_, resp) => resp is CreateTimerResponse.Created)
                .As((req, resp) => new GetTimerRequest(((CreateTimerResponse.Created)resp).TimerId))
        );

        var context = spec.CreateTestingContext();
        context.Register(client);

        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");
        var inputs = new InputSet
        {
            create
                .With(
                    new CreateTimerRequest("lifecycle-timer", DateTime.UtcNow.AddSeconds(1)),
                    "Create near-future timer (completes async)"
                )
                .WithPolling(
                    new PollingSetup
                    {
                        Operation = "GetTimer",
                        WaitTimeInMs = 100,
                        MaxRetryCount = 100,
                    }
                ),
        };

        var cases = spec.GenerateTests(new TimerState(), inputs, new() { MaxDepth = 3 });
        var results = await spec.RunTests(
            context,
            new TimerState(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(
            results,
            r =>
                Assert.True(
                    r.Success,
                    r.LastFailureMessage ?? $"Accordant lifecycle test failed [{targetName}]"
                )
        );
    }

    /// <summary>
    /// Mirrors TimerSlugRaceScenario: two users concurrently create timers
    /// with the same slug. Accordant generates concurrent interleavings and
    /// the invariant (no duplicate slugs) catches any TOCTOU violation.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllTargets))]
    public async Task Concurrent_SlugRace_AllPass(string targetName)
    {
        var client = fixture.Clients[targetName];
        var spec = TimerSpec.Create();
        ApiClient.BindTo(spec);

        var context = spec.CreateTestingContext();
        context.Register(client);

        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");
        var inputs = new InputSet
        {
            create
                .With(
                    new CreateTimerRequest("shared-slug", DateTime.UtcNow.AddHours(1)),
                    "User A creates timer with shared slug"
                )
                .WithoutPolling(),
            create
                .With(
                    new CreateTimerRequest("shared-slug", DateTime.UtcNow.AddHours(1)),
                    "User B creates timer with same slug"
                )
                .WithoutPolling(),
        };

        var cases = spec.GenerateConcurrentTests(
            new TimerState(),
            inputs,
            new()
            {
                MaxDepth = 3,
                MaxConcurrencyLevel = 2,
                UnwindAllTerminatingStepFunctions = false,
            }
        );

        await client.ResetAsync();
        var results = await spec.RunTests(
            context,
            new TimerState(),
            cases,
            new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
        );

        Assert.All(
            results,
            r =>
                Assert.True(
                    r.Success,
                    r.LastFailureMessage ?? $"Accordant concurrent test failed [{targetName}]"
                )
        );
    }
}
