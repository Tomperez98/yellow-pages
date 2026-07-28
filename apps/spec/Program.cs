using ConsoleAppFramework;
using Microsoft.Accordant;
using spec;
using Spec.Model;
using Spec.Targets;

var app = ConsoleApp.Create();

app.Add(
    "run",
    async (string url = "https://localhost:5001") =>
    {
        var initialState = new YellowPagesState();

        var spec = YellowPagesSpec.Create();

        // Bind operations to ApiClient (wraps ITarget transport)
        spec.ExecuteWith<ApiClient>()
            .BindAsync<CreateCountryRequest, CreateCountryResponse>(
                "CreateCountry",
                (client, req) => client.CreateCountryAsync("CreateCountry", req)
            )
            .BindAsync<UpdateCountryRequest, UpdateCountryResponse>(
                "UpdateCountry",
                (client, req) => client.UpdateCountryAsync("UpdateCountry", req)
            )
            .BindAsync<DeleteCountryRequest, DeleteCountryResponse>(
                "DeleteCountry",
                (client, req) => client.DeleteCountryAsync("DeleteCountry", req)
            );

        // Register the ApiClient backed by an HTTP target
        var context = spec.CreateTestingContext();
        context.Register(new ApiClient(new HttpTarget(url)));

        // Provide seed inputs for test generation
        var admin = new Claims("admin", "admin", "", "", []);
        var inputs = new InputSet
        {
            spec["CreateCountry"].With(new CreateCountryRequest(admin, "US")),
            spec["CreateCountry"].With(new CreateCountryRequest(admin, "CA")),
        };

        // Generate and run tests
        var testCases = spec.GenerateTests(initialState, inputs);
        var results = await spec.RunTests(context, initialState, testCases);

        foreach (var r in results)
        {
            Console.WriteLine(r.Success ? $"  PASS" : $"  FAIL — {r.LastFailureMessage}");
        }
    }
);

app.Run(args);
