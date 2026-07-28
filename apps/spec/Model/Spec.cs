using Microsoft.Accordant;

namespace Spec.Model;

public static class YellowPagesSpec
{
    public static Spec<YellowPagesState> Create()
    {
        var spec = new Spec<YellowPagesState>().WithJsonPrinters();

        // --- CreateCountry ---

        spec.Operation<CreateCountryRequest, CreateCountryResponse>(
            "CreateCountry",
            (req, state) =>
            {
                // --- stateless ---
                if (req.Claims.Role != "admin")
                    // Non-admin callers get NotAuthorized
                    return Expect
                        .That<CreateCountryResponse>(
                            r => r is CreateCountryResponse.NotAuthorized,
                            "only platform admins are authorized to do this action"
                        )
                        .SameState();

                if (string.IsNullOrWhiteSpace(req.Code))
                    // Empty or whitespace code returns InvalidData
                    return Expect
                        .That<CreateCountryResponse>(
                            r => r is CreateCountryResponse.InvalidData,
                            "code cannot be empty"
                        )
                        .SameState();

                // --- state ---
                if (state.Countries.Any(c => c.Code == req.Code))
                    // Duplicate country code returns Conflict
                    return Expect
                        .That<CreateCountryResponse>(
                            r => r is CreateCountryResponse.Conflict,
                            "country already exists"
                        )
                        .SameState();

                // Valid request returns Ok with a non-empty CountryId
                return Expect
                    .That<CreateCountryResponse>(
                        r =>
                            r is CreateCountryResponse.Ok { CountryId: var id } && id != Guid.Empty,
                        "successful creation returns Ok with a valid CountryId"
                    )
                    .ThenState<YellowPagesState>(
                        (resp, s) =>
                        {
                            var id = ((CreateCountryResponse.Ok)resp).CountryId;
                            Invariant.Assert(
                                id.Version == 7,
                                "generated country id is not UUID v7"
                            );
                            s.Countries.Add(new Country { Id = id, Code = req.Code });
                            Invariant.Assert(
                                s.Countries.Select(c => c.Code).Distinct().Count()
                                    == s.Countries.Count,
                                "duplicate country codes"
                            );
                        },
                        mock: () => new CreateCountryResponse.Ok(Guid.CreateVersion7())
                    );
            }
        );

        // --- UpdateCountry ---

        spec.Operation<UpdateCountryRequest, UpdateCountryResponse>(
            "UpdateCountry",
            (req, state) =>
            {
                // --- stateless ---
                if (req.Claims.Role != "admin")
                    // Non-admin callers get NotAuthorized
                    return Expect
                        .That<UpdateCountryResponse>(
                            r => r is UpdateCountryResponse.NotAuthorized,
                            "only platform admins are authorized to do this action"
                        )
                        .SameState();

                if (string.IsNullOrWhiteSpace(req.Code))
                    // Empty or whitespace code returns ValidationFailed
                    return Expect
                        .That<UpdateCountryResponse>(
                            r => r is UpdateCountryResponse.ValidationFailed,
                            "code cannot be empty"
                        )
                        .SameState();

                // --- state ---
                var country = state.Countries.FirstOrDefault(c => c.Id == req.CountryId);
                if (country is null)
                    // Non-existent country ID returns NotFound
                    return Expect
                        .That<UpdateCountryResponse>(
                            r => r is UpdateCountryResponse.NotFound,
                            "country with the given ID does not exist"
                        )
                        .SameState();

                if (state.Countries.Any(c => c.Id != req.CountryId && c.Code == req.Code))
                    // Changing to a code already used by another country returns Conflict
                    return Expect
                        .That<UpdateCountryResponse>(
                            r => r is UpdateCountryResponse.Conflict,
                            "another country already has this code"
                        )
                        .SameState();

                // Valid update returns Ok
                return Expect
                    .That<UpdateCountryResponse>(
                        r => r is UpdateCountryResponse.Ok,
                        "successful update returns Ok"
                    )
                    .ThenState<YellowPagesState>(
                        (_, s) =>
                        {
                            var c = s.Countries.First(c => c.Id == req.CountryId);
                            c.Code = req.Code;
                            Invariant.Assert(
                                s.Countries.Select(c => c.Code).Distinct().Count()
                                    == s.Countries.Count,
                                "duplicate country codes"
                            );
                        },
                        mock: () => new UpdateCountryResponse.Ok()
                    );
            }
        );

        // --- DeleteCountry ---

        spec.Operation<DeleteCountryRequest, DeleteCountryResponse>(
            "DeleteCountry",
            (req, state) =>
            {
                // --- stateless ---
                if (req.Claims.Role != "admin")
                    // Non-admin callers get NotAuthorized
                    return Expect
                        .That<DeleteCountryResponse>(
                            r => r is DeleteCountryResponse.NotAuthorized,
                            "only platform admins are authorized to do this action"
                        )
                        .SameState();

                // --- state ---
                var country = state.Countries.FirstOrDefault(c => c.Id == req.CountryId);
                if (country is null)
                    // Non-existent country ID returns NotFound
                    return Expect
                        .That<DeleteCountryResponse>(
                            r => r is DeleteCountryResponse.NotFound,
                            "country with the given ID does not exist"
                        )
                        .SameState();

                // Valid deletion returns Ok
                return Expect
                    .That<DeleteCountryResponse>(
                        r => r is DeleteCountryResponse.Ok,
                        "successful deletion returns Ok"
                    )
                    .ThenState<YellowPagesState>(
                        (_, s) =>
                        {
                            var removed = s.Countries.RemoveAll(c => c.Id == req.CountryId);
                            Invariant.Assert(removed == 1, "expected exactly one country removed");
                        },
                        mock: () => new DeleteCountryResponse.Ok()
                    );
            }
        );

        // --- Derivations ---

        spec.ConfigureDerivations(
            "UpdateCountry",
            Derive
                .From<CreateCountryRequest, CreateCountryResponse, UpdateCountryRequest>(
                    "CreateCountry"
                )
                .When((_, resp) => resp is CreateCountryResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new UpdateCountryRequest(
                            template.Claims,
                            ((CreateCountryResponse.Ok)resp).CountryId,
                            template.Code
                        )
                )
        );

        spec.ConfigureDerivations(
            "DeleteCountry",
            Derive
                .From<CreateCountryRequest, CreateCountryResponse, DeleteCountryRequest>(
                    "CreateCountry"
                )
                .When((_, resp) => resp is CreateCountryResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new DeleteCountryRequest(
                            template.Claims,
                            ((CreateCountryResponse.Ok)resp).CountryId
                        )
                )
        );

        return spec;
    }
}
