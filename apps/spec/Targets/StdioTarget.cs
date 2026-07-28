using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Spec.Model;

namespace Spec.Targets;

public class StdioTarget : ITarget
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly Lock _lock = new();

    public StdioTarget(string executablePath)
    {
        if (!Path.IsPathRooted(executablePath))
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            executablePath = Path.GetFullPath(Path.Combine(baseDir, executablePath));
        }

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = Process.Start(psi)!;
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
    }

    public Task AsyncReset()
    {
        Send(new { type = "reset" });
        return Task.CompletedTask;
    }

    public Task<TargetResponse> AsyncSend<TRequest>(TRequest request)
    {
        var reqObj = SerializeRequest(request);
        var (code, data, error) = Send(reqObj);
        var status = (HttpStatusCode)code;

        if ((int)status >= 200 && (int)status < 300 || status == HttpStatusCode.Created)
            return Task.FromResult<TargetResponse>(new TargetResponse.Ok(status, data ?? "{}"));

        return Task.FromResult<TargetResponse>(
            new TargetResponse.Err(status, error ?? status.ToString())
        );
    }

    private (int Status, string? Data, string? Error) Send(object request)
    {
        var json = JsonSerializer.Serialize(request);
        lock (_lock)
        {
            _stdin.WriteLine(json);
            var line =
                _stdout.ReadLine()
                ?? throw new InvalidOperationException("Stdio process exited unexpectedly");
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetInt32();
            var data = root.TryGetProperty("result", out var d) ? d.GetRawText() : null;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (status, data, error);
        }
    }

    private static object SerializeRequest<TRequest>(TRequest request) =>
        request switch
        {
            CreateCountryRequest r => new
            {
                type = "create_country",
                payload = new { code = r.Code, claims = SerializeClaims(r.Claims) },
            },
            UpdateCountryRequest r => new
            {
                type = "update_country",
                payload = new
                {
                    id = r.CountryId.ToString(),
                    code = r.Code,
                    claims = SerializeClaims(r.Claims),
                },
            },
            DeleteCountryRequest r => new
            {
                type = "delete_country",
                payload = new { id = r.CountryId.ToString(), claims = SerializeClaims(r.Claims) },
            },
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}"),
        };

    private static object SerializeClaims(Claims c) =>
        new
        {
            sub = c.Sub,
            role = c.Role,
            org_id = c.OrgId,
            org_role = c.OrgRole,
            orgs = c.Orgs.Select(o => new { OrgId = o.OrgId, Role = o.Role }),
        };
}
