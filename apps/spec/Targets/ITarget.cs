using System.Text.Json;

namespace Spec.Targets;

public abstract record TargetResponse
{
    private TargetResponse() { }

    public sealed record Ok(int Status, string Data) : TargetResponse
    {
        public T Deserialize<T>() => JsonSerializer.Deserialize<T>(Data)!;
    }

    public sealed record Err(int Status, string Error) : TargetResponse;
}

public interface ITarget
{
    Task AsyncReset();
    Task<TargetResponse> AsyncSend<TRequest>(TRequest request);
}
