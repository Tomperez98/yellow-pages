using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// Create-only: exercises validation branches (Forbidden, BadRequest,
/// Conflict) plus successful creation.
/// </summary>
public class TimerCreateOnlyScenario : IScenario
{
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);

    public TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState)
    {
        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");

        // .WithoutPolling() tells the framework: "don't resolve the async
        // step function during test execution." We don't need to observe
        // timers reaching Completed here — this scenario only tests that
        // validation branches return the right responses.
        // .WithoutPolling() tells the framework: skip resolving the async
        // step function during test execution. This scenario only checks that
        // validation branches return the right responses — it doesn't need
        // to wait for timers to reach Completed.
        var inputs = new InputSet
        {
            create
                .With(new CreateTimerRequest("tea", Deadline), "Create tea timer")
                .WithoutPolling(),
            create
                .With(new CreateTimerRequest("coffee", Deadline), "Create coffee timer")
                .WithoutPolling(),
            create.With(new CreateTimerRequest("", Deadline), "Empty slug"),
        };

        return new TestSuite.Sequential(
            spec.GenerateTests(initialState, inputs, new() { MaxDepth = 3 })
        );
    }
}
