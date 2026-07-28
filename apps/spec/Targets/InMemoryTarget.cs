using System.Net;
using System.Text.Json;
using Spec.Model;

namespace Spec.Targets;

public class InMemoryTarget(InMemoryServer server) : ITarget
{
    public Task AsyncReset()
    {
        server.Reset();
        return Task.CompletedTask;
    }

    public Task<TargetResponse> AsyncSend<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = request switch
        {
            CreateCountryRequest r => ToResult(server.CreateCountry(r)),
            UpdateCountryRequest r => ToResult(server.UpdateCountry(r)),
            DeleteCountryRequest r => ToResult(server.DeleteCountry(r)),
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}"),
        };

        return Task.FromResult(response);
    }

    private static TargetResponse ToResult(object resp) =>
        resp switch
        {
            CreateCountryResponse.Created => Ok(HttpStatusCode.Created, resp),
            CreateCountryResponse.Conflict => Err(HttpStatusCode.Conflict),
            CreateCountryResponse.BadRequest => Err(HttpStatusCode.BadRequest),
            CreateCountryResponse.Forbidden => Err(HttpStatusCode.Forbidden),

            UpdateCountryResponse.Ok => Ok(HttpStatusCode.OK, resp),
            UpdateCountryResponse.NotFound => Err(HttpStatusCode.NotFound),
            UpdateCountryResponse.Conflict => Err(HttpStatusCode.Conflict),
            UpdateCountryResponse.BadRequest => Err(HttpStatusCode.BadRequest),
            UpdateCountryResponse.Forbidden => Err(HttpStatusCode.Forbidden),

            DeleteCountryResponse.Ok => Ok(HttpStatusCode.OK, resp),
            DeleteCountryResponse.NotFound => Err(HttpStatusCode.NotFound),
            DeleteCountryResponse.Forbidden => Err(HttpStatusCode.Forbidden),

            _ => throw new ArgumentException($"Unknown response type: {resp.GetType().Name}"),
        };

    private static TargetResponse.Ok Ok(HttpStatusCode status, object data) =>
        new(status, JsonSerializer.Serialize(data, data.GetType()));

    private static TargetResponse.Err Err(HttpStatusCode status) => new(status, status.ToString());
}

public class InMemoryServer(YellowPagesState initialState)
{
    private readonly YellowPagesState _initial = Clone(initialState);
    private YellowPagesState _state = Clone(initialState);

    public void Reset()
    {
        _state = Clone(_initial);
    }

    public CreateCountryResponse CreateCountry(CreateCountryRequest req)
    {
        if (req.Claims.Role != "admin")
            return new CreateCountryResponse.Forbidden();

        if (string.IsNullOrWhiteSpace(req.Code))
            return new CreateCountryResponse.BadRequest();

        if (_state.Countries.Any(c => c.Code == req.Code))
            return new CreateCountryResponse.Conflict();

        var id = Guid.CreateVersion7();
        _state.Countries.Add(new Country { Id = id, Code = req.Code });
        return new CreateCountryResponse.Created(id);
    }

    public UpdateCountryResponse UpdateCountry(UpdateCountryRequest req)
    {
        if (req.Claims.Role != "admin")
            return new UpdateCountryResponse.Forbidden();

        if (string.IsNullOrWhiteSpace(req.Code))
            return new UpdateCountryResponse.BadRequest();

        var country = _state.Countries.FirstOrDefault(c => c.Id == req.CountryId);
        if (country is null)
            return new UpdateCountryResponse.NotFound();

        if (_state.Countries.Any(c => c.Id != req.CountryId && c.Code == req.Code))
            return new UpdateCountryResponse.Conflict();

        country.Code = req.Code;
        return new UpdateCountryResponse.Ok();
    }

    public DeleteCountryResponse DeleteCountry(DeleteCountryRequest req)
    {
        if (req.Claims.Role != "admin")
            return new DeleteCountryResponse.Forbidden();

        var country = _state.Countries.FirstOrDefault(c => c.Id == req.CountryId);
        if (country is null)
            return new DeleteCountryResponse.NotFound();

        _state.Countries.RemoveAll(c => c.Id == req.CountryId);
        return new DeleteCountryResponse.Ok();
    }

    private static YellowPagesState Clone(YellowPagesState s) =>
        new()
        {
            Countries = s.Countries.Select(c => new Country { Id = c.Id, Code = c.Code }).ToList(),
        };
}
