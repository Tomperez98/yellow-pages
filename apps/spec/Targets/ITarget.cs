using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spec.Targets;

public abstract record TargetResponse
{
    private TargetResponse() { }

    public sealed record Ok(HttpStatusCode Status, string Data) : TargetResponse
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
        };

        public T Deserialize<T>() => JsonSerializer.Deserialize<T>(Data, _jsonOptions)!;
    }

    public sealed record Err(HttpStatusCode Status, string Error) : TargetResponse;
}

public interface ITarget
{
    Task AsyncReset();
    Task<TargetResponse> AsyncSend<TRequest>(TRequest request);
}
