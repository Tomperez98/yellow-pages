using ConsoleAppFramework;
using Microsoft.Accordant;
using spec;
using Spec.Model;
using Spec.Scenarios;
using Spec.Targets;

var app = ConsoleApp.Create();

// ---------------------------------------------------------------------------
// test: run a named scenario against a single target
//   dotnet run -- test --target inmemory --scenario country-crud
//   dotnet run -- test --target http --url https://... --jwt-secret ... --scenario country-crud
// ---------------------------------------------------------------------------
app.Add(
    "test",
    async (
        string target = "inmemory",
        string scenario = "country-crud",
        string? url = null,
        string? jwtSecret = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetImpl = ResolveTarget(target, url, jwtSecret, stdioPath);
        if (!ScenarioRegistry.All.TryGetValue(scenario, out var sc))
            throw new ArgumentException(
                $"Unknown scenario '{scenario}'. Valid: {string.Join(", ", ScenarioRegistry.All.Keys)}"
            );

        var client = new ApiClient(targetImpl);
        var initialState = new YellowPagesState();
        var spec = YellowPagesSpec.Create();

        ApiClient.BindTo(spec);
        var inputs = sc.BuildInputs(spec);
        var genOptions = sc.Options ?? new TestGenerationOptions();

        var ok = await ExecuteTests(spec, initialState, client, inputs, genOptions);
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
        string scenario = "country-crud",
        string? url = null,
        string? jwtSecret = null,
        string? stdioPath = "../../../../stdio/stdio"
    ) =>
    {
        var targetNames = targets.Split(',', StringSplitOptions.TrimEntries);
        var initialState = new YellowPagesState();

        if (!ScenarioRegistry.All.TryGetValue(scenario, out var sc))
            throw new ArgumentException(
                $"Unknown scenario '{scenario}'. Valid: {string.Join(", ", ScenarioRegistry.All.Keys)}"
            );

        var genOptions = sc.Options ?? new TestGenerationOptions();

        var allOk = true;
        foreach (var t in targetNames)
        {
            var targetImpl = ResolveTarget(t, url, jwtSecret, stdioPath);
            var client = new ApiClient(targetImpl);

            // Each target gets a fresh spec: ExecuteWith is stateful,
            // and InputSet captures operation refs from the spec it was built from.
            var targetSpec = YellowPagesSpec.Create();
            ApiClient.BindTo(targetSpec);
            var inputs = sc.BuildInputs(targetSpec);

            Console.WriteLine($"=== {t} Target ===");
            var ok = await ExecuteTests(targetSpec, initialState, client, inputs, genOptions);
            Console.WriteLine();
            allOk &= ok;
        }

        if (targetNames.Length > 1)
            Console.WriteLine(allOk ? "✓ All targets conformance OK" : "✗ Conformance mismatch");
    }
);

// ---------------------------------------------------------------------------
// list-scenarios: print registered scenarios
//   dotnet run -- list-scenarios
// ---------------------------------------------------------------------------
app.Add(
    "list-scenarios",
    () =>
    {
        Console.WriteLine("Scenarios:");
        foreach (var (name, sc) in ScenarioRegistry.All)
        {
            var depth = sc.Options?.MaxDepth.ToString() ?? "default";
            Console.WriteLine($"  {name}  (MaxDepth={depth})");
        }
    }
);

// ---------------------------------------------------------------------------
// list-targets: print available targets
//   dotnet run -- list-targets
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

static ITarget ResolveTarget(string target, string? url, string? jwtSecret, string? stdioPath) =>
    target switch
    {
        "inmemory" => new InMemoryTarget(new InMemoryServer(new YellowPagesState())),
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
    Spec<YellowPagesState> spec,
    YellowPagesState initialState,
    ApiClient client,
    InputSet inputs,
    TestGenerationOptions genOptions
)
{
    var context = spec.CreateTestingContext();
    context.Register(client);

    var testCases = spec.GenerateTests(initialState, inputs, genOptions);
    var results = await spec.RunTests(
        context,
        initialState,
        testCases,
        new TestExecutionOptions { BeforeEachAsync = async _ => await client.ResetAsync() }
    );

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
// Scenario registry — maps name → IScenario.
// To add a scenario: implement IScenario, add one line here.
// ===========================================================================

static class ScenarioRegistry
{
    public static readonly Dictionary<string, IScenario> All = new()
    {
        ["country-crud"] = new CountryCrudScenario(),
        ["country-create-only"] = new CountryCreateOnlyScenario(),
    };
}
