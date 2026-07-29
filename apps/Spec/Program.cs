using ConsoleAppFramework;
using Microsoft.Accordant;
using spec;
using Spec.Model;
using Spec.Scenarios;
using Spec.Targets;
using Spec.Transitions;

var app = ConsoleApp.Create();

// ---------------------------------------------------------------------------
// test: run a named scenario against a single target
//   dotnet run -- test --target inmemory --scenario timer-lifecycle
//   dotnet run -- test --target http --url https://... --scenario timer-lifecycle
// ---------------------------------------------------------------------------
app.Add(
    "test",
    async (
        string target = "inmemory",
        string scenario = "timer-lifecycle",
        bool noLock = false,
        string? url = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetImpl = ResolveTarget(target, url, stdioPath, !noLock);
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
// transition: run a hand-written conformance test against a target
//   dotnet run -- transition --target inmemory --transition timer-lifecycle
//   dotnet run -- transition --target http --url https://... --transition timer-lifecycle
// ---------------------------------------------------------------------------
app.Add(
    "transition",
    async (
        string target = "inmemory",
        string transition = "timer-lifecycle",
        bool noLock = false,
        string? url = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetImpl = ResolveTarget(target, url, stdioPath, !noLock);
        if (!TransitionRegistry.All.TryGetValue(transition, out var tr))
            throw new ArgumentException(
                $"Unknown transition '{transition}'. Valid: {string.Join(", ", TransitionRegistry.All.Keys)}"
            );

        var initialState = new TimerState();
        var spec = TimerSpec.Create();
        var client = new ApiClient(targetImpl);

        await client.ResetAsync();

        await tr.RunAsync(spec, client, initialState);
        Console.WriteLine("✓ Transition passed");
    }
);

// ---------------------------------------------------------------------------
// conformance: run a scenario against multiple targets
//   dotnet run -- conformance --targets inmemory,http --url ...
// ---------------------------------------------------------------------------
app.Add(
    "conformance",
    async (
        string targets = "inmemory,http",
        string scenario = "timer-lifecycle",
        bool noLock = false,
        string? url = null,
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
            var targetImpl = ResolveTarget(t, url, stdioPath, !noLock);
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
// list-transitions: print registered transitions
// ---------------------------------------------------------------------------
app.Add(
    "list-transitions",
    () =>
    {
        Console.WriteLine("Transitions:");
        foreach (var (name, _) in TransitionRegistry.All)
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
    string? stdioPath,
    bool threadSafe = true
) =>
    target switch
    {
        "inmemory" => new InMemoryTarget(new InMemoryServer(new TimerState(), threadSafe)),
        "http" => url is not null
            ? new HttpTarget(url)
            : throw new ArgumentException("--url required for HTTP target"),
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

// ===========================================================================
// Transition registry
// ===========================================================================

static class TransitionRegistry
{
    public static readonly Dictionary<string, ITransition> All = new()
    {
        ["timer-lifecycle"] = new TimerLifecycleTransition(),
    };
}
