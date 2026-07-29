using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using Spec.Model;

namespace Spec.Targets;

public class HttpTarget(HttpClient http, string jwtSecret) : ITarget
{
    public HttpTarget(string url, string jwtSecret)
        : this(new HttpClient { BaseAddress = new Uri(url) }, jwtSecret) { }

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

        var jwt = CreateJwt();
        var msg = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var resp = await http.SendAsync(msg);
        var respBody = await resp.Content.ReadAsStringAsync();
        var status = resp.StatusCode;

        if (resp.IsSuccessStatusCode)
            return new TargetResponse.Ok(status, respBody);

        return new TargetResponse.Err(status, resp.ReasonPhrase!);
    }

    private string CreateJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var token = new JwtSecurityToken(
            claims: [new("sub", "user-1"), new("role", "user")],
            expires: DateTimeOffset.FromUnixTimeSeconds(9999999999).UtcDateTime,
            signingCredentials: new(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
