# Example: Job Queue (async, invariants, server-generated IDs, triggers)

## Prompt

> I need a job queue. You can submit a job with a name and priority (1-5). Priority must be valid. Job names must be unique. The server assigns a job ID. The job starts as Queued, then a background worker picks it up — it can either Complete or Fail. You can query a job by its ID.

## Protocol.cs

```csharp
namespace Spec.Model;

// --- SubmitJob ---

public record SubmitJobRequest(string Name, int Priority);

public abstract record SubmitJobResponse
{
    private SubmitJobResponse() { }

    public sealed record Created(Guid JobId) : SubmitJobResponse;

    public sealed record Conflict : SubmitJobResponse;

    public sealed record BadRequest : SubmitJobResponse;
}

// --- GetJob ---

public record GetJobRequest(Guid JobId);

public abstract record GetJobResponse
{
    private GetJobResponse() { }

    public sealed record Ok(JobStatus Status) : GetJobResponse;

    public sealed record NotFound : GetJobResponse;
}
```

## State.cs

```csharp
using Microsoft.Accordant;

namespace Spec.Model;

public enum JobStatus
{
    Queued,
    Completed,
    Failed,
}

[State]
public partial class JobEntry : State
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
}

[State]
public partial class JobState : State
{
    public Dictionary<Guid, JobEntry> Jobs { get; set; } = [];
}
```

## Operations.cs

```csharp
using Microsoft.Accordant;

namespace Spec.Model;

public static class JobSpec
{
    public static Spec<JobState> Create()
    {
        var spec = new Spec<JobState>().WithJsonPrinters();

        spec.Operation<SubmitJobRequest, SubmitJobResponse>(
            "SubmitJob",
            (req, state) =>
            {
                // 1. Input-only validations first
                if (string.IsNullOrWhiteSpace(req.Name))
                    return Expect
                        .That<SubmitJobResponse>(
                            r => r is SubmitJobResponse.BadRequest,
                            "job name cannot be empty")
                        .SameState();

                if (req.Priority < 1 || req.Priority > 5)
                    return Expect
                        .That<SubmitJobResponse>(
                            r => r is SubmitJobResponse.BadRequest,
                            "priority must be between 1 and 5")
                        .SameState();

                // 2. State-dependent validations
                if (state.Jobs.Values.Any(j => j.Name == req.Name))
                    return Expect
                        .That<SubmitJobResponse>(
                            r => r is SubmitJobResponse.Conflict,
                            "a job with this name already exists")
                        .SameState();

                // 3. Success — server generates ID, background worker starts
                return Expect
                    .That<SubmitJobResponse>(
                        r => r is SubmitJobResponse.Created { JobId: var id } && id != Guid.Empty,
                        "job submitted, returns Created with job ID")
                    .ThenState<JobState>(
                        (resp, s) =>
                        {
                            var id = ((SubmitJobResponse.Created)resp).JobId;
                            s.Jobs[id] = new JobEntry
                            {
                                Id = id,
                                Name = req.Name,
                                Priority = req.Priority,
                                Status = JobStatus.Queued,
                            };
                            Invariant.Assert(
                                s.Jobs.Values.Select(j => j.Name).Distinct().Count() == s.Jobs.Count,
                                "duplicate job names in state");
                            Invariant.Assert(
                                s.Jobs.Values.All(j => j.Priority is >= 1 and <= 5),
                                "all jobs must have valid priority");
                        },
                        mock: () => new SubmitJobResponse.Created(Guid.CreateVersion7()))
                    .Triggers(
                        AsyncOperation.Create<JobState>(
                            isTerminal: s =>
                            {
                                var job = s.Jobs.Values.First(j => j.Name == req.Name);
                                return job.Status != JobStatus.Queued;
                            },
                            transitions:
                            [
                                next =>
                                {
                                    var job = next.Jobs.Values.First(j => j.Name == req.Name);
                                    job.Status = JobStatus.Completed;
                                },
                                next =>
                                {
                                    var job = next.Jobs.Values.First(j => j.Name == req.Name);
                                    job.Status = JobStatus.Failed;
                                },
                            ]
                        ));
            });

        spec.Operation<GetJobRequest, GetJobResponse>(
            "GetJob",
            (req, state) =>
            {
                if (!state.Jobs.TryGetValue(req.JobId, out var job))
                    return Expect
                        .That<GetJobResponse>(
                            r => r is GetJobResponse.NotFound,
                            "job not found")
                        .SameState();

                return Expect
                    .That<GetJobResponse>(
                        r => r is GetJobResponse.Ok { Status: var s } && s == job.Status,
                        $"returns job status {job.Status}")
                    .SameState();
            });

        return spec;
    }
}
```
