using Microsoft.Accordant;
using Spec;
using Spec.Model;

namespace Spec.Transitions;

/// <summary>
/// Full lifecycle: create a timer, then poll until the async deadline
/// monitor transitions it from Active to Completed.
/// </summary>
public class TimerLifecycleTransition : ITransition
{
    private const int PollIntervalMs = 50;
    private const int MaxRetries = 200; // 10 s total, generous for CI

    public async Task RunAsync(Spec<TimerState> spec, ApiClient client, TimerState initialState)
    {
        var createOp = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");
        var getOp = spec.GetOperation<GetTimerRequest, GetTimerResponse>("GetTimer");

        var stateProfile = new StateProfile(initialState);

        // --- Step 1: Create a timer ---
        var deadline = DateTime.UtcNow.AddSeconds(1);
        var createReq = new CreateTimerRequest("lunch-break", deadline);
        var createResp = await client.CreateTimerAsync(createReq);

        var (isValid, message, next) = spec.Allows(createOp, createReq, createResp, stateProfile);
        Invariant.Assert(isValid, message);
        stateProfile = next;

        Invariant.Assert(
            createResp is CreateTimerResponse.Created,
            $"Expected Created, got {createResp.GetType().Name}"
        );
        var id = ((CreateTimerResponse.Created)createResp).TimerId;

        // --- Step 2: Poll GetTimer until the deadline monitor transitions to Completed ---
        var getReq = new GetTimerRequest(id);
        GetTimerResponse pollResp = null!;
        for (var i = 0; i < MaxRetries; i++)
        {
            await Task.Delay(PollIntervalMs);
            pollResp = await client.GetTimerAsync(getReq);

            if (pollResp is GetTimerResponse.Ok { Status: TimerStatus.Completed })
                break;
        }

        (isValid, message, next) = spec.Allows(getOp, getReq, pollResp, stateProfile);
        Invariant.Assert(isValid, message);

        Invariant.Assert(
            pollResp is GetTimerResponse.Ok { Status: TimerStatus.Completed },
            "Expected Completed status"
        );
    }
}
