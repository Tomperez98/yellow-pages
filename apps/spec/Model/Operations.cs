namespace Spec.Model;

public record Claims(string Sub, string Role);

// --- CreateTimer ---

public record CreateTimerRequest(Claims Claims, string Slug, DateTime Deadline);

public abstract record CreateTimerResponse
{
    private CreateTimerResponse() { }

    public sealed record Created(Guid TimerId) : CreateTimerResponse;

    public sealed record Conflict : CreateTimerResponse;

    public sealed record BadRequest : CreateTimerResponse;

    public sealed record Forbidden : CreateTimerResponse;
}

// --- GetTimer (polling operation) ---

public record GetTimerRequest(Claims Claims, Guid TimerId);

public abstract record GetTimerResponse
{
    private GetTimerResponse() { }

    public sealed record Ok(TimerStatus Status) : GetTimerResponse;

    public sealed record NotFound : GetTimerResponse;

    public sealed record Forbidden : GetTimerResponse;
}
