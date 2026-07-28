using System.Net;
using Spec.Model;
using Spec.Targets;

namespace spec;

public class ApiClient(ITarget target)
{
    public Task ResetAsync() => target.AsyncReset();

    public async Task<CreateCountryResponse> CreateCountryAsync(CreateCountryRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: HttpStatusCode.Created } ok =>
                new CreateCountryResponse.Created(ok.Deserialize<CreateOk>().CountryId),
            TargetResponse.Err { Status: HttpStatusCode.Conflict } =>
                new CreateCountryResponse.Conflict(),
            TargetResponse.Err { Status: HttpStatusCode.BadRequest } =>
                new CreateCountryResponse.BadRequest(),
            TargetResponse.Err { Status: HttpStatusCode.Forbidden } =>
                new CreateCountryResponse.Forbidden(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    public async Task<UpdateCountryResponse> UpdateCountryAsync(UpdateCountryRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: HttpStatusCode.OK } => new UpdateCountryResponse.Ok(),
            TargetResponse.Err { Status: HttpStatusCode.NotFound } =>
                new UpdateCountryResponse.NotFound(),
            TargetResponse.Err { Status: HttpStatusCode.Conflict } =>
                new UpdateCountryResponse.Conflict(),
            TargetResponse.Err { Status: HttpStatusCode.BadRequest } =>
                new UpdateCountryResponse.BadRequest(),
            TargetResponse.Err { Status: HttpStatusCode.Forbidden } =>
                new UpdateCountryResponse.Forbidden(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    public async Task<DeleteCountryResponse> DeleteCountryAsync(DeleteCountryRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: HttpStatusCode.OK } => new DeleteCountryResponse.Ok(),
            TargetResponse.Err { Status: HttpStatusCode.NotFound } =>
                new DeleteCountryResponse.NotFound(),
            TargetResponse.Err { Status: HttpStatusCode.Forbidden } =>
                new DeleteCountryResponse.Forbidden(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    private record CreateOk(Guid CountryId);
}
