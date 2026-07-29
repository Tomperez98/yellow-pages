using ConsoleAppFramework;
using Microsoft.Accordant;
using spec;
using Spec.Model;
using Spec.Scenarios;
using Spec.Targets;

var app = ConsoleApp.Create();

// ---------------------------------------------------------------------------
// test: run a named scenario against a single target
//   dotnet run -- test --target inmemory --scenario timer-lifecycle
//   dotnet run -- test --target http --url https://... --jwt-secret ... --scenario timer-lifecycle
// ---------------------------------------------------------------------------
app.Add(
    "test",
    async (
        string target = "inmemory",
        string scenario = "timer-lifecycle",
        bool noLock = false,
        string? url = null,
        string? jwtSecret = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetImpl = ResolveTarget(target, url, jwtSecret, stdioPath, !noLock);
        if (!ScenarioRegistry.All.TryGetValue(scenario, out var sc))
            throw new ArgumentException(
                $"Unknown scenario '{scenario}'. Valid: {string.Join(", ", ScenarioRegistry.All.Keys)}"
            );

        var initialState = new TimerState();
        var spec = TimerSpec.Create();

        ApiClient.BindTo(spec);
        var client = new ApiClient(targetImpl);
        var suite = sc.BuildTests(spec, initialState);

        var ok = await ExecuteTests(spec, initialState, client, suite);
        Console.WriteLine(ok ? "✓ All tests passed" : "✗ Some tests failed");
    }
);

// ---------------------------------------------------------------------------
// conformance: run a scenario against multiple targets
//   dotnet run -- conformance --targets inmemory,http --url ... --jwt-secret ...
// ---------------------------------------------------------------------------
app.Add(
    "conformance",
    async (
        string targets = "inmemory,http",
        string scenario = "timer-lifecycle",
        bool noLock = false,
        string? url = null,
        string? jwtSecret = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetNames = targets.Split(',', StringSplitOptions.TrimEntries);
        var initialState = new TimerState();

        if (!ScenarioRegistry.All.TryGetValue(scenario, out var sc))
            throw new ArgumentException(
                $"Unknown scenario '{scenario}'. Valid: {string.Join(", ", ScenarioRegistry.All.Keys)}"
            );

        var allOk = true;
        foreach (var t in targetNames)
        {
            var targetImpl = ResolveTarget(t, url, jwtSecret, stdioPath, !noLock);
            var client = new ApiClient(targetImpl);

            var targetSpec = TimerSpec.Create();
            ApiClient.BindTo(targetSpec);
            var suite = sc.BuildTests(targetSpec, initialState);

            Console.WriteLine($"=== {t} Target ===");
            var ok = await ExecuteTests(targetSpec, initialState, client, suite);
            Console.WriteLine();
            allOk &= ok;
        }

        if (targetNames.Length > 1)
            Console.WriteLine(allOk ? "✓ All targets conformance OK" : "✗ Conformance mismatch");
    }
);

// ---------------------------------------------------------------------------
// list-scenarios: print registered scenarios
// ---------------------------------------------------------------------------
app.Add(
    "list-scenarios",
    () =>
    {
        Console.WriteLine("Scenarios:");
        foreach (var (name, _) in ScenarioRegistry.All)
            Console.WriteLine($"  {name}");
    }
);

// ---------------------------------------------------------------------------
// list-targets: print available targets
// ---------------------------------------------------------------------------
app.Add(
    "list-targets",
    () =>
    {
        Console.WriteLine("Targets: inmemory, http, stdio");
    }
);

app.Run(args);

// ===========================================================================
// Target resolution
// ===========================================================================

static ITarget ResolveTarget(
    string target,
    string? url,
    string? jwtSecret,
    string? stdioPath,
    bool threadSafe = true
) =>
    target switch
    {
        "inmemory" => new InMemoryTarget(new InMemoryServer(new TimerState(), threadSafe)),
        "http" => url is not null && jwtSecret is not null
            ? new HttpTarget(url, jwtSecret)
            : throw new ArgumentException("--url and --jwt-secret required for HTTP target"),
        "stdio" => stdioPath is not null
            ? new StdioTarget(stdioPath)
            : throw new ArgumentException("--stdio-path required for stdio target"),
        _ => throw new ArgumentException(
            $"Unknown target '{target}'. Valid: inmemory, http, stdio"
        ),
    };

// ===========================================================================
// Shared execution
// ===========================================================================

static async Task<bool> ExecuteTests(
    Spec<TimerState> spec,
    TimerState initialState,
    ApiClient client,
    TestSuite suite
)
{
    var context = spec.CreateTestingContext();
    context.Register(client);

    var execOptions = new TestExecutionOptions
    {
        BeforeEachAsync = async _ => await client.ResetAsync(),
    };

    var results = suite switch
    {
        TestSuite.Sequential s => await spec.RunTests(context, initialState, s.Cases, execOptions),
        TestSuite.Concurrent c => await spec.RunTests(context, initialState, c.Cases, execOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(suite)),
    };

    var allPassed = true;
    foreach (var r in results)
    {
        if (r.Success)
        {
            Console.WriteLine("  PASS");
        }
        else
        {
            allPassed = false;
            Console.WriteLine("  FAIL");
            try
            {
                Console.WriteLine($"    Message: {r.LastFailureMessage}");
            }
            catch
            {
                Console.WriteLine("    (failure message not serializable)");
            }
            if (!string.IsNullOrEmpty(r.LogFilePath))
                Console.WriteLine($"    Log: {r.LogFilePath}");
        }
    }

    return allPassed;
}

// ===========================================================================
// Scenario registry
// ===========================================================================

static class ScenarioRegistry
{
    public static readonly Dictionary<string, IScenario> All = new()
    {
        ["timer-lifecycle"] = new TimerLifecycleScenario(),
        ["timer-create-only"] = new TimerCreateOnlyScenario(),
        ["timer-slug-race"] = new TimerSlugRaceScenario(),
    };
}
