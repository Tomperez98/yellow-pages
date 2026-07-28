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
            TargetResponse.Ok { Status: 200 } ok => new CreateCountryResponse.Ok(
                ok.Deserialize<CreateOk>().CountryId
            ),
            TargetResponse.Err { Status: 409 } => new CreateCountryResponse.Conflict(),
            TargetResponse.Err { Status: 400 } => new CreateCountryResponse.InvalidData(),
            TargetResponse.Err { Status: 403 } => new CreateCountryResponse.NotAuthorized(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    public async Task<UpdateCountryResponse> UpdateCountryAsync(UpdateCountryRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: 200 } => new UpdateCountryResponse.Ok(),
            TargetResponse.Err { Status: 404 } => new UpdateCountryResponse.NotFound(),
            TargetResponse.Err { Status: 409 } => new UpdateCountryResponse.Conflict(),
            TargetResponse.Err { Status: 400 } => new UpdateCountryResponse.ValidationFailed(),
            TargetResponse.Err { Status: 403 } => new UpdateCountryResponse.NotAuthorized(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    public async Task<DeleteCountryResponse> DeleteCountryAsync(DeleteCountryRequest request)
    {
        var r = await target.AsyncSend(request);
        return r switch
        {
            TargetResponse.Ok { Status: 200 } => new DeleteCountryResponse.Ok(),
            TargetResponse.Err { Status: 404 } => new DeleteCountryResponse.NotFound(),
            TargetResponse.Err { Status: 403 } => new DeleteCountryResponse.NotAuthorized(),
            _ => throw new InvalidOperationException($"Unexpected response: {r}"),
        };
    }

    private record CreateOk(Guid CountryId);
}
