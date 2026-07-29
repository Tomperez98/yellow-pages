using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// Full lifecycle: create a timer with a near-future deadline and let
/// the async step function transition it to Completed.
/// </summary>
public class TimerLifecycleScenario : IScenario
{
    private static readonly Claims User = new("user-1", "user");
    private static readonly DateTime NearFuture = DateTime.UtcNow.AddSeconds(5);

    public TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState)
    {
        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");

        // This is the only scenario that polls: we need to observe the
        // async Active → Completed transition, so we configure a derivation
        // (CreateTimer response → GetTimer request) and tell the framework
        // to poll GetTimer until isTerminal returns true.
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
