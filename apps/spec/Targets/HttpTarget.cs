using System.Text;
using System.Text.Json;

namespace Spec.Targets;

public class HttpTarget : ITarget
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HttpTarget(string baseUrl) => _http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    public HttpTarget(HttpClient http) => _http = http;

    public Task AsyncReset() => Task.CompletedTask;

    public async Task<string> AsyncSend<TRequest>(string name, TRequest request)
    {
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(name, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
