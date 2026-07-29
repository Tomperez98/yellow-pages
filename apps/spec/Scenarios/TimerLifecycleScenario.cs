using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// Full lifecycle: create timers with future and past deadlines,
/// exercise slug conflict, and let the async step function complete
/// the past-deadline timer.
/// </summary>
public class TimerLifecycleScenario : IScenario
{
    private static readonly Claims User = new("user-1", "user");
    private static readonly DateTime NearFuture = DateTime.UtcNow.AddSeconds(5);

    public TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState)
    {
        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");

        spec.ConfigureDerivations(
            "GetTimer",
            Derive
                .From<CreateTimerRequest, CreateTimerResponse, GetTimerRequest>("CreateTimer")
                .When((_, resp) => resp is CreateTimerResponse.Created)
                .As(
                    (req, resp) =>
                        new GetTimerRequest(req.Claims, ((CreateTimerResponse.Created)resp).TimerId)
                )
        );

        var inputs = new InputSet
        {
            create
                .With(
                    new CreateTimerRequest(User, "5s-timer", NearFuture),
                    "Create near-future timer (completes async in ~5s)"
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

        return new TestSuite.Sequential(
            spec.GenerateTests(initialState, inputs, new() { MaxDepth = 3 })
        );
    }
}
