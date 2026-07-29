using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// Slug uniqueness race: two users concurrently create timers with the
/// same slug. If both succeed, the system allowed a duplicate — a
/// linearizability violation caught by the invariant in ThenState.
///
/// Showcasing the lock in InMemoryServer:
///   dotnet run -- test --target inmemory --scenario timer-slug-race
///     → PASS (SemaphoreSlim serializes check-then-insert)
///   dotnet run -- test --target inmemory --scenario timer-slug-race --no-lock
///     → FAIL (TOCTOU: both threads pass the slug-exists check before
///       either inserts, so both succeed and the invariant fires)
/// </summary>
public class TimerSlugRaceScenario : IScenario
{
    private static readonly Claims UserA = new("user-a", "user");
    private static readonly Claims UserB = new("user-b", "user");
    private static readonly DateTime Deadline = DateTime.UtcNow.AddHours(1);

    public TestSuite BuildTests(Spec<TimerState> spec, TimerState initialState)
    {
        var create = spec.GetOperation<CreateTimerRequest, CreateTimerResponse>("CreateTimer");

        var inputs = new InputSet
        {
            create.With(
                new CreateTimerRequest(UserA, "shared-slug", Deadline),
                "User A creates timer with shared slug"
            ),
            create.With(
                new CreateTimerRequest(UserB, "shared-slug", Deadline),
                "User B creates timer with same slug"
            ),
        };

        return new TestSuite.Concurrent(
            spec.GenerateConcurrentTests(
                initialState,
                inputs,
                new() { MaxDepth = 3, MaxConcurrencyLevel = 2 }
            )
        );
    }
}
