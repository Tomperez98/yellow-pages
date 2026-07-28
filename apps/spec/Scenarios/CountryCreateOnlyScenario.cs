using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public class CountryCreateOnlyScenario : IScenario
{
    private static readonly Claims Admin = new("admin", "admin", "", "", []);

    public TestSuite BuildTests(Spec<YellowPagesState> spec, YellowPagesState initialState)
    {
        var create = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry"
        );

        var inputs = new InputSet
        {
            create.With(new CreateCountryRequest(Admin, "US"), "Create US"),
            create.With(new CreateCountryRequest(Admin, "US"), "Duplicate US"),
            create.With(new CreateCountryRequest(Admin, "CA"), "Create CA"),
        };

        return new TestSuite.Sequential(
            spec.GenerateTests(initialState, inputs, new() { MaxDepth = 3 })
        );
    }
}
