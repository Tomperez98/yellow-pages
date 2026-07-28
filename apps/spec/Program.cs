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

        // Bind operations to ApiClient (wraps ITarget transport)
        spec.ExecuteWith<ApiClient>()
            .BindAsync<CreateCountryRequest, CreateCountryResponse>(
                "CreateCountry",
                (client, req) => client.CreateCountryAsync(req)
            )
            .BindAsync<UpdateCountryRequest, UpdateCountryResponse>(
                "UpdateCountry",
                (client, req) => client.UpdateCountryAsync(req)
            )
            .BindAsync<DeleteCountryRequest, DeleteCountryResponse>(
                "DeleteCountry",
                (client, req) => client.DeleteCountryAsync(req)
            );

        // Register the ApiClient backed by an HTTP target
        var context = spec.CreateTestingContext();
        var client = new ApiClient(new HttpTarget(url, jwtSecret));
        context.Register(client);

        // Provide seed inputs for test generation
        var admin = new Claims("admin", "admin", "", "", []);
        var createCountry = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry"
        );
        var inputs = new InputSet
        {
            createCountry.With(new CreateCountryRequest(admin, "US"), "Create US"),
            createCountry.With(new CreateCountryRequest(admin, "CA"), "Create CA"),
        };

        // Generate and run tests
        var testCases = spec.GenerateTests(initialState, inputs);
        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async (info) =>
                {
                    await client.ResetAsync();
                },
            }
        );

        foreach (var r in results)
        {
            if (r.Success)
                Console.WriteLine($"  PASS");
            else
            {
                Console.WriteLine($"  FAIL");
                // LastFailureMessage may contain exception objects that fail to serialize;
                // print what we can safely.
                try
                {
                    Console.WriteLine($"    Message: {r.LastFailureMessage}");
                }
                catch
                {
                    Console.WriteLine($"    (failure message not serializable)");
                }
                if (!string.IsNullOrEmpty(r.LogFilePath))
                    Console.WriteLine($"    Log: {r.LogFilePath}");
            }
        }
    }
);

app.Run(args);
