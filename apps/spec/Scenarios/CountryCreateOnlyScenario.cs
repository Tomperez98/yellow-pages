using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public class CountryCreateOnlyScenario : IScenario
{
    private static readonly Claims Admin = new("admin", "admin", "", "", []);

    public TestGenerationOptions? Options => new() { MaxDepth = 3 };

    public InputSet BuildInputs(Spec<YellowPagesState> spec)
    {
        var create = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry"
        );

        return
        [
            create.With(new CreateCountryRequest(Admin, "US"), "Create US"),
            create.With(new CreateCountryRequest(Admin, "US"), "Duplicate US"),
            create.With(new CreateCountryRequest(Admin, "CA"), "Create CA"),
        ];
    }
}
