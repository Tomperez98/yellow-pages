using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

/// <summary>
/// TOCTOU race: two admins concurrently create the same country code.
/// If both succeed, the system allowed a duplicate — a linearizability violation.
/// </summary>
public class CountryCreateRaceScenario : IScenario
{
    private static readonly Claims Admin = new("admin", "admin", "", "", []);

    public TestSuite BuildTests(Spec<YellowPagesState> spec, YellowPagesState initialState)
    {
        var create = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry"
        );

        var inputs = new InputSet
        {
            create.With(new CreateCountryRequest(Admin, "US"), "Admin A creates US"),
            create.With(new CreateCountryRequest(Admin, "US"), "Admin B creates US"),
        };

        return new TestSuite.Concurrent(
            spec.GenerateConcurrentTests(
                initialState,
                inputs,
                new() { MaxDepth = 3, MaxConcurrencyLevel = 2 }
            )
        );
    }
}
