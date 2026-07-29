using System.Net;
using Microsoft.Accordant;
using Spec.Model;
using Spec.Targets;

namespace spec;

public class ApiClient(ITarget target)
{
    public static void BindTo(Spec<TimerState> spec)
    {
        spec.ExecuteWith<ApiClient>()
            .BindAsync<CreateTimerRequest, CreateTimerResponse>(
                "CreateTimer",
                (c, req) => c.CreateTimerAsync(req)
            )
            .BindAsync<GetTimerRequest, GetTimerResponse>(
                "GetTimer",
                (c, req) => c.GetTimerAsync(req)
            );
    }

    public Task ResetAsync() => target.AsyncReset();

    public async Task<CreateTimerResponse> CreateTimerAsync(CreateTimerRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: HttpStatusCode.Created } ok =>
                new CreateTimerResponse.Created(ok.Deserialize<CreateOk>().TimerId),
            TargetResponse.Err { Status: HttpStatusCode.Conflict } =>
                new CreateTimerResponse.Conflict(),
            TargetResponse.Err { Status: HttpStatusCode.BadRequest } =>
                new CreateTimerResponse.BadRequest(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    public async Task<GetTimerResponse> GetTimerAsync(GetTimerRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok ok => new GetTimerResponse.Ok(ok.Deserialize<GetOk>().Status),
            TargetResponse.Err { Status: HttpStatusCode.NotFound } =>
                new GetTimerResponse.NotFound(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    private record CreateOk(Guid TimerId);

    private record GetOk(TimerStatus Status);
}
