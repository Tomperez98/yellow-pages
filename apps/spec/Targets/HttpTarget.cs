using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Spec.Model;

namespace Spec.Targets;

public class HttpTarget(HttpClient http) : ITarget
{
    public HttpTarget(string url)
        : this(new HttpClient { BaseAddress = new Uri(url) }) { }

    public async Task AsyncReset()
    {
        await http.PostAsync("/rpc/reset", null);
    }

    public async Task<TargetResponse> AsyncSend<TRequest>(TRequest request)
    {
        var (path, body) = request switch
        {
            CreateTimerRequest r => (
                "/rpc/create_timer",
                new JsonObject { ["slug"] = r.Slug, ["deadline"] = r.Deadline.ToString("o") }
            ),
            GetTimerRequest r => (
                "/rpc/get_timer",
                new JsonObject { ["id"] = r.TimerId.ToString() }
            ),
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}"),
        };

        var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };

        var resp = await http.SendAsync(msg);
        var respBody = await resp.Content.ReadAsStringAsync();
        var status = resp.StatusCode;

        if (resp.IsSuccessStatusCode)
            return new TargetResponse.Ok(status, respBody);

        return new TargetResponse.Err(status, resp.ReasonPhrase!);
    }


}
