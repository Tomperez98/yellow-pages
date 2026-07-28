using System.Net;
using System.Text.Json;

namespace Spec.Targets;

public abstract record TargetResponse
{
    private TargetResponse() { }

    public sealed record Ok(HttpStatusCode Status, string Data) : TargetResponse
    {
        public T Deserialize<T>() => JsonSerializer.Deserialize<T>(Data)!;
    }

    public sealed record Err(HttpStatusCode Status, string Error) : TargetResponse;
}

public interface ITarget
{
    Task AsyncReset();
    Task<TargetResponse> AsyncSend<TRequest>(TRequest request);
}
