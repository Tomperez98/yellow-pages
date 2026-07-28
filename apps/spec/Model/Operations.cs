namespace Spec.Model;

// --- Claims ---

public record OrgMembership(string OrgId, string Role);

public record Claims(string Sub, string Role, string OrgId, string OrgRole, OrgMembership[] Orgs);

// --- Country ---

public record CreateCountryRequest(Claims Claims, string Code);

public abstract record CreateCountryResponse
{
    private CreateCountryResponse() { }

    public sealed record Created(Guid CountryId) : CreateCountryResponse;

    public sealed record Conflict : CreateCountryResponse;

    public sealed record BadRequest : CreateCountryResponse;

    public sealed record Forbidden : CreateCountryResponse;
}

public record UpdateCountryRequest(Claims Claims, Guid CountryId, string Code);

public abstract record UpdateCountryResponse
{
    private UpdateCountryResponse() { }

    public sealed record Ok : UpdateCountryResponse;

    public sealed record NotFound : UpdateCountryResponse;

    public sealed record Conflict : UpdateCountryResponse;

    public sealed record BadRequest : UpdateCountryResponse;

    public sealed record Forbidden : UpdateCountryResponse;
}

public record DeleteCountryRequest(Claims Claims, Guid CountryId);

public abstract record DeleteCountryResponse
{
    private DeleteCountryResponse() { }

    public sealed record Ok : DeleteCountryResponse;

    public sealed record NotFound : DeleteCountryResponse;

    public sealed record Forbidden : DeleteCountryResponse;
}
