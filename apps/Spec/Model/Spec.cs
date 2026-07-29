using Microsoft.Accordant;

namespace Spec.Model;

public static class TimerSpec
{
    public static Spec<TimerState> Create()
    {
        var spec = new Spec<TimerState>().WithJsonPrinters();

        // --- CreateTimer ---
        //
        // Creates a timer in Active state, then triggers background deadline
        // monitoring. The async step function models the autonomous transition
        // Active → Completed when the deadline is reached — no client API call
        // triggers this.
        //
        // Key invariants:
        //   - Slug is user-defined and must be unique across all timers
        //   - The async step function has exactly one deterministic transition

        spec.Operation<CreateTimerRequest, CreateTimerResponse>(
            "CreateTimer",
            (req, state) =>
            {
                if (string.IsNullOrWhiteSpace(req.Slug))
                    return Expect
                        .That<CreateTimerResponse>(
                            r => r is CreateTimerResponse.BadRequest,
                            "slug cannot be empty"
                        )
                        .SameState();

                if (state.Items.Any(t => t.Slug == req.Slug))
                    return Expect
                        .That<CreateTimerResponse>(
                            r => r is CreateTimerResponse.Conflict,
                            "a timer with this slug already exists"
                        )
                        .SameState();

                return Expect
                    .That<CreateTimerResponse>(
                        r =>
                            r is CreateTimerResponse.Created { TimerId: var id }
                            && id != Guid.Empty,
                        "successful creation returns Created with a valid TimerId"
                    )
                    .ThenState<TimerState>(
                        (resp, s) =>
                        {
                            var id = ((CreateTimerResponse.Created)resp).TimerId;
                            s.Items.Add(
                                new TimerItem
                                {
                                    Id = id,
                                    Slug = req.Slug,
                                    Deadline = req.Deadline,
                                    Status = TimerStatus.Active,
                                }
                            );
                            Invariant.Assert(
                                s.Items.Select(t => t.Slug).Distinct().Count() == s.Items.Count,
                                "duplicate slugs"
                            );
                            Invariant.Assert(
                                s.Items.All(t => t.Id.Version == 7),
                                "all timer IDs must be version 7"
                            );
                        },
                        mock: () => new CreateTimerResponse.Created(Guid.CreateVersion7())
                    )
                    .Triggers(
                        AsyncOperation.Create<TimerState>(
                            isTerminal: s =>
                            {
                                var timer = s.Items.First(t => t.Slug == req.Slug);
                                return timer.Status == TimerStatus.Completed;
                            },
                            transitions:
                            [
                                next =>
                                {
                                    var timer = next.Items.First(t => t.Slug == req.Slug);
                                    timer.Status = TimerStatus.Completed;
                                },
                            ]
                        )
                    );
            }
        );

        // --- GetTimer ---
        //
        // Read-only operation the framework polls to observe async transitions.

        spec.Operation<GetTimerRequest, GetTimerResponse>(
            "GetTimer",
            (req, state) =>
            {
                var timer = state.Items.FirstOrDefault(t => t.Id == req.TimerId);
                if (timer is null)
                    return Expect
                        .That<GetTimerResponse>(
                            r => r is GetTimerResponse.NotFound,
                            "timer with the given ID does not exist"
                        )
                        .SameState();

                return Expect
                    .That<GetTimerResponse>(
                        r => r is GetTimerResponse.Ok { Status: var s } && s == timer.Status,
                        $"returns current status {timer.Status}"
                    )
                    .SameState();
            }
        );

        return spec;
    }
}
