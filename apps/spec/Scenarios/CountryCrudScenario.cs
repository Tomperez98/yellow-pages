using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public class CountryCrudScenario : IScenario
{
    private static readonly Claims Admin = new("admin", "admin", "", "", []);

    public TestSuite BuildTests(Spec<YellowPagesState> spec, YellowPagesState initialState)
    {
        ConfigureDerivations(spec);
        var inputs = BuildInputs(spec);
        return new TestSuite.Sequential(
            spec.GenerateTests(initialState, inputs, new() { MaxDepth = 5 })
        );
    }

    private static void ConfigureDerivations(Spec<YellowPagesState> spec)
    {
        spec.ConfigureDerivations(
            "UpdateCountry",
            Derive
                .From<CreateCountryRequest, CreateCountryResponse, UpdateCountryRequest>(
                    "CreateCountry"
                )
                .When((_, resp) => resp is CreateCountryResponse.Created)
                .As(
                    (req, resp) =>
                        new UpdateCountryRequest(
                            req.Claims,
                            ((CreateCountryResponse.Created)resp).CountryId,
                            req.Code
                        )
                )
        );

        spec.ConfigureDerivations(
            "DeleteCountry",
            Derive
                .From<CreateCountryRequest, CreateCountryResponse, DeleteCountryRequest>(
                    "CreateCountry"
                )
                .When((_, resp) => resp is CreateCountryResponse.Created)
                .As(
                    (req, resp) =>
                        new DeleteCountryRequest(
                            req.Claims,
                            ((CreateCountryResponse.Created)resp).CountryId
                        )
                )
        );
    }

    private static InputSet BuildInputs(Spec<YellowPagesState> spec)
    {
        var create = spec.GetOperation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry"
        );

        return
        [
            create.With(new CreateCountryRequest(Admin, "US"), "Create US"),
            create.With(new CreateCountryRequest(Admin, "CA"), "Create CA"),
            create.With(new CreateCountryRequest(Admin, "MX"), "Create MX"),
            create.With(new CreateCountryRequest(Admin, ""), "Create with empty code"),
        ];
    }
}
