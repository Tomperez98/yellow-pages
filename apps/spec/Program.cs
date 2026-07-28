using ConsoleAppFramework;
using Microsoft.Accordant;
using spec;
using Spec.Model;
using Spec.Targets;

var app = ConsoleApp.Create();

app.Add(
    "run",
    async (string url, string jwtSecret) =>
    {
        var initialState = new YellowPagesState();
        var spec = YellowPagesSpec.Create();
        var client = new ApiClient(new HttpTarget(url, jwtSecret));

        await RunTests(spec, initialState, client);
    }
);

app.Add(
    "conformance",
    async (string target = "inmemory,http", string? url = null, string? jwtSecret = null) =>
    {
        var targets = target.Split(',', StringSplitOptions.TrimEntries);
        var initialState = new YellowPagesState();
        var spec = YellowPagesSpec.Create();

        var allOk = true;

        foreach (var t in targets)
        {
            ITarget targetImpl = t switch
            {
                "inmemory" => new InMemoryTarget(new InMemoryServer(initialState)),
                "http" => url is not null && jwtSecret is not null
                    ? new HttpTarget(url, jwtSecret)
                    : throw new ArgumentException(
                        "--url and --jwt-secret required for HTTP target"
                    ),
                "stdio" => new StdioTarget("../../../../stdio/stdio"),
                _ => throw new ArgumentException(
                    $"Unknown target '{t}'. Valid: inmemory, http, stdio"
                ),
            };

            var client = new ApiClient(targetImpl);

            Console.WriteLine($"=== {t} Target ===");
            var ok = await RunTests(spec, initialState, client);
            Console.WriteLine();
            allOk &= ok;

            if (targetImpl is IDisposable d)
                d.Dispose();
        }

        if (targets.Length > 1)
        {
            Console.WriteLine(allOk ? "✓ All targets conformance OK" : "✗ Conformance mismatch");
        }
    }
);

app.Run(args);

static async Task<bool> RunTests(
    Spec<YellowPagesState> spec,
    YellowPagesState initialState,
    ApiClient client
)
{
    spec.ExecuteWith<ApiClient>()
        .BindAsync<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry",
            (c, req) => c.CreateCountryAsync(req)
        )
        .BindAsync<UpdateCountryRequest, UpdateCountryResponse>(
            "UpdateCountry",
            (c, req) => c.UpdateCountryAsync(req)
        )
        .BindAsync<DeleteCountryRequest, DeleteCountryResponse>(
            "DeleteCountry",
            (c, req) => c.DeleteCountryAsync(req)
        );

    var context = spec.CreateTestingContext();
    context.Register(client);

    var admin = new Claims("admin", "admin", "", "", []);
    var createCountry = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
        "CreateCountry"
    );
    var inputs = new InputSet
    {
        createCountry.With(new CreateCountryRequest(admin, "US"), "Create US"),
        createCountry.With(new CreateCountryRequest(admin, "CA"), "Create CA"),
    };

    var testCases = spec.GenerateTests(initialState, inputs);
    var results = await spec.RunTests(
        context,
        initialState,
        testCases,
        new TestExecutionOptions { BeforeEachAsync = async (_) => await client.ResetAsync() }
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
