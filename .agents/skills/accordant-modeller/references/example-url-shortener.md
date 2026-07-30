# Example: URL Shortener (CRUD, optional fields, server-generated IDs)

## Prompt

> I need a spec for a URL shortener. You can create a short link by giving it a URL and a custom slug (optional). If the slug is taken, it's a conflict. If no slug is given, the server generates one. You can also look up a short link by slug — if it doesn't exist, 404. And you can delete a short link by slug.

## Protocol.cs

```csharp
namespace Spec.Model;

public record CreateLinkRequest(string Url, string? Slug);

public abstract record CreateLinkResponse
{
    private CreateLinkResponse() { }

    public sealed record Created(string Slug) : CreateLinkResponse;

    public sealed record Conflict : CreateLinkResponse;

    public sealed record BadRequest : CreateLinkResponse;
}

public record GetLinkRequest(string Slug);

public abstract record GetLinkResponse
{
    private GetLinkResponse() { }

    public sealed record Ok(string Url) : GetLinkResponse;

    public sealed record NotFound : GetLinkResponse;
}

public record DeleteLinkRequest(string Slug);

public abstract record DeleteLinkResponse
{
    private DeleteLinkResponse() { }

    public sealed record NoContent : DeleteLinkResponse;

    public sealed record NotFound : DeleteLinkResponse;
}
```

## State.cs

```csharp
using Microsoft.Accordant;

namespace Spec.Model;

[State]
public partial class LinkEntry : State
{
    public string Slug { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

[State]
public partial class ShortenerState : State
{
    public Dictionary<string, LinkEntry> Links { get; set; } = [];
}
```

## Operations.cs

```csharp
using Microsoft.Accordant;

namespace Spec.Model;

public static class ShortenerSpec
{
    public static Spec<ShortenerState> Create()
    {
        var spec = new Spec<ShortenerState>().WithJsonPrinters();

        spec.Operation<CreateLinkRequest, CreateLinkResponse>(
            "CreateLink",
            (req, state) =>
            {
                // 1. Input-only validations first
                if (string.IsNullOrWhiteSpace(req.Url))
                    return Expect
                        .That<CreateLinkResponse>(
                            r => r is CreateLinkResponse.BadRequest,
                            "URL cannot be empty")
                        .SameState();

                // 2. State-dependent validations
                if (req.Slug is not null && state.Links.ContainsKey(req.Slug))
                    return Expect
                        .That<CreateLinkResponse>(
                            r => r is CreateLinkResponse.Conflict,
                            $"slug '{req.Slug}' already taken")
                        .SameState();

                // 3. Success — server generates slug if none provided
                return Expect
                    .That<CreateLinkResponse>(
                        r => r is CreateLinkResponse.Created { Slug: var s } && !string.IsNullOrEmpty(s),
                        "returns Created with the assigned slug")
                    .ThenState<ShortenerState>(
                        (resp, s) =>
                        {
                            var slug = ((CreateLinkResponse.Created)resp).Slug;
                            s.Links[slug] = new LinkEntry { Slug = slug, Url = req.Url };
                            Invariant.Assert(
                                s.Links.Values.Select(l => l.Slug).Distinct().Count() == s.Links.Count,
                                "duplicate slugs in state");
                        },
                        mock: () => new CreateLinkResponse.Created(
                            req.Slug ?? Guid.NewGuid().ToString("N")[..8]));
            });

        spec.Operation<GetLinkRequest, GetLinkResponse>(
            "GetLink",
            (req, state) =>
            {
                if (!state.Links.TryGetValue(req.Slug, out var link))
                    return Expect
                        .That<GetLinkResponse>(r => r is GetLinkResponse.NotFound,
                            "link not found")
                        .SameState();

                return Expect
                    .That<GetLinkResponse>(
                        r => r is GetLinkResponse.Ok { Url: var u } && u == link.Url,
                        $"returns URL '{link.Url}'")
                    .SameState();
            });

        spec.Operation<DeleteLinkRequest, DeleteLinkResponse>(
            "DeleteLink",
            (req, state) =>
            {
                if (!state.Links.ContainsKey(req.Slug))
                    return Expect
                        .That<DeleteLinkResponse>(r => r is DeleteLinkResponse.NotFound,
                            "link not found")
                        .SameState();

                return Expect
                    .That<DeleteLinkResponse>(r => r is DeleteLinkResponse.NoContent,
                        "link deleted")
                    .ThenState<ShortenerState>(next =>
                    {
                        next.Links.Remove(req.Slug);
                        Invariant.Assert(
                            next.Links.Values.Select(l => l.Slug).Distinct().Count() == next.Links.Count,
                            "duplicate slugs in state");
                    });
            });

        return spec;
    }
}
```
