using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// Create-only: exercises validation branches (Forbidden, BadRequest,
/// Conflict) plus successful creation with the async deadline trigger.
/// </summary>
public class TimerCreateOnlyScenario : IScenario
{
    private static readonly Claims User = new("user-1", "user");
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);

    public TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState)
    {
        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");

        var inputs = new InputSet
        {
            create.With(new CreateTimerRequest(User, "tea", Deadline), "Create tea timer"),
            create.With(new CreateTimerRequest(User, "coffee", Deadline), "Create coffee timer"),
            create.With(new CreateTimerRequest(User, "", Deadline), "Empty slug"),
        };

        return new TestSuite.Sequential(
            spec.GenerateTests(initialState, inputs, new() { MaxDepth = 3 })
        );
    }
}
