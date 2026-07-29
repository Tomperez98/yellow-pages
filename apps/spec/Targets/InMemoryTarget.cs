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

    public async Task<TargetResponse> AsyncSend<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = request switch
        {
            CreateTimerRequest r => ToResult(await server.CreateTimerAsync(r)),
            GetTimerRequest r => ToResult(await server.GetTimerAsync(r)),
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}"),
        };

        return response;
    }

    private static TargetResponse ToResult(object resp) =>
        resp switch
        {
            CreateTimerResponse.Created => Ok(HttpStatusCode.Created, resp),
            CreateTimerResponse.Conflict => Err(HttpStatusCode.Conflict),
            CreateTimerResponse.BadRequest => Err(HttpStatusCode.BadRequest),
            CreateTimerResponse.Forbidden => Err(HttpStatusCode.Forbidden),

            GetTimerResponse.Ok => Ok(HttpStatusCode.OK, resp),
            GetTimerResponse.NotFound => Err(HttpStatusCode.NotFound),
            GetTimerResponse.Forbidden => Err(HttpStatusCode.Forbidden),

            _ => throw new ArgumentException($"Unknown response type: {resp.GetType().Name}"),
        };

    private static TargetResponse.Ok Ok(HttpStatusCode status, object data) =>
        new(status, JsonSerializer.Serialize(data, data.GetType()));

    private static TargetResponse.Err Err(HttpStatusCode status) => new(status, status.ToString());
}

public class InMemoryServer : IDisposable
{
    private readonly TimerState _initial;
    private TimerState _state;
    private readonly bool _threadSafe;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _deadlineCts = new();

    // ponytail: single background loop, per-item timers if throughput matters
    public InMemoryServer(
        TimerState initialState,
        bool threadSafe = true,
        int deadlineCheckMs = 500
    )
    {
        _initial = Clone(initialState);
        _state = Clone(initialState);
        _threadSafe = threadSafe;
        _ = RunDeadlineMonitor(deadlineCheckMs, _deadlineCts.Token);
    }

    private async Task RunDeadlineMonitor(int intervalMs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(intervalMs, ct);
            if (_threadSafe)
                await _lock.WaitAsync(ct);
            try
            {
                foreach (
                    var item in _state.Items.Where(t =>
                        t.Status == TimerStatus.Active && t.Deadline < DateTime.UtcNow
                    )
                )
                    item.Status = TimerStatus.Completed;
            }
            finally
            {
                if (_threadSafe)
                    _lock.Release();
            }
        }
    }

    public void Reset()
    {
        _state = Clone(_initial);
    }

    public async Task<CreateTimerResponse> CreateTimerAsync(CreateTimerRequest req)
    {
        if (req.Claims.Role != "user")
            return new CreateTimerResponse.Forbidden();

        if (string.IsNullOrWhiteSpace(req.Slug))
            return new CreateTimerResponse.BadRequest();

        if (_threadSafe)
            await _lock.WaitAsync();
        try
        {
            if (_state.Items.Any(t => t.Slug == req.Slug))
                return new CreateTimerResponse.Conflict();

            await Task.Yield(); // simulate async gap

            var id = Guid.CreateVersion7();
            _state.Items.Add(
                new TimerItem
                {
                    Id = id,
                    Slug = req.Slug,
                    Deadline = req.Deadline,
                    Status = TimerStatus.Active,
                }
            );
            return new CreateTimerResponse.Created(id);
        }
        finally
        {
            if (_threadSafe)
                _lock.Release();
        }
    }

    public async Task<GetTimerResponse> GetTimerAsync(GetTimerRequest req)
    {
        if (req.Claims.Role != "user")
            return new GetTimerResponse.Forbidden();

        if (_threadSafe)
            await _lock.WaitAsync();
        try
        {
            var timer = _state.Items.FirstOrDefault(t => t.Id == req.TimerId);
            if (timer is null)
                return new GetTimerResponse.NotFound();

            return new GetTimerResponse.Ok(timer.Status);
        }
        finally
        {
            if (_threadSafe)
                _lock.Release();
        }
    }

    public void Dispose() => _deadlineCts.Cancel();

    private static TimerState Clone(TimerState s) =>
        new()
        {
            Items = s
                .Items.Select(i => new TimerItem
                {
                    Id = i.Id,
                    Slug = i.Slug,
                    Deadline = i.Deadline,
                    Status = i.Status,
                })
                .ToList(),
        };
}
