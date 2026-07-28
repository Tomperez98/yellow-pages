using Microsoft.Accordant;
using Spec.Model;

namespace Spec.Scenarios;

public class CountryCrudScenario : IScenario
{
    private static readonly Claims Admin = new("admin", "admin", "", "", []);

    public TestGenerationOptions Options => new() { MaxDepth = 5 };

    public InputSet BuildInputs(Spec<YellowPagesState> spec)
    {
        // Auto-derive UpdateCountry/DeleteCountry from a successful CreateCountry
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
