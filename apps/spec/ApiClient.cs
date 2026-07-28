using System.Text.Json;
using Spec.Model;
using Spec.Targets;

namespace spec;

public class ApiClient(ITarget target)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CreateCountryResponse> CreateCountryAsync(
        string name,
        CreateCountryRequest request
    )
    {
        var responseJson = await target.AsyncSend(name, request);
        return JsonSerializer.Deserialize<CreateCountryResponse>(responseJson, JsonOptions)!;
    }

    public async Task<UpdateCountryResponse> UpdateCountryAsync(
        string name,
        UpdateCountryRequest request
    )
    {
        var responseJson = await target.AsyncSend(name, request);
        return JsonSerializer.Deserialize<UpdateCountryResponse>(responseJson, JsonOptions)!;
    }

    public async Task<DeleteCountryResponse> DeleteCountryAsync(
        string name,
        DeleteCountryRequest request
    )
    {
        var responseJson = await target.AsyncSend(name, request);
        return JsonSerializer.Deserialize<DeleteCountryResponse>(responseJson, JsonOptions)!;
    }
}
