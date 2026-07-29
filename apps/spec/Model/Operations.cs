namespace Spec.Model;

// --- CreateTimer ---

public record CreateTimerRequest(string Slug, DateTime Deadline);

public abstract record CreateTimerResponse
{
    private CreateTimerResponse() { }

    public sealed record Created(Guid TimerId) : CreateTimerResponse;

    public sealed record Conflict : CreateTimerResponse;

    public sealed record BadRequest : CreateTimerResponse;
}

// --- GetTimer (polling operation) ---

public record GetTimerRequest(Guid TimerId);

public abstract record GetTimerResponse
{
    private GetTimerResponse() { }

    public sealed record Ok(TimerStatus Status) : GetTimerResponse;

    public sealed record NotFound : GetTimerResponse;
}
